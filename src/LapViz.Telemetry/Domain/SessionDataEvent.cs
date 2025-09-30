using System;
using System.Collections.Generic;
using System.Globalization;
using LapViz.Telemetry.Abstractions;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents a timing or positional event within a telemetry session,
/// such as a lap completion, sector split, or GPS position.
/// </summary>
public class SessionDataEvent : ITelemetryData, ICloneable
{
    /// <summary>
    /// Event timestamp (UTC recommended).
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Event kind, for example Lap or Sector.
    /// </summary>
    public SessionEventType Type { get; set; }

    /// <summary>
    /// Sector index for sector events. 0 for non-sector events.
    /// </summary>
    public int Sector { get; set; }

    /// <summary>
    /// Duration carried by this event. For laps and sectors, this is the time.
    /// For position events, this can be TimeSpan.Zero.
    /// </summary>
    public TimeSpan Time { get; set; }

    /// <summary>
    /// Lap number for lap or sector events. 0 for non-lap events.
    /// </summary>
    public int LapNumber { get; set; }

    /// <summary>
    /// Device identifier that produced this event.
    /// </summary>
    public string DeviceId { get; set; }

    /// <summary>
    /// User identifier linked to the device or session.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Session identifier tying events together.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Circuit code where this event occurred.
    /// </summary>
    public string CircuitCode { get; set; }

    /// <summary>
    /// First geo coordinate associated with the event, for example the crossing point.
    /// </summary>
    public GeoCoordinates FirstGeoCoordinates { get; set; }

    /// <summary>
    /// Second geo coordinate associated with the event, for example the next point.
    /// </summary>
    public GeoCoordinates SecondGeoCoordinates { get; set; }

    /// <summary>
    /// True if this is the driver's best value so far in the session.
    /// </summary>
    public bool IsBestOverall { get; set; }

    /// <summary>
    /// True if this is the overall session best value so far.
    /// </summary>
    public bool IsPersonnalBest { get; set; }

    /// <summary>
    /// Back-reference to the driver session container. Ignored for JSON.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public DeviceSessionData DriverRace { get; set; }

    /// <summary>
    /// Optional per-channel minima aggregated for this event window.
    /// </summary>
    public IList<double?> DataMin { get; set; }

    /// <summary>
    /// Optional per-channel maxima aggregated for this event window.
    /// </summary>
    public IList<double?> DataMax { get; set; }

    /// <summary>
    /// Generic factor associated with the event. For example, a projection or confidence factor.
    /// </summary>
    public double Factor { get; set; }

    /// <summary>
    /// ITelemetryData numeric vector. Order is:
    /// [ Type, LapNumber, Sector, Time(ms),
    ///   First.lat, First.lon, Second.lat, Second.lon, Factor ].
    /// </summary>
    public IList<double?> Data
    {
        get
        {
            return new double?[]
            {
                (double)Type,
                LapNumber,
                Sector,
                Time.TotalMilliseconds,
                FirstGeoCoordinates != null ? FirstGeoCoordinates.Latitude : 0,
                FirstGeoCoordinates != null ? FirstGeoCoordinates.Longitude : 0,
                SecondGeoCoordinates != null ? SecondGeoCoordinates.Latitude : 0,
                SecondGeoCoordinates != null ? SecondGeoCoordinates.Longitude : 0,
                Factor
            };
        }
        set
        {
            // No-op. Vector is derived from properties.
        }
    }

    /// <summary>
    /// Soft-delete marker. When set, the event should be treated as deleted.
    /// </summary>
    public DateTime? Deleted { get; set; }

    /// <summary>
    /// Human-friendly line for logs. Includes timestamp, user, type, info, time, device, session.
    /// </summary>
    public override string ToString()
    {
        return $"{Timestamp}: {UserId}> {Type} {GetInfo()} - ({Time:mm\\:ss\\.fff}) [{DeviceId}] {{{SessionId}}}";
    }

    /// <summary>
    /// Returns a compact info string depending on event type.
    /// </summary>
    private string GetInfo()
    {
        switch (Type)
        {
            case SessionEventType.Lap: return LapNumber.ToString(CultureInfo.InvariantCulture);
            case SessionEventType.Sector: return $"{LapNumber.ToString(CultureInfo.InvariantCulture)}.{Sector.ToString(CultureInfo.InvariantCulture)}";
            case SessionEventType.Position: return SecondGeoCoordinates?.ToString() ?? string.Empty;
            default: return string.Empty;
        }
    }

    /// <summary>
    /// Creates a deep copy. Coordinates are cloned when available.
    /// Note, DriverRace is a back-reference and is copied as-is.
    /// </summary>
    public object Clone()
    {
        return new SessionDataEvent
        {
            Timestamp = Timestamp,
            Sector = Sector,
            Type = Type,
            FirstGeoCoordinates = FirstGeoCoordinates != null
                ? (GeoCoordinates)FirstGeoCoordinates.Clone()
                : null,
            SecondGeoCoordinates = SecondGeoCoordinates != null
                ? (GeoCoordinates)SecondGeoCoordinates.Clone()
                : null,
            UserId = UserId,
            DeviceId = DeviceId,
            Factor = Factor,
            Time = Time,
            LapNumber = LapNumber,
            DriverRace = DriverRace,
            CircuitCode = CircuitCode,
            SessionId = SessionId,
            IsBestOverall = IsBestOverall,
            IsPersonnalBest = IsPersonnalBest,
            DataMin = DataMin != null ? new List<double?>(DataMin) : null,
            DataMax = DataMax != null ? new List<double?>(DataMax) : null,
            Deleted = Deleted
        };
    }
}
