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
    /// Computes the great-circle distance between this point and another using the spherical law of cosines.
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
        var theta = Longitude - intersect.Longitude;
        var rtheta = Math.PI * theta / 180;

        // Apply spherical law of cosines
        var dist = Math.Sin(rlat1) * Math.Sin(rlat2) +
                   Math.Cos(rlat1) * Math.Cos(rlat2) * Math.Cos(rtheta);

        // Numerical safety
        if (dist > 1.0) dist = 1.0;
        if (dist < -1.0) dist = -1.0;

        dist = Math.Acos(dist);
        dist = dist * 180 / Math.PI;
        dist = dist * 60 * 1.1515; // distance in miles

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
