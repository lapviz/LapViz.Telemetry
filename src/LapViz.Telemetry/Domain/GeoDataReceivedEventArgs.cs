using System;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Event arguments used when a new piece of geo-telemetry data is received.
/// Carries both the data payload and the time it was received.
/// </summary>
public class GeoDataReceivedEventArgs : EventArgs
{
    /// <summary>
    /// The geo-telemetry message that was received.
    /// Contains GPS coordinates, speed, altitude, etc.
    /// </summary>
    public GeoTelemetryData Message { get; set; }

    /// <summary>
    /// The timestamp at which the message was received by the system.
    /// Not necessarily the same as the message’s internal timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
}
