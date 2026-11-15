using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Timing;

/// <summary>
/// The session of one competitor to put on a common clock with others (<see cref="SessionTimeline"/>): its laps as
/// recorded by its device (lap timer, data logger, phone).
/// </summary>
public sealed class TimelineSession
{
    /// <summary>Unique key of the competitor in the timeline.</summary>
    public string Key { get; set; } = "";

    /// <summary>Racing number, when known.</summary>
    public string Number { get; set; } = "";

    /// <summary>Name shown.</summary>
    public string Name { get; set; } = "";

    /// <summary>Class, when known.</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Circuit code of the session (sessions of one run share it), null when unknown.</summary>
    public string? CircuitCode { get; set; }

    /// <summary>
    /// The events of the session: its laps (<see cref="SessionEventType.Lap"/>) with the time of the finish line crossing
    /// that ends them (<see cref="SessionDataEvent.Timestamp"/>) and the lap time. Other events are ignored.
    /// </summary>
    public IReadOnlyList<SessionDataEvent> Events { get; set; } = Array.Empty<SessionDataEvent>();

    /// <summary>A correction of the device clock set by hand, added to every time of the session.</summary>
    public TimeSpan Offset { get; set; }

    /// <summary>The laps (completed, with a time), in order.</summary>
    internal IEnumerable<SessionDataEvent> Laps =>
        Events.Where(e => e.Type == SessionEventType.Lap && e.Time > TimeSpan.Zero).OrderBy(e => e.Timestamp);

    /// <summary>
    /// The time span of the session on its own clock (manual offset included): from the start of its first lap to the end
    /// of its last one; null without laps.
    /// </summary>
    public (DateTimeOffset Start, DateTimeOffset End)? Span
    {
        get
        {
            var laps = Laps.ToList();
            if (laps.Count == 0)
                return null;
            return (laps[0].Timestamp - laps[0].Time + Offset, laps[laps.Count - 1].Timestamp + Offset);
        }
    }
}

/// <summary>Options of <see cref="SessionTimeline.Build"/>.</summary>
public sealed class TimelineOptions
{
    /// <summary>The timing rules: practice or qualifying (by the best lap), race (by the laps). Practice by default.</summary>
    public TimingRules Rules { get; set; } = TimingRules.Practice();

    /// <summary>
    /// Find device clocks that are off by whole days, hours or half hours (local time written as UTC, no date) and correct
    /// them on the other sessions. True by default.
    /// </summary>
    public bool AlignClocks { get; set; } = true;
}

/// <summary>The standing of a competitor at a crossing of the timeline.</summary>
public sealed class TimelineStanding
{
    internal TimelineStanding(string key, int position, int laps, TimeSpan? bestLap, TimeSpan? gap, int gapLaps)
    {
        Key = key;
        Position = position;
        Laps = laps;
        BestLap = bestLap;
        Gap = gap;
        GapLaps = gapLaps;
    }

    /// <summary>Key of the competitor.</summary>
    public string Key { get; }

    /// <summary>Position, from 1.</summary>
    public int Position { get; }

    /// <summary>Laps completed.</summary>
    public int Laps { get; }

    /// <summary>Best lap so far.</summary>
    public TimeSpan? BestLap { get; }

    /// <summary>Gap to the first (time), null for the first or when given in laps.</summary>
    public TimeSpan? Gap { get; }

    /// <summary>Gap to the first in laps (race), 0 otherwise.</summary>
    public int GapLaps { get; }
}

/// <summary>A finish line crossing of the timeline, with the standings after it.</summary>
public sealed class TimelineCrossing
{
    internal TimelineCrossing(DateTimeOffset time, string key, int lap, TimeSpan? lapTime, IReadOnlyList<TimelineStanding> standings)
    {
        Time = time;
        Key = key;
        Lap = lap;
        LapTime = lapTime;
        Standings = standings;
    }

    /// <summary>When the line was crossed, on the common clock.</summary>
    public DateTimeOffset Time { get; }

    /// <summary>Key of the competitor.</summary>
    public string Key { get; }

