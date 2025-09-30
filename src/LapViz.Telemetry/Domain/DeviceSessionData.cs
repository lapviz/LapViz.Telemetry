using System;
using System.Collections.Generic;
using LapViz.Telemetry.Abstractions;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents all telemetry and timing information collected from a single device session.
/// A session corresponds to one recording event from a device (GPS, sensors, etc.)
/// associated with a driver on a specific circuit.
/// </summary>
public class DeviceSessionData : SessionEvents
{
    /// <summary>
    /// Default constructor. Initializes collections and default values.
    /// </summary>
    public DeviceSessionData()
    {
        TimingData = new List<SessionEvents>();
        TelemetryData = new List<ITelemetryData>();
        TelemetryChannels = new List<string>();
        Driver = new Driver();

        // Short, shareable identifier (8 chars) for external references (e.g., URLs, invites).
        ShareId = Guid.NewGuid().ToString().Substring(0, 8);

        CreatedDate = DateTime.Now;
    }

    /// <summary>
    /// Constructs a session tied to a specific device and driver identity.
    /// If no driverId is provided, the deviceId is used as a placeholder.
    /// </summary>
    public DeviceSessionData(string deviceId, string driverId) : this()
    {
        Driver.Id = string.IsNullOrEmpty(driverId) ? deviceId : driverId;
        Driver.Name = string.IsNullOrEmpty(driverId) ? deviceId : driverId;
        Driver.Number = "Unknown";

        DeviceId = deviceId;
    }

    /// <summary>
    /// Unique session identifier (database or persistence ID).
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The last known GPS position of the device during the session.
    /// </summary>
    public GeoTelemetryData LastPosition { get; set; }

    /// <summary>
    /// Timestamp associated with the last known position.
    /// </summary>
    public DateTimeOffset LastPositionTS { get; set; }

    /// <summary>
    /// The driver associated with this session.
    /// </summary>
    public Driver Driver { get; set; }

    /// <summary>
    /// Unique identifier of the device (hardware ID or software ID).
    /// </summary>
    public string DeviceId { get; set; }

    /// <summary>
    /// Short identifier intended for external sharing.
    /// </summary>
    public string ShareId { get; set; }

    /// <summary>
    /// Identifier of the broader session context (e.g., event or race session).
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Timing-related events (laps, sectors, positions) recorded in the session.
    /// </summary>
    public IList<SessionEvents> TimingData { get; set; }

    /// <summary>
    /// List of telemetry channel names actively recorded during this session.
    /// Example: "GPS Speed", "RPM".
    /// </summary>
    public IList<string> TelemetryChannels { get; set; }

    /// <summary>
    /// Original list of telemetry channel names as received from the device.
    /// Useful for preserving raw mappings before renaming/normalization.
    /// </summary>
    public IList<string> TelemetryChannelsOriginal { get; set; }

    /// <summary>
    /// Actual telemetry data points (e.g., GPS, RPM, throttle, etc.).
    /// </summary>
    public IList<ITelemetryData> TelemetryData { get; set; }

    /// <summary>
    /// Weather information recorded or associated with the session.
    /// </summary>
    public WeatherInfo Weather { get; set; }

    #region State properties & methods

    /// <summary>
    /// Extracts telemetry data points relevant to a given session event.
    /// Finds all telemetry entries that occurred during the event's time window.
    /// </summary>
    /// <param name="sessionDataEvent">The session event (with timestamp and duration).</param>
    /// <returns>List of telemetry data recorded within the event window.</returns>
    public IList<ITelemetryData> GetTelemetryDataForEvent(SessionDataEvent sessionDataEvent)
    {
        var telemetryData = new List<ITelemetryData>();

        // Event covers the time interval [begin, end]
        DateTimeOffset begin = sessionDataEvent.Timestamp.ToUniversalTime().Add(-sessionDataEvent.Time);
        DateTimeOffset end = sessionDataEvent.Timestamp.ToUniversalTime();

        foreach (var telemetry in TelemetryData)
        {
            var ts = telemetry.Timestamp.ToUniversalTime();
            if (ts <= end && ts > begin)
                telemetryData.Add(telemetry);
        }

        return telemetryData;
    }

    #endregion

    #region Metadata

    /// <summary>
    /// Name of the team associated with the driver/session.
    /// Example: "Red Bull Racing".
    /// </summary>
    public string TeamName { get; set; }

    /// <summary>
    /// Race number assigned to the driver or vehicle for this session.
    /// Example: "303".
    /// </summary>
    public string RaceNumber { get; set; }

    /// <summary>
    /// Temporary filename used during processing or upload,
    /// before being stored permanently.
    /// </summary>
    public string TemporaryFilename { get; set; }

    /// <summary>
    /// Hash of the original source file, used to verify integrity
    /// and prevent duplicate imports.
    /// </summary>
    public string SourceFileHash { get; set; }

    /// <summary>
    /// Name of the file as originally provided (raw file name).
    /// </summary>
    public string OriginalFilename { get; set; }

    /// <summary>
    /// Code identifying the circuit where the session took place.
    /// Example: "SPA-FRANCORCHAMPS".
    /// </summary>
    public string CircuitCode { get; set; }

    /// <summary>
    /// Display name of the driver, for UI or reporting.
    /// Example: "John Doe".
    /// </summary>
    public string DriverDisplayName { get; set; }

    /// <summary>
    /// Name of the vehicle used in the session.
    /// Example: "Kart Rotax Max" or "Formula Renault".
    /// </summary>
    public string VehicleName { get; set; }

    /// <summary>
    /// Synchronization offset relative to the reference video timeline.
    /// This value is added to telemetry timestamps to align them with the video.
    /// Example: if the telemetry starts 2.5 seconds after the video,
    /// then Sync = 00:00:02.500.
    /// </summary>
    public TimeSpan VideoSync { get; set; }

    /// <summary>
    /// Brand of the recording device.
    /// Example: "GoPro", "LapViz", "RaceBox".
    /// </summary>
    public string DeviceBrand { get; set; }

    /// <summary>
    /// Model or user-friendly name of the recording device.
    /// Example: "LapViz Pro V2".
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    /// Date and time when this session record was created in the system.
    /// </summary>
    public DateTime CreatedDate { get; set; }

    #endregion

}
