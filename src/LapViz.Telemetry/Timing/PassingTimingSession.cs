using System;
using System.Collections.Generic;
using System.Linq;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.Utils;

namespace LapViz.Telemetry.Timing;

/// <summary>
/// The timing of a session from passings: transponders seen by a decoder, LapViz devices, line crossings of
/// the GPS lap timer. Passings and operator commands form a journal; the laps, sectors, classification and race
/// state are a deterministic projection of it: the same journal gives the same result whatever the order of
/// arrival. A passing or a command that arrives in time order is applied at once (<see cref="TimingUpdate.Events"/>);
/// an older one, an invalidation or a rule change recomputes the session (<see cref="TimingUpdate.Rebuilt"/>).
/// The session never reads the clock: the host passes the current time where it matters.
/// Thread-safe.
/// </summary>
/// <remarks>
/// Rules (see RACE-MANAGER.md §4 of LapViz.OpenLiveTiming):
/// a passing on the same line within <see cref="TimingRules.MinPassingInterval"/> is a duplicate;
/// a finish line passing within <see cref="TimingRules.MinLapTime"/> of the previous one is rejected;
/// a sector has a time only when the previous passing is the previous line of the same lap (a missed loop gives
/// no sector time, never a wrong one); an intermediate line seen twice without a finish line passing in between
/// counts a lap without time; once the checkered flag is out, the next finish line passing of each competitor
/// finishes it and its later passings are ignored.
/// A red flag suspends the session until the next flag (the restart): the clock stops (<see cref="TimingRules.RedFlagStopsClock"/>)
/// and a lap or a sector that overlaps the suspension is counted without time; a lap longer than
/// <see cref="TimingRules.MaxLapTime"/> too; a pit stop shorter than <see cref="TimingRules.MinPitStopTime"/> gets the
/// missing time as a penalty.
/// </remarks>
public sealed class PassingTimingSession
{
    private readonly object _sync = new object();
    private readonly Dictionary<string, TimingPassing> _passingsById = new Dictionary<string, TimingPassing>(StringComparer.Ordinal);
    private readonly List<TimingPassing> _passings = new List<TimingPassing>();
    private readonly HashSet<string> _invalidated = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<TimingCommand> _commands = new List<TimingCommand>();
    private readonly Dictionary<string, TimingCompetitor> _competitors = new Dictionary<string, TimingCompetitor>(StringComparer.Ordinal);
    private readonly Dictionary<string, CompetitorTiming> _timings = new Dictionary<string, CompetitorTiming>(StringComparer.Ordinal);
    private readonly Dictionary<string, PassingRejection> _rejections = new Dictionary<string, PassingRejection>(StringComparer.Ordinal);
    private readonly List<KeyValuePair<DateTimeOffset, RaceFlag>> _flags = new List<KeyValuePair<DateTimeOffset, RaceFlag>>();

    private long _commandSequence;

    // Derived state, recomputed by Rebuild
    private DateTimeOffset? _lastProcessed;
    private DateTimeOffset? _startCommandAt;
    private DateTimeOffset? _firstStartCrossing;
    private DateTimeOffset? _checkeredAt;
    private string? _leaderKey;
    private int _leaderLaps;

    /// <summary>Creates a session on <paramref name="layout"/> with <paramref name="rules"/>.</summary>
    public PassingTimingSession(TimingLineLayout layout, TimingRules rules)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    private enum TimingCommandKind
    {
        Start,
        Flag,
        Checkered
    }

    /// <summary>The timing lines.</summary>
    public TimingLineLayout Layout { get; }

    /// <summary>The rules. After changing them, call <see cref="Recompute"/>.</summary>
    public TimingRules Rules { get; }

    /// <summary>
    /// When the session started: the start command, or the first finish line passing without one (and always
    /// the first finish line passing of a race started on the first crossing, the rolling start). Null before.
    /// </summary>
    public DateTimeOffset? StartedAt
    {
        get { lock (_sync) return SessionStart; }
    }

    /// <summary>When the checkered flag went out, null before.</summary>
    public DateTimeOffset? CheckeredAt
    {
        get { lock (_sync) return _checkeredAt; }
    }

    /// <summary>The flag changes so far, in time order.</summary>
    public IReadOnlyList<KeyValuePair<DateTimeOffset, RaceFlag>> FlagChanges
    {
        get { lock (_sync) return _flags.ToList(); }
    }

    /// <summary>All passings received (invalidated ones included), in time order.</summary>
    public IReadOnlyList<TimingPassing> Passings
    {
        get { lock (_sync) return _passings.ToList(); }
    }

    private DateTimeOffset? SessionStart =>
        Rules.Classification == TimingClassificationMode.Race && Rules.StartMode == TimingStartMode.FirstCrossing
            ? _firstStartCrossing
            : _startCommandAt ?? _firstStartCrossing;

    // ── Competitors ───────────────────────────────────────────

