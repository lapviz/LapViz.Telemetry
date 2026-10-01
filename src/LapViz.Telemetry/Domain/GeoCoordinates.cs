using System;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents a geographic location with latitude, longitude, and optional altitude.
/// Provides basic utilities such as distance calculation and cloning.
/// </summary>
public class GeoCoordinates : ICloneable
{
    /// <summary>
    /// Latitude in decimal degrees. Positive = North, Negative = South.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitude in decimal degrees. Positive = East, Negative = West.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Altitude in meters above sea level. Default = 0.
    /// </summary>
    public double Altitude { get; set; }

    /// <summary>
    /// Default constructor. Creates an empty coordinate (0,0,0).
    /// </summary>
    public GeoCoordinates()
    {
    }

    /// <summary>
    /// Constructs a coordinate with latitude, longitude, and optional altitude.
    /// </summary>
    public GeoCoordinates(double latitude, double longitude, double altitude = 0)
    {
        Latitude = latitude;
        Longitude = longitude;
        Altitude = altitude;
    }

    /// <summary>
    /// Returns a culture-invariant string representation: "lat, lon".
    /// Example: "50.12345, 5.67890".
    /// </summary>
    public override string ToString()
    {
        FormattableString message = $"{Latitude}, {Longitude}";
        return FormattableString.Invariant(message);
    }

    /// <summary>
    /// Computes the great-circle distance between this point and another using the haversine formula,
    /// which stays accurate down to centimeters (the law of cosines loses precision below a few meters).
    /// </summary>
    /// <param name="intersect">The other coordinate.</param>
    /// <param name="unit">
    /// Unit of measure:
    ///   'K' = kilometers (default),
    ///   'M' = miles,
    ///   'N' = nautical miles.
    /// </param>
    /// <returns>The distance between the two coordinates in the requested unit.</returns>
    public double DistanceTo(GeoCoordinates intersect, char unit = 'K')
    {
        // Convert degrees to radians
        var rlat1 = Math.PI * Latitude / 180;
        var rlat2 = Math.PI * intersect.Latitude / 180;
        var dlat = rlat2 - rlat1;
        var dlon = Math.PI * (intersect.Longitude - Longitude) / 180;

        // Haversine
        var sinDlat = Math.Sin(dlat / 2);
        var sinDlon = Math.Sin(dlon / 2);
        var h = sinDlat * sinDlat + Math.Cos(rlat1) * Math.Cos(rlat2) * sinDlon * sinDlon;
        if (h > 1.0) h = 1.0; // numerical safety
        var centralAngle = 2 * Math.Asin(Math.Sqrt(h));

        // Same Earth model as before (60 * 1.1515 miles per degree), so results stay consistent
        var dist = centralAngle * 180 / Math.PI * 60 * 1.1515; // distance in miles

        // Convert units
        switch (unit)
        {
            case 'K': // kilometers (default)
                return dist * 1.609344;
            case 'N': // nautical miles
                return dist * 0.8684;
            case 'M': // miles
                return dist;
            default:
                return dist;
        }
    }

    /// <summary>
    /// Creates a shallow clone of this object.
    /// </summary>
    public object Clone()
    {
        return new GeoCoordinates(Latitude, Longitude, Altitude);
    }
}
