using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents the full state of a race session.
/// Composes a <see cref="SessionData"/> for aggregated timing with race-specific
/// metadata such as flags, session info, announcements, and schedule.
/// <para>
/// This is the authoritative "game state" that a server maintains and
/// transmits to clients as snapshots or deltas.
/// </para>
/// </summary>
public class RaceSessionState
{
    private readonly ConcurrentDictionary<string, object> _entryLocks =
        new ConcurrentDictionary<string, object>();

    /// <summary>
    /// Creates a new empty race session state.
    /// </summary>
    public RaceSessionState()
    {
        Entries = new ConcurrentDictionary<string, RaceEntry>();
        Announcements = new List<RaceAnnouncement>();
        Schedule = new List<RaceScheduleEntry>();
        Links = new List<RaceLink>();
    }

    // ── Session identity ─────────────────────────────────────

    /// <summary>
    /// Name of the track or circuit (e.g. "Spa-Francorchamps").
    /// </summary>
    public string TrackName { get; set; }

    /// <summary>
    /// Track length in kilometers.
    /// </summary>
    public double TrackLength { get; set; }

    /// <summary>
    /// Name of the event (e.g. "IAME Series Benelux").
    /// </summary>
    public string EventName { get; set; }

    /// <summary>
    /// Name of the class or group (e.g. "Senior").
    /// </summary>
    public string GroupName { get; set; }

    /// <summary>
    /// Name of the current run or session (e.g. "Race 1").
    /// </summary>
    public string RunName { get; set; }

    /// <summary>
    /// Type of the current run.
    /// </summary>
    public RaceRunType RunType { get; set; }

    // ── Timing ────────────────────────────────────────────────

    /// <summary>
    /// Number of timing sectors on the track.
    /// </summary>
    public int SectorCount { get; set; }

    /// <summary>
    /// Elapsed race or session time.
    /// </summary>
    public TimeSpan? RaceTime { get; set; }

    /// <summary>
    /// Remaining time. Null if lap-based.
    /// </summary>
    public TimeSpan? TimeToGo { get; set; }

    /// <summary>
    /// Current local time of day at the track.
    /// </summary>
    public DateTimeOffset? TimeOfDay { get; set; }

    /// <summary>
    /// Current lap number of the leader.
    /// </summary>
    public int LeaderLap { get; set; }

    /// <summary>
    /// Alias for <see cref="LeaderLap"/>. Used by timing sources that
    /// refer to the session lap count simply as "Laps".
    /// </summary>
    public int Laps { get => LeaderLap; set => LeaderLap = value; }

    /// <summary>
    /// Total laps in the race. 0 if time-based.
    /// </summary>
    public int TotalLaps { get; set; }

    // ── Flag ──────────────────────────────────────────────────

    /// <summary>
    /// Current track flag status.
    /// </summary>
    public RaceFlag Flag { get; set; }

    // ── Best lap info ─────────────────────────────────────────

    /// <summary>
    /// Session best lap time as provided by the timing source.
    /// May differ from the computed <see cref="BestLap"/> property
    /// when the source has authoritative data.
    /// </summary>
    public TimeSpan? BestLapTime { get; set; }

    /// <summary>
    /// Racing number of the driver who set the session best lap.
    /// </summary>
    public string BestLapByNumber { get; set; }

    /// <summary>
    /// Display name of the driver who set the session best lap.
    /// </summary>
    public string BestLapBy { get; set; }

    // ── Display fields ────────────────────────────────────────

    /// <summary>
    /// Number of currently connected viewers.
    /// </summary>
    public int ViewerCount { get; set; }

    /// <summary>
    /// Overall best sector times as provided by the timing source,
    /// indexed by sector number. May differ from <see cref="BestSectors"/>.
    /// </summary>
    public TimeSpan?[] BestSectorTimes { get; set; } = Array.Empty<TimeSpan?>();

    /// <summary>
    /// Name of the current leader (display only, source-specific).
    /// </summary>
    public string Leader { get; set; }

    /// <summary>
    /// Average speed of the leader (display only, source-specific).
    /// </summary>
    public string LeaderAvgSpeed { get; set; }

    /// <summary>
    /// Margin of victory for the leader (display only, source-specific).
    /// </summary>
    public string LeaderMargin { get; set; }

    // ── Weather ───────────────────────────────────────────────

    /// <summary>
    /// Current weather conditions at the track, if available.
    /// </summary>
    public WeatherInfo Weather { get; set; }

    /// <summary>
    /// Track surface condition.
    /// </summary>
    public TrackCondition TrackCondition { get; set; }

    // ── Entries (competitors) ─────────────────────────────────

    /// <summary>
    /// All race entries keyed by driver/car number.
    /// Thread-safe for concurrent reads and writes.
    /// </summary>
    public ConcurrentDictionary<string, RaceEntry> Entries { get; }

    /// <summary>
    /// Gets or creates a <see cref="RaceEntry"/> for the given key (typically driver number).
    /// </summary>
    public RaceEntry GetOrCreateEntry(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("key must be non-empty.", nameof(key));

        return Entries.GetOrAdd(key, _ => new RaceEntry());
    }

    /// <summary>
    /// Executes a thread-safe action against a specific race entry.
    /// Creates the entry if it does not exist yet.
    /// </summary>
    public void WithEntry(string key, Action<RaceEntry> action)
    {
        if (action == null) return;

        var entry = Entries.GetOrAdd(key, _ => new RaceEntry());
        var entryLock = _entryLocks.GetOrAdd(key, _ => new object());

        lock (entryLock)
        {
            action(entry);
        }
    }

    // ── Computed bests (across all entries) ───────────────────

    /// <summary>
    /// Best lap time across all entries. Null if no valid laps.
    /// </summary>
    public TimeSpan? BestLap
    {
        get
        {
            var snapshot = Entries.Values.ToArray();
            return snapshot
                .Select(e => e.Timing.BestLap)
                .Where(lap => lap != null && lap.Time > TimeSpan.Zero)
                .OrderBy(lap => lap.Time)
                .Select(lap => (TimeSpan?)lap.Time)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Best sector times across all entries.
    /// Key is sector index, value is the best time.
    /// </summary>
    public IDictionary<int, TimeSpan?> BestSectors
    {
        get
        {
            var snapshot = Entries.Values.ToArray();
            return snapshot
                .SelectMany(e => e.Timing.BestSectors ?? new Dictionary<int, SessionDataEvent>())
                .Where(kvp => kvp.Value != null && kvp.Value.Time > TimeSpan.Zero)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(
                    g => g.Key,
                    g => (TimeSpan?)g.Min(kvp => kvp.Value.Time)
                );
        }
    }

    // ── Ancillary data ────────────────────────────────────────

    /// <summary>
    /// Race control or event announcements (penalties, track status, etc.).
    /// </summary>
    public IList<RaceAnnouncement> Announcements { get; set; }

    /// <summary>
    /// Scheduled sessions for the event (timetable).
    /// </summary>
    public IList<RaceScheduleEntry> Schedule { get; set; }

    /// <summary>
    /// External links related to the event (results, live stream, etc.).
    /// </summary>
    public IList<RaceLink> Links { get; set; }

    // ── Metadata ──────────────────────────────────────────────

    /// <summary>
    /// UTC timestamp of the last update to this state.
    /// </summary>
    public DateTime LastUpdated { get; set; }
}