    /// <summary>
    /// Registers a competitor or changes its identity (number, name, class, grid). A key seen before its
    /// registration was an unknown competitor: it keeps its laps under its new identity.
    /// </summary>
    public void SetCompetitor(TimingCompetitor competitor)
    {
        if (competitor == null) throw new ArgumentNullException(nameof(competitor));
        if (string.IsNullOrWhiteSpace(competitor.Key)) throw new ArgumentException("The key is required.", nameof(competitor));

        lock (_sync)
        {
            competitor.IsUnknown = false;
            _competitors[competitor.Key] = competitor;
            if (!_timings.TryGetValue(competitor.Key, out var timing))
            {
                // Classified from now on (grid order before its first passing)
                TimingOf(competitor.Key);
            }
            else
            {
                timing.Competitor = competitor;
                timing.Session.RaceNumber = competitor.DisplayNumber;
                timing.Session.DriverDisplayName = competitor.Name;
                timing.Session.Driver.Number = competitor.DisplayNumber;
                if (!string.IsNullOrWhiteSpace(competitor.Name))
                    timing.Session.Driver.Name = competitor.Name;
            }
        }
    }

    /// <summary>The timing of the competitor with <paramref name="key"/>, null when unknown.</summary>
    public CompetitorTiming? GetTiming(string key)
    {
        lock (_sync)
            return _timings.TryGetValue(key, out var timing) ? timing : null;
    }

    // ── Passings ──────────────────────────────────────────────

    /// <summary>Adds a passing. A passing with an id already received is ignored.</summary>
    public TimingUpdate AddPassing(TimingPassing passing)
    {
        if (passing == null) throw new ArgumentNullException(nameof(passing));
        if (string.IsNullOrWhiteSpace(passing.Id)) throw new ArgumentException("The passing id is required.", nameof(passing));
        if (string.IsNullOrWhiteSpace(passing.Key)) throw new ArgumentException("The passing key is required.", nameof(passing));

        lock (_sync)
        {
            if (_passingsById.ContainsKey(passing.Id))
                return TimingUpdate.Of(null, PassingRejection.AlreadyReceived, false);

            _passingsById[passing.Id] = passing;
            InsertSorted(_passings, passing, ComparePassings);

            var flags = _flags.Count;
            if (_lastProcessed.HasValue && passing.Timestamp < _lastProcessed.Value)
            {
                Rebuild();
                return TimingUpdate.OfRebuild(RejectionOf(passing.Id), _flags.Count != flags);
            }

            var events = new List<SessionDataEvent>();
            Process(passing, events);
            _lastProcessed = passing.Timestamp;
            return TimingUpdate.Of(events, RejectionOf(passing.Id), _flags.Count != flags);
        }
    }

    /// <summary>Invalidates a passing (operator correction): it no longer counts. False when unknown or already invalid.</summary>
    public bool InvalidatePassing(string passingId)
    {
        lock (_sync)
        {
            if (!_passingsById.ContainsKey(passingId) || !_invalidated.Add(passingId))
                return false;
            Rebuild();
            return true;
        }
    }

    /// <summary>Counts an invalidated passing again. False when unknown or not invalidated.</summary>
    public bool RestorePassing(string passingId)
    {
        lock (_sync)
        {
            if (!_invalidated.Remove(passingId))
                return false;
            Rebuild();
            return true;
        }
    }

    // ── Commands ──────────────────────────────────────────────

    /// <summary>
    /// Starts the session at <paramref name="at"/> (green flag): with <see cref="TimingStartMode.Signal"/> the
    /// first finish line passing completes lap 1 from this time; passings before it never count. A new start
    /// replaces the previous one.
    /// </summary>
    public TimingUpdate Start(DateTimeOffset at)
    {
        lock (_sync)
        {
            var replaced = _commands.RemoveAll(c => c.Kind == TimingCommandKind.Start) > 0;
            return AddCommand(new TimingCommand(TimingCommandKind.Start, at, RaceFlag.Green, ++_commandSequence), replaced);
        }
    }

    /// <summary>Changes the track flag at <paramref name="at"/>. <see cref="RaceFlag.Checkered"/> is <see cref="Checkered"/>.</summary>
    public TimingUpdate SetFlag(RaceFlag flag, DateTimeOffset at)
    {
        if (flag == RaceFlag.Checkered)
            return Checkered(at);

        lock (_sync)
            return AddCommand(new TimingCommand(TimingCommandKind.Flag, at, flag, ++_commandSequence), false);
    }

    /// <summary>
    /// Shows the checkered flag at <paramref name="at"/> (manual end of the session, also before the automatic
    /// one): the next finish line passing of each competitor finishes it.
    /// </summary>
    public TimingUpdate Checkered(DateTimeOffset at)
    {
        lock (_sync)
        {
            var replaced = _commands.RemoveAll(c => c.Kind == TimingCommandKind.Checkered) > 0;
            return AddCommand(new TimingCommand(TimingCommandKind.Checkered, at, RaceFlag.Checkered, ++_commandSequence), replaced);
        }
    }

