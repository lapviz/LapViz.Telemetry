namespace LapViz.Telemetry.Domain
{
    /// <summary>
    /// Extension methods for <see cref="RaceRunType"/> display and querying.
    /// </summary>
    public static class RaceRunTypeExtensions
    {
        /// <summary>
        /// Gets the short display code for the run type (e.g., "R" for Race, "Q" for Qualifying).
        /// </summary>
        public static string ToShortCode(this RaceRunType runType)
        {
            switch (runType)
            {
                case RaceRunType.Practice: return "P";
                case RaceRunType.Qualifying: return "Q";
                case RaceRunType.Warmup: return "W";
                case RaceRunType.Race: return "R";
                case RaceRunType.Test: return "T";
                default: return "?";
            }
        }

        /// <summary>
        /// Gets the display name for the run type.
        /// </summary>
        public static string ToDisplayName(this RaceRunType runType)
        {
            switch (runType)
            {
                case RaceRunType.Practice: return "Practice";
                case RaceRunType.Qualifying: return "Qualifying";
                case RaceRunType.Warmup: return "Warmup";
                case RaceRunType.Race: return "Race";
                case RaceRunType.Test: return "Test";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Determines if this run type represents a competitive race session.
        /// </summary>
        public static bool IsRaceMode(this RaceRunType runType) => runType == RaceRunType.Race;

        /// <summary>
        /// Determines if this run type represents a timed/qualifying session (position based on best lap time).
        /// </summary>
        public static bool IsTimedSession(this RaceRunType runType) =>
            runType == RaceRunType.Practice ||
            runType == RaceRunType.Qualifying ||
            runType == RaceRunType.Warmup ||
            runType == RaceRunType.Test;
    }
}
