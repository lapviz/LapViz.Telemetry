namespace LapViz.Telemetry.Domain;

/// <summary>
/// Racing status of a competitor in a session.
/// </summary>
public enum RaceEntryStatus
{
    /// <summary>Status is not known or not yet set.</summary>
    Unknown,

    /// <summary>The competitor is on track and running normally.</summary>
    Running,

    /// <summary>The competitor is currently in the pit lane.</summary>
    InPit,

    /// <summary>The competitor is on an out-lap (leaving the pits).</summary>
    OutLap,

    /// <summary>The competitor did not finish the race.</summary>
    DNF,

    /// <summary>The competitor did not start the race.</summary>
    DNS,

    /// <summary>The competitor has been disqualified.</summary>
    DSQ,

    /// <summary>The competitor has crossed the finish line.</summary>
    Finished
}