    /// <summary>Sets the penalties of a competitor (replacing the previous ones): time added to its race time, laps removed.</summary>
    public void SetPenalty(string key, TimeSpan time, int laps = 0)
    {
        lock (_sync)
        {
            var timing = TimingOf(key);
            timing.PenaltyTime = time < TimeSpan.Zero ? TimeSpan.Zero : time;
            timing.PenaltyLaps = Math.Max(0, laps);
        }
    }

    /// <summary>Sets the status of a competitor (DNS, DNF, DSQ), null to clear it.</summary>
    public void SetStatus(string key, RaceEntryStatus? status)
    {
        lock (_sync)
            TimingOf(key).StatusOverride = status;
    }

    /// <summary>Recomputes the whole session, after a change of <see cref="Rules"/>.</summary>
    public void Recompute()
    {
        lock (_sync)
            Rebuild();
    }

    // ── Classification ────────────────────────────────────────

    /// <summary>
    /// The classification at <paramref name="now"/> (decoder time): by best lap or as a race (<see cref="TimingRules.Classification"/>).
    /// Registered competitors appear before their first passing; unknown keys once they passed.
    /// </summary>
    public IReadOnlyList<TimingClassificationRow> Classify(DateTimeOffset now)
    {
        lock (_sync)
        {
            ApplyDurationExpiry(now);

            var timings = _timings.Values
                .Where(t => !t.Competitor.IsUnknown || t.LastPassingAt.HasValue)
                .ToList();

            timings.Sort(Rules.Classification == TimingClassificationMode.Race ? CompareRace : CompareBestLap);

            var rows = new List<TimingClassificationRow>(timings.Count);
            for (var i = 0; i < timings.Count; i++)
            {
                var row = new TimingClassificationRow(i + 1, timings[i]);
                if (i > 0)
                {
                    if (Rules.Classification == TimingClassificationMode.Race)
                    {
                        RaceGap(timings[i], timings[0], out var gapTime, out var gapLaps);
                        RaceGap(timings[i], timings[i - 1], out var intervalTime, out var intervalLaps);
                        row.GapTime = gapTime;
                        row.GapLaps = gapLaps;
                        row.IntervalTime = intervalTime;
                        row.IntervalLaps = intervalLaps;
                    }
                    else
                    {
                        row.GapTime = timings[i].BestLapTime - timings[0].BestLapTime;
                        row.IntervalTime = timings[i].BestLapTime - timings[i - 1].BestLapTime;
                    }
                }

                rows.Add(row);
            }

            return rows;
        }
    }

