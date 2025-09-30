namespace LapViz.Telemetry.IO;

/// <summary>
/// Supported timestamp formats for parsing telemetry data.
/// </summary>
public enum TimestampFormat
{
    /// <summary>
    /// Unix epoch time in milliseconds or seconds.
    /// </summary>
    UnixTime,

    /// <summary>
    /// ISO-8601 date/time strings (e.g. "2025-09-14T10:45:00Z").
    /// </summary>
    ISO8601
}
