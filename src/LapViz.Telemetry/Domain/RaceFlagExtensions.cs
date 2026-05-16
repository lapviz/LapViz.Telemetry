namespace LapViz.Telemetry.Domain;

/// <summary>
/// Extension methods for <see cref="RaceFlag"/> display and querying.
/// </summary>
public static class RaceFlagExtensions
{
    /// <summary>
    /// Gets the display name for the flag.
    /// </summary>
    public static string ToDisplayName(this RaceFlag flag)
    {
        switch (flag)
        {
            case RaceFlag.None: return "";
            case RaceFlag.Green: return "Green";
            case RaceFlag.Yellow: return "Yellow";
            case RaceFlag.Red: return "Red";
            case RaceFlag.Blue: return "Blue";
            case RaceFlag.White: return "White";
            case RaceFlag.Black: return "Black";
            case RaceFlag.Checkered: return "Checkered";
            case RaceFlag.FullCourseYellow: return "Full Course Yellow";
            case RaceFlag.SafetyCar: return "Safety Car";
            case RaceFlag.VirtualSafetyCar: return "VSC";
            default: return "";
        }
    }

    /// <summary>
    /// Determines if the session is under green flag conditions (racing active).
    /// </summary>
    public static bool IsGreen(this RaceFlag flag) => flag == RaceFlag.Green;

    /// <summary>
    /// Determines if the session is under caution (yellow, safety car, FCY or VSC).
    /// </summary>
    public static bool IsCaution(this RaceFlag flag) =>
        flag == RaceFlag.Yellow ||
        flag == RaceFlag.FullCourseYellow ||
        flag == RaceFlag.SafetyCar ||
        flag == RaceFlag.VirtualSafetyCar;

    /// <summary>
    /// Determines if the session has ended (checkered flag).
    /// </summary>
    public static bool IsFinished(this RaceFlag flag) => flag == RaceFlag.Checkered;

    /// <summary>
    /// Determines if the session is stopped (red flag).
    /// </summary>
    public static bool IsStopped(this RaceFlag flag) => flag == RaceFlag.Red;
}
