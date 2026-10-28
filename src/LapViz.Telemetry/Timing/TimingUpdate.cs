using System;
using System.Collections.Generic;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Timing;

/// <summary>What a passing or a command changed in a <see cref="PassingTimingSession"/>.</summary>
public sealed class TimingUpdate
{
    internal TimingUpdate(bool rebuilt, IReadOnlyList<SessionDataEvent> events, PassingRejection? rejection, bool flagChanged)
    {
        Rebuilt = rebuilt;
        Events = events;
        Rejection = rejection;
        FlagChanged = flagChanged;
    }

    /// <summary>
    /// The whole session was recomputed (a passing or a command older than the last one processed, an
    /// invalidation, a rule change): publish the full state, <see cref="Events"/> is empty.
    /// </summary>
    public bool Rebuilt { get; }

    /// <summary>Sector and lap events added, in order (empty when <see cref="Rebuilt"/>).</summary>
    public IReadOnlyList<SessionDataEvent> Events { get; }

    /// <summary>Why the passing does not count, null when it counts or for a command.</summary>
    public PassingRejection? Rejection { get; }

    /// <summary>The track flag changed (start, flag, checkered).</summary>
    public bool FlagChanged { get; }

    internal static TimingUpdate Of(IReadOnlyList<SessionDataEvent>? events, PassingRejection? rejection, bool flagChanged) =>
        new TimingUpdate(false, events ?? Array.Empty<SessionDataEvent>(), rejection, flagChanged);

    internal static TimingUpdate OfRebuild(PassingRejection? rejection, bool flagChanged) =>
        new TimingUpdate(true, Array.Empty<SessionDataEvent>(), rejection, flagChanged);
}

/// <summary>One line of a classification.</summary>
public sealed class TimingClassificationRow
{
    internal TimingClassificationRow(int position, CompetitorTiming timing)
    {
        Position = position;
        Timing = timing;
    }

    /// <summary>Position (1-based).</summary>
    public int Position { get; }

    /// <summary>The timing of the competitor.</summary>
    public CompetitorTiming Timing { get; }

    /// <summary>The competitor.</summary>
    public TimingCompetitor Competitor => Timing.Competitor;

    /// <summary>Gap to the leader in time, null for the leader, when a lap down or unknown.</summary>
    public TimeSpan? GapTime { get; internal set; }

    /// <summary>Gap to the leader in laps (race), 0 when on the same lap.</summary>
    public int GapLaps { get; internal set; }

    /// <summary>Interval to the competitor ahead in time, null for the leader, when a lap down or unknown.</summary>
    public TimeSpan? IntervalTime { get; internal set; }

    /// <summary>Interval to the competitor ahead in laps (race), 0 when on the same lap.</summary>
    public int IntervalLaps { get; internal set; }
}
