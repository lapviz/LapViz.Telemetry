namespace LapViz.Telemetry.Domain;

/// <summary>
/// Track flag status during a race session.
/// </summary>
public enum RaceFlag
{
    /// <summary>No flag / normal conditions.</summary>
    None = 0,

    /// <summary>Track is clear, racing is underway.</summary>
    Green = 1,

    /// <summary>Caution: hazard on track, no overtaking.</summary>
    Yellow = 2,

    /// <summary>Session stopped, all cars must stop.</summary>
    Red = 3,

    /// <summary>Blue flag: let faster car pass.</summary>
    Blue = 4,

    /// <summary>White flag: slow vehicle on track / last lap (context-dependent).</summary>
    White = 5,

    /// <summary>Black flag: driver disqualified or must report to pit.</summary>
    Black = 6,

    /// <summary>Session or race has ended.</summary>
    Checkered = 7,

    /// <summary>Safety car is deployed.</summary>
    SafetyCar = 8,

    /// <summary>Virtual safety car is deployed.</summary>
    VirtualSafetyCar = 9,

    /// <summary>Full-course caution (all sectors).</summary>
    FullCourseYellow = 10
}
