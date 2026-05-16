using System;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// A race control or event announcement (e.g. penalty, track status change).
/// </summary>
public class RaceAnnouncement
{
    /// <summary>
    /// Timestamp of the announcement.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Announcement text content.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Optional category (e.g. "Penalty", "Track Status").
    /// </summary>
    public string Category { get; set; }
}
