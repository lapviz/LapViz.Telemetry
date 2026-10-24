using System;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Timing;

/// <summary>How the timing of a competitor starts.</summary>
public enum TimingStartMode
{
    /// <summary>
    /// The first passing on the finish line starts lap 1 and has no time (practice, qualifying, rolling start).
    /// </summary>
    FirstCrossing,

    /// <summary>
    /// The session starts on the start command (standing start, start signal): the first passing on the
    /// finish line after it completes lap 1, timed from the start.
    /// </summary>
    Signal
}

/// <summary>How the competitors are ranked.</summary>
public enum TimingClassificationMode
{
    /// <summary>By best lap (practice, qualifying).</summary>
    BestLap,

    /// <summary>By laps completed, then by the time the last lap was completed (race).</summary>
    Race
}

/// <summary>Rules of a timed session. Durations are in decoder time.</summary>
public sealed class TimingRules
{
    /// <summary>Type of the session, published in the race state.</summary>
    public RaceRunType RunType { get; set; } = RaceRunType.Practice;

    /// <summary>Ranking of the competitors.</summary>
    public TimingClassificationMode Classification { get; set; } = TimingClassificationMode.BestLap;

    /// <summary>How the timing of a competitor starts.</summary>
    public TimingStartMode StartMode { get; set; } = TimingStartMode.FirstCrossing;

    /// <summary>
    /// Length of the session in time, null when not timed. When it has elapsed, the checkered flag goes out:
    /// at once by best lap, at the leader's next finish line passing in a race.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Length of the session in laps (0 = not lap based): the checkered flag goes out when the leader completes them.</summary>
    public int Laps { get; set; }

    /// <summary>A finish line passing sooner than this after the previous one is rejected. Zero = no minimum.</summary>
    public TimeSpan MinLapTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// A passing of a competitor on the same line sooner than this after the previous one is a duplicate
    /// (two antennas, a decoder repeat) and is ignored.
    /// </summary>
    public TimeSpan MinPassingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Rules of a practice session: by best lap, timing from the first passing.</summary>
    public static TimingRules Practice(TimeSpan? duration = null) => new TimingRules
    {
        RunType = RaceRunType.Practice,
        Classification = TimingClassificationMode.BestLap,
        StartMode = TimingStartMode.FirstCrossing,
        Duration = duration,
    };

    /// <summary>Rules of a qualifying session: by best lap, timing from the first passing.</summary>
    public static TimingRules Qualifying(TimeSpan? duration = null) => new TimingRules
    {
        RunType = RaceRunType.Qualifying,
        Classification = TimingClassificationMode.BestLap,
        StartMode = TimingStartMode.FirstCrossing,
        Duration = duration,
    };

    /// <summary>Rules of a race over laps and/or time.</summary>
    public static TimingRules Race(int laps = 0, TimeSpan? duration = null, TimingStartMode startMode = TimingStartMode.Signal) => new TimingRules
    {
        RunType = RaceRunType.Race,
        Classification = TimingClassificationMode.Race,
        StartMode = startMode,
        Laps = laps,
        Duration = duration,
    };
}
