namespace LapViz.Telemetry.Domain
{
    /// <summary>
    /// Track flag status during a race session.
    /// </summary>
    public enum RaceFlag
    {
        /// <summary>No flag / normal conditions.</summary>
        None,

        /// <summary>Track is clear, racing is underway.</summary>
        Green,

        /// <summary>Caution: hazard on track, no overtaking.</summary>
        Yellow,

        /// <summary>Session stopped, all cars must stop.</summary>
        Red,

        /// <summary>Session or race has ended.</summary>
        Checkered,

        /// <summary>Full-course caution (all sectors).</summary>
        FullCourseYellow,

        /// <summary>Safety car is deployed.</summary>
        SafetyCar,

        /// <summary>Virtual safety car is deployed.</summary>
        VirtualSafetyCar,

        /// <summary>Blue flag: let faster car pass.</summary>
        Blue,

        /// <summary>White flag: slow vehicle on track / last lap (context-dependent).</summary>
        White,

        /// <summary>Black flag: driver disqualified or must report to pit.</summary>
        Black
    }
}