    /// <summary>The lap completed (0: the crossing that starts the first lap).</summary>
    public int Lap { get; }

    /// <summary>The time of the lap completed, null for the start.</summary>
    public TimeSpan? LapTime { get; }

    /// <summary>The standings after the crossing, by position.</summary>
    public IReadOnlyList<TimelineStanding> Standings { get; }

    /// <summary>The position of the competitor after the crossing, 0 when not classified.</summary>
    public int Position => Standings.FirstOrDefault(s => s.Key == Key)?.Position ?? 0;
}

/// <summary>
/// Several sessions recorded on the same circuit at the same time (the lap timers of the drivers of one run) put on one
/// clock, and the standings over time: who led, the gaps, at each finish line crossing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Runs.</b> <see cref="GroupRuns"/> finds the sessions of one run: same circuit, time spans that overlap (after their
/// manual offsets).
/// </para>
/// <para>
/// <b>Clocks.</b> Satellite time is accurate, but some devices write local time as if it were UTC, or no date. With
/// <see cref="TimelineOptions.AlignClocks"/>, a session that does not overlap the reference session (the one that overlaps
/// the most others) is moved by the smallest whole number of days, hours or half hours that makes it overlap it
/// (<see cref="DetectOffset"/>). A clock off by seconds cannot be told from the laps alone: set
/// <see cref="TimelineSession.Offset"/>.
/// </para>
/// <para>
/// <b>Standings.</b> The laps become finish line passings of a <see cref="PassingTimingSession"/> (the first lap's start
/// from its end minus its time); the timing rules rank them (best lap, or laps in a race).
/// </para>
/// </remarks>
public sealed class SessionTimeline
{
    private SessionTimeline(IReadOnlyList<TimelineSession> sessions, IReadOnlyDictionary<string, TimeSpan> offsets,
        PassingTimingSession timing, IReadOnlyList<TimelineCrossing> crossings)
    {
        Sessions = sessions;
        Offsets = offsets;
        Timing = timing;
        Crossings = crossings;
        Start = crossings.Count > 0 ? crossings[0].Time : default;
        End = crossings.Count > 0 ? crossings[crossings.Count - 1].Time : default;
    }

    /// <summary>The sessions of the timeline.</summary>
    public IReadOnlyList<TimelineSession> Sessions { get; }

    /// <summary>The correction applied to the clock of each session (manual and found), by key.</summary>
    public IReadOnlyDictionary<string, TimeSpan> Offsets { get; }

    /// <summary>The timing of the run (laps, classification).</summary>
    public PassingTimingSession Timing { get; }

    /// <summary>The finish line crossings in time order, with the standings after each.</summary>
    public IReadOnlyList<TimelineCrossing> Crossings { get; }

    /// <summary>The first crossing (the start of the first lap).</summary>
    public DateTimeOffset Start { get; }

    /// <summary>The last crossing.</summary>
    public DateTimeOffset End { get; }

    /// <summary>
    /// The runs among <paramref name="sessions"/>: sessions of the same circuit whose time spans overlap, or are less than
    /// <paramref name="maxSeparation"/> apart. Sessions without laps are left out. Earliest run first.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TimelineSession>> GroupRuns(IEnumerable<TimelineSession> sessions, TimeSpan maxSeparation)
    {
        var runs = new List<IReadOnlyList<TimelineSession>>();
        foreach (var circuit in sessions.Where(s => s.Span != null).GroupBy(s => s.CircuitCode ?? "", StringComparer.OrdinalIgnoreCase))
        {
            List<TimelineSession>? run = null;
            var runEnd = DateTimeOffset.MinValue;
            foreach (var session in circuit.OrderBy(s => s.Span!.Value.Start))
            {
                var span = session.Span!.Value;
                if (run == null || span.Start > runEnd + maxSeparation)
                {
                    run = new List<TimelineSession>();
                    runs.Add(run);
                    runEnd = span.End;
                }

                run.Add(session);
                if (span.End > runEnd)
                    runEnd = span.End;
            }
        }

        return runs.OrderBy(r => r[0].Span!.Value.Start).ToList();
    }

