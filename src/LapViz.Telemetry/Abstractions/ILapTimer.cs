using System;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Abstractions;

/// <summary>
/// Consumes geolocation fixes for a configured circuit and emits sector/lap events,
/// managing the lifecycle of the active session.
/// </summary>
public interface ILapTimer
{
    /// <summary>Sets or replaces the circuit used for event detection. Closes any active session.</summary>
    void SetCircuit(CircuitConfiguration circuitConfiguration);

    /// <summary>
    /// Adds a single geolocation fix. Optionally tag the source with a <paramref name="deviceId"/>.
    /// </summary>
    /// <param name="geoTelemetryData">The geolocation fix (timestamped).</param>
    /// <param name="deviceId">Optional device identifier for the fix.</param>
    void AddGeolocation(GeoTelemetryData geoTelemetryData, string deviceId = null);

    /// <summary>The currently active session, or null if none.</summary>
    DeviceSessionData ActiveSession { get; }

    /// <summary>Creates and starts a new session bound to the current circuit.</summary>
    DeviceSessionData CreateSession();

    /// <summary>Stops event detection (no new events will be generated until restarted).</summary>
    void StopDetection();

    /// <summary>Starts or resumes event detection.</summary>
    void StartDetection();

    /// <summary>Closes and returns the active session (if any).</summary>
    DeviceSessionData CloseSession();

    /// <summary>True when detection is running (not paused).</summary>
    bool IsRunning { get; }

    /// <summary>The circuit currently used for detection.</summary>
    CircuitConfiguration CircuitConfiguration { get; }

    /// <summary>Raised when a new session event (sector/lap/position) is added.</summary>
    event EventHandler<SessionDataEvent> EventAdded;

    /// <summary>Raised when a session is created and becomes active.</summary>
    event EventHandler<DeviceSessionData> SessionStarted;

    /// <summary>Raised when the active session is closed.</summary>
    event EventHandler<DeviceSessionData> SessionEnded;

    /// <summary>Raised when detection is paused (session may remain open).</summary>
    event EventHandler<DeviceSessionData> SessionPaused;

    /// <summary>Raised when an error occurs during processing.</summary>
    event EventHandler<Exception> Error;
}
