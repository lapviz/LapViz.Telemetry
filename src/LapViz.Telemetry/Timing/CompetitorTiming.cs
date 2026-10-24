using System;
using System.Collections.Generic;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Timing;

/// <summary>Why a passing does not count.</summary>
public enum PassingRejection
{
    /// <summary>The line is not a line of the layout.</summary>
    UnknownLine,

    /// <summary>Before the start of the session.</summary>
    BeforeStart,

    /// <summary>The competitor already finished (cool-down lap).</summary>
    AfterFinish,

    /// <summary>Same line again within <see cref="TimingRules.MinPassingInterval"/>.</summary>
    Duplicate,

    /// <summary>A finish line passing within <see cref="TimingRules.MinLapTime"/> of the previous one.</summary>
    MinLapTime,

    /// <summary>Invalidated by the operator.</summary>
    Invalidated,

    /// <summary>A passing with the same id was already received.</summary>
    AlreadyReceived
}

/// <summary>A passing that does not count, and why.</summary>
public sealed class RejectedPassing
{
    internal RejectedPassing(TimingPassing passing, PassingRejection reason)
    {
        Passing = passing;
        Reason = reason;
    }

    /// <summary>The passing.</summary>
    public TimingPassing Passing { get; }

    /// <summary>Why it does not count.</summary>
    public PassingRejection Reason { get; }
}

/// <summary>
/// The timing of one competitor in a <see cref="PassingTimingSession"/>, derived from its passings.
/// Read-only for callers; recomputed by the session.
/// </summary>
public sealed class CompetitorTiming
{
    private readonly List<DateTimeOffset> _lapCompletions = new List<DateTimeOffset>();
    private readonly List<RejectedPassing> _rejected = new List<RejectedPassing>();
    private readonly Dictionary<int, DateTimeOffset> _lastByLine = new Dictionary<int, DateTimeOffset>();

    internal CompetitorTiming(TimingCompetitor competitor, int sectorCount)
    {
        Competitor = competitor;
        Session = NewSession(competitor);
        SectorTimes = new TimeSpan?[sectorCount];
        BestSectorTimes = new TimeSpan?[sectorCount];
        PreviousLapSectorTimes = new TimeSpan?[sectorCount];
    }

    /// <summary>The competitor.</summary>
    public TimingCompetitor Competitor { get; internal set; }

    /// <summary>Key of the competitor (transponder number or device id).</summary>
    public string Key => Competitor.Key;

    /// <summary>
    /// Sector and lap events, as the GPS lap timer records them (lap 0 = the start of the timing). A lap whose
    /// time is unknown (a missed finish line passing) has no lap event: the lap numbers of the events skip it.
    /// </summary>
    public DeviceSessionData Session { get; private set; }

    /// <summary>Laps completed, those without time included.</summary>
    public int Laps => _lapCompletions.Count;

    /// <summary>Laps counted without time: a finish line passing was missed and deduced from the other lines.</summary>
    public int EstimatedLaps { get; internal set; }

    /// <summary>When each lap was completed (index 0 = lap 1). An estimated lap has an estimated time.</summary>
    public IReadOnlyList<DateTimeOffset> LapCompletions => _lapCompletions;

    /// <summary>Time of the last completed lap, null when unknown.</summary>
    public TimeSpan? LastLapTime { get; internal set; }

    /// <summary>Best lap time, null without timed lap.</summary>
    public TimeSpan? BestLapTime { get; internal set; }

    /// <summary>When the best lap was completed (the earlier one ranks first on a tie).</summary>
    public DateTimeOffset? BestLapAt { get; internal set; }

    /// <summary>When the timing of the competitor started (first finish line passing, or the start signal).</summary>
    public DateTimeOffset? StartedAt { get; internal set; }

    /// <summary>Last passing that counted.</summary>
    public DateTimeOffset? LastPassingAt { get; internal set; }

    /// <summary>Sector times of the lap in progress (index 0 = sector 1); the previous lap's until a sector is completed.</summary>
    public TimeSpan?[] SectorTimes { get; private set; }

    /// <summary>Sector times of the last completed lap.</summary>
    public TimeSpan?[] PreviousLapSectorTimes { get; private set; }

    /// <summary>Best time of each sector.</summary>
    public TimeSpan?[] BestSectorTimes { get; private set; }

    /// <summary>Index (0-based) of the last sector completed in the lap in progress, -1 when none.</summary>
    public int LastCompletedSector { get; internal set; } = -1;

    /// <summary>True once the competitor took the checkered flag.</summary>
    public bool Finished { get; internal set; }

    /// <summary>When the competitor took the checkered flag.</summary>
    public DateTimeOffset? FinishedAt { get; internal set; }

