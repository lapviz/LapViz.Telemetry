using System;
using System.Collections.Generic;
using System.Linq;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Aggregates timing events for a single driver/device context,
/// and exposes convenience properties like last lap, best lap,
/// best sectors, theoretical best, and rolling best.
/// </summary>
public class SessionEvents
{
    /// <summary>
    /// Optional identifier of the component that generated the events.
    /// Example: "LapViz.Stopwatch 1.2".
    /// </summary>
    public string Generator { get; set; }

    /// <summary>
    /// Version of the generator or schema of the payload.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Circuit configuration used to generate or interpret events
    /// (number of sectors, gates, orientation, etc.).
    /// </summary>
    public CircuitConfiguration CircuitConfiguration { get; set; }

    /// <summary>
    /// True if this producer is a live telemetry device, false if it is a derived source
    /// like a file importer, simulator, or manual entry.
    /// </summary>
    public bool IsTelemetryDevice { get; set; }

    /// <summary>
    /// Adds a new event to the collection as-is.
    /// No deduplication or validation is performed here.
    /// </summary>
    public void AddEvent(SessionDataEvent newEvent)
    {
        _events.Add(newEvent);
    }

    /// <summary>
    /// Returns the last event in the list, or null if none.
    /// </summary>
    public SessionDataEvent LastEvent => Events.LastOrDefault();

    /// <summary>
    /// Current lap time measured from the timestamp of the last lap,
    /// or, if no lap yet, from the timestamp of the last event.
    /// Returns TimeSpan.Zero if there is no event.
    /// </summary>
    public TimeSpan CurrentLapTime =>
        LastLap != null
            ? DateTimeOffset.Now - LastLap.Timestamp
            : LastEvent != null
                ? DateTimeOffset.Now - LastEvent.Timestamp
                : TimeSpan.Zero;

    /// <summary>
    /// The most recent lap event by lap number. Returns null if no lap.
    /// </summary>
    public SessionDataEvent LastLap
    {
        get
        {
            var laps = Events.Where(x => x.Type == SessionEventType.Lap);

            if (!laps.Any())
                return null;

            return laps.Aggregate((last, current) =>
                current.LapNumber > last.LapNumber ? current : last);
        }
    }

    /// <summary>
    /// The best lap event by time among valid laps (LapNumber > 0).
    /// Returns null if none.
    /// </summary>
    public SessionDataEvent BestLap
    {
        get
        {
            var laps = Events
                .Where(x => x.Type == SessionEventType.Lap && x.LapNumber > 0);

            if (!laps.Any())
                return null;

            return laps.Aggregate((best, current) =>
                best == null || current.Time < best.Time ? current : best);
        }
    }

    /// <summary>
    /// Last sector event observed in chronological order,
    /// resolved by greatest LapNumber then greatest Sector index.
    /// Returns null if no sector with a positive time exists.
    /// </summary>
    public SessionDataEvent LastSector
    {
        get
        {
            SessionDataEvent last = null;

            foreach (var e in Events)
            {
                if (e.Type != SessionEventType.Sector || e.Time <= TimeSpan.Zero)
                    continue;

                if (last == null ||
                    e.LapNumber > last.LapNumber ||
                    (e.LapNumber == last.LapNumber && e.Sector > last.Sector))
                {
                    last = e;
                }
            }

            return last;
        }
    }

    /// <summary>
    /// Best sector times across all laps. Key is sector index,
    /// value is the best SessionDataEvent achieving that time.
    /// </summary>
    public IDictionary<int, SessionDataEvent> BestSectors
    {
        get
        {
            return Events
                .Where(x => x.Type == SessionEventType.Sector && x.Time > TimeSpan.Zero)
                .GroupBy(x => x.Sector)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        SessionDataEvent best = null;
                        foreach (var e in g)
                        {
                            if (best == null || e.Time < best.Time)
                                best = e;
                        }
                        return best;
                    });
        }
    }

    // Internal backing store. Kept mutable internally, but exposed as readonly.
    private readonly List<SessionDataEvent> _events = new List<SessionDataEvent>();

    /// <summary>
    /// All events recorded for this entity, in append order.
    /// Exposed as <see cref="IReadOnlyList{T}"/> to prevent external modification.
    /// </summary>
    public IReadOnlyList<SessionDataEvent> Events => _events;

    /// <summary>
    /// Optional recorded maximum speed for this entity, if tracked separately.
    /// </summary>
    public double? MaxSpeed { get; set; }

    /// <summary>
    /// Theoretical best lap time computed as the sum of best sector times,
    /// each best taken independently across the whole session.
    /// Returns null if no sector times are available.
    /// </summary>
    public TimeSpan? Theorical
    {
        get
        {
            var bestSectors = Events
                .Where(x => x.Type == SessionEventType.Sector && x.LapNumber > 0 && x.Time > TimeSpan.Zero)
                .GroupBy(x => x.Sector)
                .Select(g => g.Min(e => e.Time))
                .ToList();

            if (bestSectors.Count == 0)
                return null;

            return TimeSpan.FromTicks(bestSectors.Sum(t => t.Ticks));
        }
    }

    /// <summary>
    /// Rolling best lap computed by summing a sliding window of the most recent N sector times
    /// in chronological order, where N equals the number of sectors per lap.
    /// Returns null if there are not enough sectors to form a lap.
    /// </summary>
    public TimeSpan? Rolling
    {
        get
        {
            var sectors = Events
                .Where(x => x.Type == SessionEventType.Sector && x.LapNumber > 0)
                .OrderBy(x => x.Timestamp)
                .Select(x => x.Time)
                .ToList();

            if (sectors.Count == 0)
                return null;

            int sectorCount = Events
                .Where(x => x.Type == SessionEventType.Sector)
                .Select(x => x.Sector)
                .DefaultIfEmpty()
                .Max();

            if (sectorCount <= 0 || sectors.Count < sectorCount)
                return null;

            TimeSpan bestLap = TimeSpan.MaxValue;
            var rollingWindow = new Queue<TimeSpan>();

            foreach (var sectorTime in sectors)
            {
                rollingWindow.Enqueue(sectorTime);

                if (rollingWindow.Count > sectorCount)
                    rollingWindow.Dequeue();

                if (rollingWindow.Count == sectorCount)
                {
                    var totalTicks = rollingWindow.Sum(t => t.Ticks);
                    var currentLap = new TimeSpan(totalTicks);
                    if (currentLap < bestLap)
                        bestLap = currentLap;
                }
            }

            return bestLap;
        }
    }

    /// <summary>
    /// Returns true if the provided sector event is the best so far
    /// for its sector index. If we have no best recorded yet, returns true.
    /// </summary>
    public bool IsBestSector(SessionDataEvent sector)
    {
        if (sector == null || sector.Time <= TimeSpan.Zero)
            return false;

        if (!BestSectors.TryGetValue(sector.Sector, out var best))
            return true;

        return best == null || best.Time <= TimeSpan.Zero || sector.Time <= best.Time;
    }
}
