using System;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Provides data for telemetry device state change events.
/// Fired whenever the device transitions to a new <see cref="TelemetryState"/>.
/// </summary>
public class TelemetryStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The new state of the device after the change.
    /// </summary>
    public TelemetryState State { get; set; }

    /// <summary>
    /// Optional human-readable message describing the state change.
    /// Useful for logging or displaying to the user (e.g., "Connecting to device...").
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// The timestamp when the state change occurred.
    /// Stored as local system time by default.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
