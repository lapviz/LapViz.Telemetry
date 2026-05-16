namespace LapViz.Telemetry.Domain;

/// <summary>
/// A scheduled session entry in an event timetable.
/// </summary>
public class RaceScheduleEntry
{
    /// <summary>
    /// Scheduled start time as a display string.
    /// </summary>
    public string DateTime { get; set; }

    /// <summary>
    /// Class or group name.
    /// </summary>
    public string GroupName { get; set; }

    /// <summary>
    /// Session or run name.
    /// </summary>
    public string RunName { get; set; }

    /// <summary>
    /// Type of session.
    /// </summary>
    public RaceRunType RunType { get; set; }
}
