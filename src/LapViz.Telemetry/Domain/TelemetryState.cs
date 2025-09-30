namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents the lifecycle state of a device connection/session.
/// </summary>
public enum TelemetryState
{
    /// <summary>
    /// The device is initialized and ready to start, but idle.
    /// </summary>
    Ready,

    /// <summary>
    /// The device is starting up, initializing resources.
    /// </summary>
    Starting,

    /// <summary>
    /// The device has completed startup and is running,
    /// but not yet connected to any source.
    /// </summary>
    Started,

    /// <summary>
    /// Attempting to establish a connection to a telemetry source.
    /// </summary>
    Connecting,

    /// <summary>
    /// Successfully connected to the telemetry source.
    /// </summary>
    Connected,

    /// <summary>
    /// Actively receiving telemetry data from the source.
    /// </summary>
    Receiving,

    /// <summary>
    /// The device is shutting down gracefully.
    /// </summary>
    Stopping,

    /// <summary>
    /// The devicehas been fully stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// A failure occurred (connection lost, error in receiving, or fatal issue).
    /// </summary>
    Failed
}