    /// <summary>
    /// The smallest shift of whole days (up to one), hours (up to 14) and half or quarter hours that makes the span
    /// <paramref name="session"/> overlap <paramref name="reference"/> by at least half of the shorter one; zero when it
    /// already does, null when no such shift exists (another run, or a clock off by less than that).
    /// </summary>
    public static TimeSpan? DetectOffset((DateTimeOffset Start, DateTimeOffset End) session, (DateTimeOffset Start, DateTimeOffset End) reference)
    {
        if (Overlaps(session, reference))
            return TimeSpan.Zero;

        // Whole hours first (a time zone), then half and quarter hours, the smallest first
        var candidates = new List<(TimeSpan Shift, int Kind)>();
        foreach (var days in new[] { 0, -1, 1 })
        {
            for (var quarters = -14 * 4; quarters <= 14 * 4; quarters++)
            {
                var kind = quarters % 4 == 0 ? 0 : quarters % 2 == 0 ? 1 : 2;
                candidates.Add((TimeSpan.FromDays(days) + TimeSpan.FromMinutes(15 * quarters), kind));
            }
        }

        foreach (var (shift, _) in candidates.OrderBy(c => c.Kind).ThenBy(c => Math.Abs(c.Shift.Ticks)).ThenBy(c => c.Shift.Ticks))
        {
            if (shift != TimeSpan.Zero && Overlaps((session.Start + shift, session.End + shift), reference))
                return shift;
        }

        return null;
    }

    private static bool Overlaps((DateTimeOffset Start, DateTimeOffset End) a, (DateTimeOffset Start, DateTimeOffset End) b)
    {
        var overlap = (a.End < b.End ? a.End : b.End) - (a.Start > b.Start ? a.Start : b.Start);
        var shorter = Math.Min((a.End - a.Start).Ticks, (b.End - b.Start).Ticks);
        return overlap > TimeSpan.Zero && overlap.Ticks * 2 >= shorter;
    }

    /// <summary>The sessions of one run on one clock, with the standings at each finish line crossing.</summary>
    public static SessionTimeline Build(IReadOnlyList<TimelineSession> sessions, TimelineOptions? options = null)
    {
        options ??= new TimelineOptions();
        var withLaps = sessions.Where(s => s.Span != null).ToList();

        // The reference: the session that overlaps the most others (the clocks of most devices are right)
        var offsets = sessions.ToDictionary(s => s.Key, s => s.Offset, StringComparer.Ordinal);
        if (options.AlignClocks && withLaps.Count > 1)
        {
            var reference = withLaps
                .OrderByDescending(s => withLaps.Count(o => o != s && Overlaps(s.Span!.Value, o.Span!.Value)))
                .ThenByDescending(s => s.Laps.Count())
                .First();
            foreach (var session in withLaps)
            {
                if (session == reference)
                    continue;
                if (DetectOffset(session.Span!.Value, reference.Span!.Value) is { } shift)
                    offsets[session.Key] = session.Offset + shift;
            }
        }

        var timing = new PassingTimingSession(new TimingLineLayout(), options.Rules);
        var passings = new List<(TimingPassing Passing, int Lap, TimeSpan? Time)>();
        foreach (var session in sessions)
        {
            timing.SetCompetitor(new TimingCompetitor { Key = session.Key, Number = session.Number, Name = session.Name, ClassName = session.ClassName });
            var offset = offsets[session.Key];
            var laps = session.Laps.ToList();
            if (laps.Count == 0)
                continue;

            // The start of the first lap, then the end of each lap
            passings.Add((Passing(session.Key, 0, laps[0].Timestamp - laps[0].Time + offset), 0, null));
            for (var i = 0; i < laps.Count; i++)
                passings.Add((Passing(session.Key, i + 1, laps[i].Timestamp + offset), i + 1, laps[i].Time));
        }

        var crossings = new List<TimelineCrossing>(passings.Count);
        foreach (var (passing, lap, time) in passings.OrderBy(p => p.Passing.Timestamp).ThenBy(p => p.Passing.Key, StringComparer.Ordinal))
        {
            timing.AddPassing(passing);
            var standings = timing.Classify(passing.Timestamp)
                .Select(r => new TimelineStanding(r.Competitor.Key, r.Position, r.Timing.Laps, r.Timing.BestLapTime, r.GapTime, r.GapLaps))
                .ToList();
            crossings.Add(new TimelineCrossing(passing.Timestamp, passing.Key, lap, time, standings));
        }

        return new SessionTimeline(sessions, offsets, timing, crossings);
    }