    /// <summary>In the pit lane (pit in line passed, not out yet).</summary>
    public bool InPit { get; internal set; }

    /// <summary>On the lap after the pit lane exit.</summary>
    public bool OnOutLap { get; internal set; }

    /// <summary>Time penalty, added to the race time of the competitor.</summary>
    public TimeSpan PenaltyTime { get; internal set; }

    /// <summary>Laps removed from the laps completed.</summary>
    public int PenaltyLaps { get; internal set; }

    /// <summary>Status set by the operator (DNS, DNF, DSQ), null when none.</summary>
    public RaceEntryStatus? StatusOverride { get; internal set; }

    /// <summary>Laps that count in a race classification: laps completed minus penalty laps.</summary>
    public int ClassifiedLaps => Math.Max(0, Laps - PenaltyLaps);

    /// <summary>
    /// When the competitor completed its last lap, the time penalty added; null without counted lap. With penalty
    /// laps the competitor is classified on fewer laps but at the time of its last passing.
    /// </summary>
    public DateTimeOffset? ClassifiedLapAt =>
        ClassifiedLaps > 0 ? _lapCompletions[_lapCompletions.Count - 1] + PenaltyTime : null;

    /// <summary>Passings that did not count.</summary>
    public IReadOnlyList<RejectedPassing> Rejected => _rejected;

    /// <summary>Status of the competitor in the race model.</summary>
    public RaceEntryStatus Status
    {
        get
        {
            if (StatusOverride.HasValue) return StatusOverride.Value;
            if (Finished) return RaceEntryStatus.Finished;
            if (InPit) return RaceEntryStatus.InPit;
            if (OnOutLap) return RaceEntryStatus.OutLap;
            return LastPassingAt.HasValue ? RaceEntryStatus.Running : RaceEntryStatus.Unknown;
        }
    }

    // ── Working state of the derivation ──────────────────────

    internal DateTimeOffset? LapStart { get; set; }

    internal bool LapStartKnown { get; set; }

    internal int? ChainLine { get; set; }

    internal DateTimeOffset ChainAt { get; set; }

    internal int MaxLineThisLap { get; set; }

    internal DateTimeOffset? LastFinishLineAt { get; set; }

    /// <summary>When the competitor had <paramref name="laps"/> counted laps (1-based), the time penalty added.</summary>
    internal DateTimeOffset ClassifiedCompletion(int laps) => _lapCompletions[laps - 1 + PenaltyLaps] + PenaltyTime;

    internal void AddLapCompletion(DateTimeOffset at) => _lapCompletions.Add(at);

    internal void Reject(TimingPassing passing, PassingRejection reason) =>
        _rejected.Add(new RejectedPassing(passing, reason));

    internal bool TryGetLastOnLine(int line, out DateTimeOffset at) => _lastByLine.TryGetValue(line, out at);

    internal void SetLastOnLine(int line, DateTimeOffset at) => _lastByLine[line] = at;

    /// <summary>Starts a new lap: the sectors of the lap just completed become the previous lap's.</summary>
    internal void NewLapSectors()
    {
        PreviousLapSectorTimes = (TimeSpan?[])SectorTimes.Clone();
        Array.Clear(SectorTimes, 0, SectorTimes.Length);
        LastCompletedSector = -1;
    }

    /// <summary>Back to the state before the first passing, keeping the competitor and the operator's decisions.</summary>
    internal void Reset()
    {
        _lapCompletions.Clear();
        _rejected.Clear();
        _lastByLine.Clear();
        Session = NewSession(Competitor);
        EstimatedLaps = 0;
        LastLapTime = null;
        BestLapTime = null;
        BestLapAt = null;
        StartedAt = null;
        LastPassingAt = null;
        Array.Clear(SectorTimes, 0, SectorTimes.Length);
        Array.Clear(PreviousLapSectorTimes, 0, PreviousLapSectorTimes.Length);
        Array.Clear(BestSectorTimes, 0, BestSectorTimes.Length);
        LastCompletedSector = -1;
        Finished = false;
        FinishedAt = null;
        InPit = false;
        OnOutLap = false;
        LapStart = null;
        LapStartKnown = false;
        ChainLine = null;
        ChainAt = default;
        MaxLineThisLap = 0;
        LastFinishLineAt = null;
    }

    private static DeviceSessionData NewSession(TimingCompetitor competitor)
    {
        var session = new DeviceSessionData(competitor.Key, competitor.Key)
        {
            Generator = "LapViz.Telemetry.PassingTiming",
            RaceNumber = competitor.DisplayNumber,
            DriverDisplayName = competitor.Name,
        };
        session.Driver.Number = competitor.DisplayNumber;
        if (!string.IsNullOrWhiteSpace(competitor.Name))
            session.Driver.Name = competitor.Name;
        return session;
    }
}
