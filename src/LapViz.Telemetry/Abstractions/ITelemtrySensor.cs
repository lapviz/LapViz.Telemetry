using System;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Abstractions;

public interface ITelemtrySensor : IDisposable
{
    /// <summary>
    /// Starts the sensor. Should be safe to call multiple times.
    /// Returns true if the call caused a state transition to a running state.
    /// </summary>
    bool Start();

    /// <summary>
    /// Stops the sensor and releases any transport resources that can be reopened later.
    /// Returns true if the call caused a state transition to a stopped state.
    /// </summary>
    bool Stop();

    /// <summary>
    /// Total number of messages successfully parsed and surfaced to the application.
    /// </summary>
    long MessagesReceived { get; }


    /// <summary>
    /// Total number of errors encountered.
    /// </summary>
    int Errors { get; }

    /// <summary>
    /// Stable unique identifier for this sensor instance or its underlying device.
    /// Useful for logs, metrics, and deduplication.
    /// </summary>
    string UniqueId { get; }

    /// <summary>
    /// Current state of the sensor.
    /// </summary>
    TelemetryState State { get; }

    /// <summary>
    /// Fired when a batch of telemetry samples is received and decoded.
    /// </summary>
    event EventHandler<GeoDataReceivedEventArgs> DataReceived;

    /// <summary>
    /// Fired on state transitions such as Connecting, Connected, Failed, Stopped.
    /// </summary>
    event EventHandler<TelemetryStateChangedEventArgs> StateChanged;

    /// <summary>
    /// Fired when a recoverable or terminal error occurs.
    /// Implementations should also reflect the error in State when appropriate.
    /// </summary>
    event EventHandler<Exception> Error;
}
