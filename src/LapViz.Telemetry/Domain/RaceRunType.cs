namespace LapViz.Telemetry.Domain
{
    /// <summary>
    /// Type of a session or run within an event.
    /// </summary>
    public enum RaceRunType
    {
        /// <summary>Unknown or unclassified session type.</summary>
        Unknown,

        /// <summary>Free practice session.</summary>
        Practice,

        /// <summary>Qualifying session to determine grid order.</summary>
        Qualifying,

        /// <summary>Warmup session before the race.</summary>
        Warmup,

        /// <summary>Competitive race session.</summary>
        Race,

        /// <summary>Test session.</summary>
        Test
    }
}