    private static TimingPassing Passing(string key, int lap, DateTimeOffset at) => new TimingPassing
    {
        Id = key + "-" + lap.ToString(CultureInfo.InvariantCulture),
        Key = key,
        Line = TimingLineLayout.FinishLine,
        Timestamp = at,
    };

    /// <summary>
    /// The timeline as a LapViz race timeline (<c>format: "lapviz-race"</c>, version 2: the race feed of the LapViz 3D
    /// replay): drivers (with the URL of their GPS session when <paramref name="traces"/> gives one, and its clock
    /// correction), and the finish line crossings in seconds from <see cref="Start"/> with their lap time and position.
    /// </summary>
    /// <param name="title">Title of the run (event).</param>
    /// <param name="group">Class or group.</param>
    /// <param name="run">Name of the run.</param>
    /// <param name="track">Name of the circuit.</param>
    /// <param name="circuitCode">LapViz circuit code.</param>
    /// <param name="traces">The URL of each competitor's GPS session, by key (optional).</param>
    public string ToRaceTimelineJson(string title, string group, string run, string track, string? circuitCode,
        IReadOnlyDictionary<string, string>? traces = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("format", "lapviz-race");
            w.WriteNumber("version", 2);
            w.WriteString("title", title);
            w.WriteString("group", group);
            w.WriteString("run", run);
            w.WriteString("track", track);
            if (!string.IsNullOrEmpty(circuitCode))
                w.WriteString("circuit", circuitCode);
            w.WriteString("type", Timing.Rules.RunType == RaceRunType.Race ? "R" : Timing.Rules.RunType == RaceRunType.Qualifying ? "Q" : "P");
            w.WriteString("start", Start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            w.WriteNumber("duration", Seconds(End - Start));
            w.WriteNumber("sectors", 1);

            var final = Crossings.Count > 0 ? Crossings[Crossings.Count - 1].Standings : Array.Empty<TimelineStanding>();
            w.WriteStartArray("drivers");
            foreach (var session in Sessions.OrderBy(s => final.FirstOrDefault(f => f.Key == s.Key)?.Position ?? int.MaxValue))
            {
                w.WriteStartObject();
                w.WriteString("n", string.IsNullOrWhiteSpace(session.Number) ? session.Key : session.Number);
                w.WriteString("name", session.Name);
                w.WriteString("class", session.ClassName);
                w.WriteNumber("pos", final.FirstOrDefault(f => f.Key == session.Key)?.Position ?? 0);
                if (traces != null && traces.TryGetValue(session.Key, out var trace))
                {
                    w.WriteString("trace", trace);
                    w.WriteNumber("traceOffset", Seconds(Offsets.TryGetValue(session.Key, out var o) ? o : TimeSpan.Zero, allowNegative: true));
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();

            var numbers = Sessions.ToDictionary(s => s.Key, s => string.IsNullOrWhiteSpace(s.Number) ? s.Key : s.Number, StringComparer.Ordinal);
            w.WriteStartArray("events");
            foreach (var crossing in Crossings.Where(c => c.Lap > 0))
            {
                w.WriteStartObject();
                w.WriteNumber("t", Seconds(crossing.Time - Start));
                w.WriteString("n", numbers[crossing.Key]);
                w.WriteString("k", "l");
                w.WriteNumber("lap", crossing.Lap);
                if (crossing.LapTime is { } lap)
                    w.WriteNumber("time", Seconds(lap));
                else
                    w.WriteNull("time");
                w.WriteNumber("pos", crossing.Position);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static double Seconds(TimeSpan value, bool allowNegative = false) =>
        Math.Round(allowNegative ? value.TotalSeconds : Math.Max(0, value.TotalSeconds), 3);
}
