using System;
using System.Collections.Generic;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents the configuration of a racing circuit, including metadata, geometry,
/// segmentation, and operational parameters used for telemetry processing.
/// </summary>
public class CircuitConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier of the circuit.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name of the circuit.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the textual description of the location (e.g., city, venue).
    /// </summary>
    public string Location { get; set; }

    /// <summary>
    /// Gets or sets a short alphanumeric code representing the circuit.
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// Gets or sets the ISO 3166 country code where the circuit is located.
    /// </summary>
    public string CountryCode { get; set; }

    /// <summary>
    /// Gets or sets the list of segments that make up the circuit layout.
    /// Each segment represents a defined portion of the track.
    /// </summary>
    public IList<CircuitSegment> Segments { get; set; } = new List<CircuitSegment>();

    /// <summary>
    /// Gets or sets the bounding box that defines the geographic area of the circuit.
    /// </summary>
    public CircuitGeoLine BoundingBox { get; set; }

    /// <summary>
    /// Gets or sets the type of circuit (closed or open).
    /// </summary>
    public CircuitType Type { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the driving direction should be enforced.
    /// </summary>
    public bool UseDirection { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed time (in seconds) for completing a sector,
    /// used to filter invalid or incomplete lap data.
    /// </summary>
    public int SectorTimeout { get; set; }

    /// <summary>
    /// Gets or sets the central geographic coordinates of the circuit.
    /// </summary>
    public GeoCoordinates Center { get; set; }

    /// <summary>
    /// Gets or sets the map zoom level typically used to visualize the circuit.
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>
    /// Gets or sets the code used to query weather forecasts for this circuit.
    /// </summary>
    public string WeatherForecastCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this configuration is a test circuit
    /// and not intended for production use.
    /// </summary>
    public bool Test { get; set; }
    public DateTimeOffset Updated { get; set; }
}