    /// <summary>
    /// Fills <paramref name="state"/> (the race model of OpenLiveTiming) with the session at <paramref name="now"/>:
    /// run type, flag, clock, bests and one entry per classified competitor, keyed by racing number.
    /// The identity of the session (event, group, run, track) is the caller's.
    /// </summary>
    public void ToRaceState(RaceSessionState state, DateTimeOffset now)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        lock (_sync)
        {
            // Same lock as the classification: no passing comes in between
            var rows = Classify(now);
            var sectorCount = Layout.SectorCount;
            var start = SessionStart;

            state.RunType = Rules.RunType;
            state.SectorCount = sectorCount;
            state.TotalLaps = Rules.Classification == TimingClassificationMode.Race ? Rules.Laps : 0;
            state.Flag = CurrentFlag;

            var clockEnd = _checkeredAt.HasValue && _checkeredAt.Value < now ? _checkeredAt.Value : now;
            state.RaceTime = start.HasValue ? Max(TimeSpan.Zero, Elapsed(start.Value, clockEnd)) : null;
            state.TimeToGo = Rules.Duration.HasValue
                ? Max(TimeSpan.Zero, Rules.Duration.Value - (state.RaceTime ?? TimeSpan.Zero))
                : null;
            state.LeaderLap = rows.Count == 0
                ? 0
                : Rules.Classification == TimingClassificationMode.Race ? rows[0].Timing.ClassifiedLaps : rows.Max(r => r.Timing.Laps);
            state.LastUpdated = now.UtcDateTime;

            // Session bests
            var best = rows
                .Where(r => r.Timing.BestLapTime.HasValue)
                .OrderBy(r => r.Timing.BestLapTime)
                .ThenBy(r => r.Timing.BestLapAt)
                .FirstOrDefault();
            state.BestLapTime = best?.Timing.BestLapTime;
            state.BestLapByNumber = best?.Competitor.DisplayNumber ?? "";
            state.BestLapBy = best == null ? "" : NameOf(best.Competitor);

            var bestSectors = new TimeSpan?[sectorCount];
            for (var s = 0; s < sectorCount; s++)
                bestSectors[s] = rows.Select(r => r.Timing.BestSectorTimes[s]).Where(x => x.HasValue).Min();
            state.BestSectorTimes = bestSectors;

            state.Entries.Clear();
            foreach (var row in rows)
            {
                var timing = row.Timing;
                var number = row.Competitor.DisplayNumber;
                var key = state.Entries.ContainsKey(number) ? $"{number} ({timing.Key})" : number;

                var current = timing.LastCompletedSector >= 0 ? timing.SectorTimes : timing.PreviousLapSectorTimes;
                var entry = new RaceEntry
                {
                    Number = number,
                    FullName = NameOf(row.Competitor),
                    ClassName = row.Competitor.ClassName ?? "",
                    Position = row.Position,
                    StartPosition = row.Competitor.StartPosition,
                    Laps = Rules.Classification == TimingClassificationMode.Race ? timing.ClassifiedLaps : timing.Laps,
                    TotalTime = Rules.Classification == TimingClassificationMode.Race && start.HasValue && timing.ClassifiedLapAt.HasValue
                        ? timing.ClassifiedLapAt.Value - start.Value
                        : null,
                    LastTime = timing.LastLapTime,
                    BestTime = timing.BestLapTime,
                    Status = timing.Status,
                    HasFinished = timing.Finished,
                    PenaltyTime = (int)Math.Round(timing.TotalPenaltyTime.TotalSeconds),
                    SectionTimes = (TimeSpan?[])current.Clone(),
                    BestSectionTimes = (TimeSpan?[])timing.BestSectorTimes.Clone(),
                    IsSectorPersonalBest = new bool[sectorCount],
                    IsSectorOverallBest = new bool[sectorCount],
                    LastCompletedSector = timing.LastCompletedSector >= 0 ? timing.LastCompletedSector : (current.Any(x => x.HasValue) ? sectorCount - 1 : -1),
                    Gap = row.Position == 1 ? "" : FormatGap(row.GapTime, row.GapLaps),
                    Interval = row.Position == 1 ? "" : FormatGap(row.IntervalTime, row.IntervalLaps),
                    Timing = timing.Session,
                };

                for (var s = 0; s < sectorCount; s++)
                {
                    if (current[s] is { } sector)
                    {
                        entry.IsSectorPersonalBest[s] = timing.BestSectorTimes[s] is { } own && sector <= own;
                        entry.IsSectorOverallBest[s] = bestSectors[s] is { } overall && sector <= overall;
                    }
                }

                entry.IsLastLapPersonalBest = timing.LastLapTime.HasValue && timing.LastLapTime == timing.BestLapTime;
                entry.IsLastLapOverallBest = timing.LastLapTime.HasValue && timing.LastLapTime <= state.BestLapTime;
                entry.Driver.Id = timing.Key;

                state.Entries[key] = entry;
            }
        }
    }

    /// <summary>The track flag now: checkered once out, else the last flag, green once started, none before.</summary>
    public RaceFlag Flag
    {
        get { lock (_sync) return CurrentFlag; }
    }

    private RaceFlag CurrentFlag =>
        _checkeredAt.HasValue ? RaceFlag.Checkered
        : _flags.Count > 0 ? _flags[_flags.Count - 1].Value
        : SessionStart.HasValue ? RaceFlag.Green
        : RaceFlag.None;

    // ── Derivation ────────────────────────────────────────────

    // A command older than the last item processed, or one that replaces a previous start or checkered flag,
    // changes how the passings count: the session is recomputed
    private TimingUpdate AddCommand(TimingCommand command, bool replaced)
    {
        InsertSorted(_commands, command, CompareCommands);
        var flags = _flags.Count;

        if (replaced || (_lastProcessed.HasValue && command.At < _lastProcessed.Value))
        {
            Rebuild();
            return TimingUpdate.OfRebuild(null, _flags.Count != flags);
        }

        Apply(command);
        _lastProcessed = command.At;
        return TimingUpdate.Of(null, null, _flags.Count != flags);
    }

    private void Rebuild()
    {
        _lastProcessed = null;
        _startCommandAt = null;
        _firstStartCrossing = null;
        _checkeredAt = null;
        _leaderKey = null;
        _leaderLaps = 0;
        _flags.Clear();
        _rejections.Clear();
        foreach (var timing in _timings.Values)
            timing.Reset();

        // Commands before passings of the same instant: a start or a checkered flag applies to them
        var c = 0;
        foreach (var passing in _passings)
        {
            while (c < _commands.Count && _commands[c].At <= passing.Timestamp)
                Apply(_commands[c++]);

            if (_invalidated.Contains(passing.Id))
            {
                _rejections[passing.Id] = PassingRejection.Invalidated;
                TimingOf(passing.Key).Reject(passing, PassingRejection.Invalidated);
            }
            else
            {
                Process(passing, null);
            }

            _lastProcessed = passing.Timestamp;
        }

        while (c < _commands.Count)
        {
            Apply(_commands[c]);
            _lastProcessed = _commands[c++].At;
        }
    }

    private void Apply(TimingCommand command)
    {
        ApplyDurationExpiry(command.At);
        switch (command.Kind)
        {
            case TimingCommandKind.Start:
                if (!_startCommandAt.HasValue)
                {
                    _startCommandAt = command.At;
                    _flags.Add(new KeyValuePair<DateTimeOffset, RaceFlag>(command.At, RaceFlag.Green));
                }

                break;

            case TimingCommandKind.Flag:
                if (!_checkeredAt.HasValue)
                    _flags.Add(new KeyValuePair<DateTimeOffset, RaceFlag>(command.At, command.Flag));
                break;

            case TimingCommandKind.Checkered:
                if (!_checkeredAt.HasValue)
                    SetCheckered(command.At);
                break;
        }
    }

    // By best lap, a timed session ends at once when its time is up
    private void ApplyDurationExpiry(DateTimeOffset at)
    {
        if (_checkeredAt.HasValue || Rules.Classification != TimingClassificationMode.BestLap || !Rules.Duration.HasValue)
            return;

        if (EndOfDuration() is { } end && at >= end)
            SetCheckered(end);
    }

    // ── Suspensions (red flag) ────────────────────────────────

    /// <summary>The red flag periods so far: from a red flag to the next other flag, open while it is still out.</summary>
    private IEnumerable<KeyValuePair<DateTimeOffset, DateTimeOffset?>> Suspensions()
    {
        DateTimeOffset? from = null;
        foreach (var change in _flags)
        {
            if (change.Value == RaceFlag.Red)
                from ??= change.Key;
            else if (from.HasValue)
            {
                yield return new KeyValuePair<DateTimeOffset, DateTimeOffset?>(from.Value, change.Key);
                from = null;
            }
        }

        if (from.HasValue)
            yield return new KeyValuePair<DateTimeOffset, DateTimeOffset?>(from.Value, null);
    }

    /// <summary>True when a suspension overlaps the interval from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private bool OverlapsSuspension(DateTimeOffset from, DateTimeOffset to) =>
        Suspensions().Any(s => s.Key < to && (s.Value ?? DateTimeOffset.MaxValue) > from);

    /// <summary>Session time from <paramref name="from"/> to <paramref name="to"/>: without the suspensions when they stop the clock.</summary>
    private TimeSpan Elapsed(DateTimeOffset from, DateTimeOffset to)
    {
        var elapsed = to - from;
        if (!Rules.RedFlagStopsClock)
            return elapsed;
        foreach (var s in Suspensions())
        {
            var a = s.Key > from ? s.Key : from;
            var b = s.Value.HasValue && s.Value.Value < to ? s.Value.Value : to;
            if (b > a)
                elapsed -= b - a;
        }

        return elapsed;
    }

    /// <summary>When the duration of the session is reached (suspensions added); null when not timed, not started or suspended at that point.</summary>
    private DateTimeOffset? EndOfDuration()
    {
        var start = SessionStart;
        if (!Rules.Duration.HasValue || !start.HasValue)
            return null;
        if (!Rules.RedFlagStopsClock)
            return start.Value + Rules.Duration.Value;

        var t = start.Value;
        var remaining = Rules.Duration.Value;
        foreach (var s in Suspensions())
        {
            if (s.Key <= t)
            {
                if (!s.Value.HasValue)
                    return null;
                if (s.Value.Value > t)
                    t = s.Value.Value;
                continue;
            }

            if (s.Key - t >= remaining)
                break;
            remaining -= s.Key - t;
            if (!s.Value.HasValue)
                return null;
            t = s.Value.Value;
        }

        return t + remaining;
    }

    private void SetCheckered(DateTimeOffset at)
    {
        _checkeredAt = at;
        _flags.Add(new KeyValuePair<DateTimeOffset, RaceFlag>(at, RaceFlag.Checkered));
    }

    private void Process(TimingPassing passing, List<SessionDataEvent>? sink)
    {
        var at = passing.Timestamp;
        ApplyDurationExpiry(at);

        var timing = TimingOf(passing.Key);
        var rejection = Check(timing, passing);
        if (rejection.HasValue)
        {
            _rejections[passing.Id] = rejection.Value;
            timing.Reject(passing, rejection.Value);
            return;
        }

        var previousPassingAt = timing.LastPassingAt;
        timing.SetLastOnLine(passing.Line, at);
        timing.LastPassingAt = at;

        if (passing.Line == Layout.PitInLine)
        {
            timing.InPit = true;
            timing.OnOutLap = false;
            timing.ChainLine = null;
            timing.PitInAt = at;
        }
        else if (passing.Line == Layout.PitOutLine)
        {
            if (timing.PitInAt.HasValue)
            {
                var duration = at - timing.PitInAt.Value;
                var shortfall = Rules.MinPitStopTime > TimeSpan.Zero && duration < Rules.MinPitStopTime
                    ? Rules.MinPitStopTime - duration
                    : TimeSpan.Zero;
                timing.AddPitStop(new TimingPitStop(timing.PitInAt.Value, at, shortfall));
                timing.PitInAt = null;
            }

            timing.InPit = false;
            timing.OnOutLap = true;
            timing.ChainLine = null;
        }
        else if (passing.Line == TimingLineLayout.FinishLine)
        {
            FinishLinePassing(timing, at, sink);
        }
        else
        {
            IntermediatePassing(timing, passing.Line, at, previousPassingAt, sink);
        }
    }

    private PassingRejection? Check(CompetitorTiming timing, TimingPassing passing)
    {
        var at = passing.Timestamp;
        if (!Layout.IsKnownLine(passing.Line))
            return PassingRejection.UnknownLine;

        // Before the start: never with a start signal, else only when a start was given
        if (_startCommandAt.HasValue ? at < _startCommandAt.Value : Rules.StartMode == TimingStartMode.Signal)
            return PassingRejection.BeforeStart;

        if (timing.Finished)
            return PassingRejection.AfterFinish;

        if (timing.TryGetLastOnLine(passing.Line, out var last) && at - last < Rules.MinPassingInterval)
            return PassingRejection.Duplicate;

        if (passing.Line == TimingLineLayout.FinishLine && Rules.MinLapTime > TimeSpan.Zero
            && timing.LastFinishLineAt.HasValue && at - timing.LastFinishLineAt.Value < Rules.MinLapTime)
            return PassingRejection.MinLapTime;

        return null;
    }

    private void FinishLinePassing(CompetitorTiming timing, DateTimeOffset at, List<SessionDataEvent>? sink)
    {
        if (!timing.StartedAt.HasValue)
        {
            if (Rules.StartMode == TimingStartMode.Signal)
            {
                // Started by the signal: the first passing completes lap 1, unless it comes sooner than a lap
                // (grid behind the line): then it only starts the lap
                var signal = _startCommandAt!.Value;
                timing.StartedAt = signal;
                AddLapEvent(timing, 0, TimeSpan.Zero, signal, sink);
                timing.LapStart = signal;
                timing.LapStartKnown = true;
                timing.ChainLine = TimingLineLayout.FinishLine;
                timing.ChainAt = signal;
                timing.MaxLineThisLap = TimingLineLayout.FinishLine;

                if (Rules.MinLapTime > TimeSpan.Zero && at - signal < Rules.MinLapTime)
                {
                    StartLapAt(timing, at);
                    return;
                }
            }
            else
            {
                timing.StartedAt = at;
                if (!_firstStartCrossing.HasValue)
                    _firstStartCrossing = at;
                AddLapEvent(timing, 0, TimeSpan.Zero, at, sink);
                StartLapAt(timing, at);
                return;
            }
        }

        // Last sector: only when the previous passing is the last intermediate line of this lap
        if (Layout.SectorCount > 0 && timing.LapStartKnown && timing.ChainLine == Layout.SectorLines && !OverlapsSuspension(timing.ChainAt, at))
            AddSectorEvent(timing, Layout.SectorLines, at - timing.ChainAt, at, sink);

        var lapNumber = timing.Laps + 1;
        TimeSpan? lapTime = timing.LapStartKnown && timing.LapStart.HasValue ? at - timing.LapStart.Value : null;
        if (lapTime.HasValue && (OverlapsSuspension(timing.LapStart!.Value, at) || (Rules.MaxLapTime > TimeSpan.Zero && lapTime.Value > Rules.MaxLapTime)))
        {
            // Not representative: counted, never a best lap (no lap event, as a lap whose time is unknown)
            lapTime = null;
            timing.ExcludedLaps++;
        }
        timing.AddLapCompletion(at);
        timing.LastLapTime = lapTime;
        if (lapTime.HasValue)
        {
            if (!timing.BestLapTime.HasValue || lapTime.Value < timing.BestLapTime.Value)
            {
                timing.BestLapTime = lapTime;
                timing.BestLapAt = at;
            }

            AddLapEvent(timing, lapNumber, lapTime.Value, at, sink);
        }

        timing.NewLapSectors();
        StartLapAt(timing, at);

        // The out lap ends on the line; without a pit out line, the pit lane ends there too
        if (timing.OnOutLap)
            timing.OnOutLap = false;
        if (timing.InPit && !Layout.PitOutLine.HasValue)
        {
            timing.InPit = false;
            timing.OnOutLap = true;
        }

        UpdateLeader(timing);

        if (_checkeredAt.HasValue && at >= _checkeredAt.Value)
        {
            Finish(timing, at);
            return;
        }

        if (Rules.Classification != TimingClassificationMode.Race || timing.Key != _leaderKey)
            return;

        var lapsDone = Rules.Laps > 0 && timing.Laps >= Rules.Laps;
        var timeDone = EndOfDuration() is { } end && at >= end;
        if (lapsDone || timeDone)
        {
            SetCheckered(at);
            Finish(timing, at);
        }
    }

    private void IntermediatePassing(CompetitorTiming timing, int line, DateTimeOffset at, DateTimeOffset? previousPassingAt, List<SessionDataEvent>? sink)
    {
        if (!timing.StartedAt.HasValue)
        {
            // Out lap before the first finish line passing: no lap to place it in
            timing.ChainLine = line;
            timing.ChainAt = at;
            return;
        }

        // The same line again (or an earlier one) without the finish line in between: the finish line was missed
        if (timing.MaxLineThisLap >= line)
            CountMissedLap(timing, at, previousPassingAt ?? at);

        if (timing.ChainLine == line - 1 && !OverlapsSuspension(timing.ChainAt, at))
            AddSectorEvent(timing, Layout.SectorEndedBy(line), at - timing.ChainAt, at, sink);

        timing.ChainLine = line;
        timing.ChainAt = at;
        timing.MaxLineThisLap = line;
    }

    // A lap whose finish line passing was missed: counted, without time, completed at an estimated time
    private void CountMissedLap(CompetitorTiming timing, DateTimeOffset at, DateTimeOffset after)
    {
        var estimate = after + TimeSpan.FromTicks((at - after).Ticks / 2);
        var typical = timing.LastLapTime ?? timing.BestLapTime;
        if (timing.LapStartKnown && timing.LapStart.HasValue && typical.HasValue)
        {
            var expected = timing.LapStart.Value + typical.Value;
            if (expected > after && expected < at)
                estimate = expected;
        }

        timing.AddLapCompletion(estimate);
        timing.EstimatedLaps++;
        timing.LastLapTime = null;
        timing.NewLapSectors();
        timing.LapStart = estimate;
        timing.LapStartKnown = false;
        timing.ChainLine = null;
        timing.MaxLineThisLap = 0;
        timing.LastFinishLineAt = estimate;
        UpdateLeader(timing);
    }

    private static void StartLapAt(CompetitorTiming timing, DateTimeOffset at)
    {
        timing.LapStart = at;
        timing.LapStartKnown = true;
        timing.ChainLine = TimingLineLayout.FinishLine;
        timing.ChainAt = at;
        timing.MaxLineThisLap = TimingLineLayout.FinishLine;
        timing.LastFinishLineAt = at;
    }

    private void UpdateLeader(CompetitorTiming timing)
    {
        // The leader is the first to reach the most laps
        if (timing.Laps > _leaderLaps)
        {
            _leaderLaps = timing.Laps;
            _leaderKey = timing.Key;
        }
    }

    private static void Finish(CompetitorTiming timing, DateTimeOffset at)
    {
        timing.Finished = true;
        timing.FinishedAt = at;
        timing.InPit = false;
        timing.OnOutLap = false;
    }

    private static void AddSectorEvent(CompetitorTiming timing, int sector, TimeSpan time, DateTimeOffset at, List<SessionDataEvent>? sink)
    {
        if (sector < 1 || time <= TimeSpan.Zero)
            return;

        var e = NewEvent(timing, SessionEventType.Sector, timing.Laps + 1, sector, time, at);
        e.IsBestOverall = timing.Session.IsBestSector(e);
        timing.Session.AddEvent(e);
        sink?.Add(e);

        var i = sector - 1;
        timing.SectorTimes[i] = time;
        timing.LastCompletedSector = i;
        if (timing.BestSectorTimes[i] is not { } best || time < best)
            timing.BestSectorTimes[i] = time;
    }

    private static void AddLapEvent(CompetitorTiming timing, int lapNumber, TimeSpan time, DateTimeOffset at, List<SessionDataEvent>? sink)
    {
        var e = NewEvent(timing, SessionEventType.Lap, lapNumber, 0, time, at);
        if (time > TimeSpan.Zero)
        {
            var best = timing.Session.BestLap;
            e.IsBestOverall = best == null || best.Time <= TimeSpan.Zero || time <= best.Time;
        }

        timing.Session.AddEvent(e);
        sink?.Add(e);
    }

    private static SessionDataEvent NewEvent(CompetitorTiming timing, SessionEventType type, int lap, int sector, TimeSpan time, DateTimeOffset at) =>
        new SessionDataEvent
        {
            EventId = CompactEventId.FromTimestamp(at, StableHash($"{timing.Key}|{type}|{lap}|{sector}")),
            Timestamp = at,
            Type = type,
            LapNumber = lap,
            Sector = sector,
            Time = time,
            DeviceId = timing.Key,
            DriverRace = timing.Session,
        };

    private CompetitorTiming TimingOf(string key)
    {
        if (!_timings.TryGetValue(key, out var timing))
        {
            if (!_competitors.TryGetValue(key, out var competitor))
                competitor = new TimingCompetitor { Key = key, IsUnknown = true };
            timing = new CompetitorTiming(competitor, Layout.SectorCount);
            _timings[key] = timing;
        }

        return timing;
    }

    private PassingRejection? RejectionOf(string passingId) =>
        _rejections.TryGetValue(passingId, out var reason) ? reason : null;

    // ── Ordering ──────────────────────────────────────────────

    // Disqualified and non-starters at the end, in this order
    private static int StatusGroup(CompetitorTiming t) =>
        t.StatusOverride == RaceEntryStatus.DSQ ? 2 : t.StatusOverride == RaceEntryStatus.DNS ? 1 : 0;

    private static int CompareRace(CompetitorTiming a, CompetitorTiming b)
    {
        var c = StatusGroup(a).CompareTo(StatusGroup(b));
        if (c != 0) return c;

        c = b.ClassifiedLaps.CompareTo(a.ClassifiedLaps);
        if (c != 0) return c;

        if (a.ClassifiedLaps > 0)
        {
            c = a.ClassifiedLapAt!.Value.CompareTo(b.ClassifiedLapAt!.Value);
            if (c != 0) return c;
        }

        return CompareGrid(a, b);
    }

    private static int CompareBestLap(CompetitorTiming a, CompetitorTiming b)
    {
        var c = StatusGroup(a).CompareTo(StatusGroup(b));
        if (c != 0) return c;

        c = (a.BestLapTime ?? TimeSpan.MaxValue).CompareTo(b.BestLapTime ?? TimeSpan.MaxValue);
        if (c != 0) return c;

        if (a.BestLapAt.HasValue && b.BestLapAt.HasValue)
        {
            c = a.BestLapAt.Value.CompareTo(b.BestLapAt.Value);
            if (c != 0) return c;
        }

        c = b.Laps.CompareTo(a.Laps);
        return c != 0 ? c : CompareGrid(a, b);
    }

    // Grid position (unknown last), then racing number, then key: a stable order before the first lap
    private static int CompareGrid(CompetitorTiming a, CompetitorTiming b)
    {
        var ga = a.Competitor.StartPosition > 0 ? a.Competitor.StartPosition : int.MaxValue;
        var gb = b.Competitor.StartPosition > 0 ? b.Competitor.StartPosition : int.MaxValue;
        var c = ga.CompareTo(gb);
        if (c != 0) return c;

        c = CompareNumbers(a.Competitor.DisplayNumber, b.Competitor.DisplayNumber);
        return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
    }

    private static int CompareNumbers(string a, string b)
    {
        var na = int.TryParse(a, out var ia);
        var nb = int.TryParse(b, out var ib);
        if (na && nb) return ia.CompareTo(ib);
        if (na != nb) return na ? -1 : 1;
        return string.CompareOrdinal(a, b);
    }

    /// <summary>
    /// Race gap of <paramref name="t"/> to <paramref name="reference"/> (ranked ahead): at the moment t completed its
    /// last lap, the laps the reference had completed beyond it, else the time between their passings of that lap.
    /// </summary>
    private static void RaceGap(CompetitorTiming t, CompetitorTiming reference, out TimeSpan? time, out int laps)
    {
        time = null;
        laps = 0;

        var tLaps = t.ClassifiedLaps;
        if (tLaps == 0 || reference.ClassifiedLaps == 0 || StatusGroup(t) != 0)
            return;

        var tAt = t.ClassifiedLapAt!.Value;
        var refLapsThen = 0;
        while (refLapsThen < reference.ClassifiedLaps && reference.ClassifiedCompletion(refLapsThen + 1) <= tAt)
            refLapsThen++;

        if (refLapsThen > tLaps)
        {
            laps = refLapsThen - tLaps;
            return;
        }

        if (reference.ClassifiedLaps >= tLaps)
            time = tAt - reference.ClassifiedCompletion(tLaps);
    }

    private static int ComparePassings(TimingPassing a, TimingPassing b)
    {
        var c = a.Timestamp.CompareTo(b.Timestamp);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }

    private static int CompareCommands(TimingCommand a, TimingCommand b)
    {
        var c = a.At.CompareTo(b.At);
        return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
    }

    private static void InsertSorted<T>(List<T> list, T item, Comparison<T> comparison)
    {
        // Mostly appended in order: search from the end
        var i = list.Count;
        while (i > 0 && comparison(list[i - 1], item) > 0)
            i--;
        list.Insert(i, item);
    }

    // ── Formatting ────────────────────────────────────────────

    private static string FormatGap(TimeSpan? time, int laps)
    {
        if (laps > 0)
            return TimingFormatters.FormatLapsGap(laps);
        return time.HasValue ? TimingFormatters.FormatGap(time.Value) : "";
    }

    private static string NameOf(TimingCompetitor competitor) =>
        string.IsNullOrWhiteSpace(competitor.Name) ? competitor.DisplayNumber : competitor.Name.Trim();

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    // FNV-1a: string.GetHashCode differs between processes
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var ch in value)
                hash = (hash ^ ch) * 16777619;
            return hash;
        }
    }

    private sealed class TimingCommand
    {
        public TimingCommand(TimingCommandKind kind, DateTimeOffset at, RaceFlag flag, long sequence)
        {
            Kind = kind;
            At = at;
            Flag = flag;
            Sequence = sequence;
        }

        public TimingCommandKind Kind { get; }

        public DateTimeOffset At { get; }

        public RaceFlag Flag { get; }

        public long Sequence { get; }
    }
}
