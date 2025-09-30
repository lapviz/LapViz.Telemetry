using System;
using System.Collections.Generic;
using LapViz.Telemetry.Abstractions;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents a single geo-referenced telemetry data point.
/// Inherits latitude, longitude, and altitude from <see cref="GeoCoordinates"/>,
/// and adds timing, speed, accuracy, and provider metadata.
/// </summary>
public class GeoTelemetryData : GeoCoordinates, ITelemetryData
{
    /// <summary>
    /// Optional ground speed in meters per second or km/h (depending on provider).
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Optional accuracy estimate for the location fix, typically in meters.
    /// Lower values mean higher accuracy.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Source of the telemetry data (e.g., "Garmin GLO", "RaceBox", "Simulated").
    /// Useful when multiple providers may be used.
    /// </summary>
    public string Provider { get; set; }

    /// <summary>
    /// Optional cumulative distance traveled, in meters or kilometers.
    /// </summary>
    public double? Distance { get; set; }

    /// <summary>
    /// Timestamp when this telemetry data point was captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Optional container for extra numerical telemetry channels
    /// associated with this data point (e.g., accelerometer values).
    /// </summary>
    public IList<double?> Data { get; set; }

    /// <summary>
    /// Initializes a new instance with an empty data list.
    /// </summary>
    public GeoTelemetryData()
    {
        Data = new List<double?>();
    }

    /// <summary>
    /// Initializes a new instance with specified latitude and longitude.
    /// </summary>
    public GeoTelemetryData(double latitude, double longitude) : this()
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>
    /// Returns a string representation of the coordinate in "lat,lon" format.
    /// Example: "50.12345,5.67890".
    /// </summary>
    public override string ToString()
    {
        return $"{Latitude},{Longitude}";
    }
}
