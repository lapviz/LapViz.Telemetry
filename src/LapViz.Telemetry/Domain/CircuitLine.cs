using System;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Geographic line segment defined by two points, interpreted on a local planar projection
/// (latitude as X, longitude as Y).
public class CircuitGeoLine
{
    /// <summary>Segment start point.</summary>
    public GeoCoordinates Start { get; }

    /// <summary>Segment end point.</summary>
    public GeoCoordinates End { get; }

    public CircuitGeoLine(GeoCoordinates start, GeoCoordinates end)
    {
        if (start == null) throw new ArgumentNullException(nameof(start));
        if (end == null) throw new ArgumentNullException(nameof(end));
        Start = start;
        End = end;
    }

    /// <summary>
    /// Returns a midpoint using simple averaging in the lat, lon plane.
    /// </summary>
    public GeoCoordinates Center
    {
        get
        {
            return new GeoCoordinates(
                (Start.Latitude + End.Latitude) * 0.5,
                (Start.Longitude + End.Longitude) * 0.5);
        }
    }

    /// <summary>
    /// Returns true if a location lies within this segment axis-aligned bounding box.
    /// </summary>
    public bool IsWithinBox(GeoTelemetryData location)
    {
        var minLat = Math.Min(Start.Latitude, End.Latitude);
        var maxLat = Math.Max(Start.Latitude, End.Latitude);
        var minLon = Math.Min(Start.Longitude, End.Longitude);
        var maxLon = Math.Max(Start.Longitude, End.Longitude);

        return location.Latitude >= minLat && location.Latitude <= maxLat
            && location.Longitude >= minLon && location.Longitude <= maxLon;
    }

    /// <summary>
    /// Computes a "centeredness" factor of a point p relative to the segment [Start, End].
    /// - 1.0 means p is exactly in the middle (equidistant from Start and End).
    /// - 0.0 means p coincides with one of the endpoints.
    /// - Values between 0 and 1 indicate how balanced the distances are.
    /// 
    /// Use this when you care about symmetry between endpoints,
    /// for example when detecting if a point is near the midpoint of a track section.
    /// </summary>
    public double CenterFactor(GeoCoordinates p)
    {
        // Distance from p to each endpoint, using proper geodesic distance
        var d1 = Start.DistanceTo(p, 'K');
        var d2 = End.DistanceTo(p, 'K');
        var sum = d1 + d2;

        // Degenerate case: p coincides with both endpoints
        if (sum <= double.Epsilon)
            return 0.0;

        // The closer d1 and d2 are to each other, the closer p is to the middle
        var ratio = 1.0 - Math.Abs(d1 - d2) / sum;

        // Clamp ratio to [0,1] for numerical stability
        if (ratio < 0.0) return 0.0;
        if (ratio > 1.0) return 1.0;

        return ratio;
    }

    /// <summary>
    /// Computes the projection factor of a point p onto the segment [Start, End].
    /// - 0.0 means projection lies exactly at Start.
    /// - 1.0 means projection lies exactly at End.
    /// - Values between 0 and 1 represent the normalized position along the segment.
    /// 
    /// Use this when you want to know "where along the segment" p is located,
    /// for example when interpolating lap position or progress along a track section.
    /// </summary>
    public double ProjectionFactor(GeoCoordinates p)
    {
        // Treat latitude/longitude as a 2D plane for local projection.
        // Acceptable approximation for small distances.
        double x1 = Start.Latitude, y1 = Start.Longitude;
        double x2 = End.Latitude, y2 = End.Longitude;
        double xp = p.Latitude, yp = p.Longitude;

        // Segment direction vector
        double dx = x2 - x1;
        double dy = y2 - y1;

        // Squared length of the segment
        double len2 = dx * dx + dy * dy;

        // Degenerate case: Start and End are (almost) identical
        if (len2 <= double.Epsilon)
            return 0.0;

        // Parametric projection of p onto the line through [Start, End]
        // Formula: t = ((p - Start) · (End - Start)) / |End - Start|²
        double t = ((xp - x1) * dx + (yp - y1) * dy) / len2;

        // Clamp to [0,1] to keep within the segment
        if (t < 0.0) return 0.0;
        if (t > 1.0) return 1.0;

        return t;
    }

    /// <summary>
    /// Approximate segment length in meters using the GeoCoordinates distance method.
    /// </summary>
    public double LengthMeters => Start.DistanceTo(End, 'K') * 1000.0;

    /// <summary>
    /// Computes the intersection point of two closed line segments in latitude/longitude space,
    /// preserving the original algebra and directional gating.
    /// </summary>
    /// <param name="line">
    /// The other segment to test against.
    /// </param>
    /// <param name="crossingFilter">
    /// Directional filter relative to the apex convention:
    /// - <see cref="CrossingFilter.Any"/>: no directional constraint.
    /// - <see cref="CrossingFilter.TowardApex"/>: reject intersections when the denominator is negative.
    /// - <see cref="CrossingFilter.AwayFromApex"/>: reject intersections when the denominator is positive.
    /// </param>
    /// <param name="ignoredEpsilon">
    /// Reserved for compatibility; not used in this implementation.
    /// </param>
    /// <returns>
    /// The intersection point as <see cref="GeoCoordinates"/> if a valid intersection exists
    /// and satisfies the directional filter; otherwise <c>null</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Coordinates are treated in a local plane (latitude as X, longitude as Y).
    /// This is suitable for small-scale computations.
    /// </para>
    /// <para>
    /// The intersection is computed using the same formulas as the legacy implementation:
    /// <list type="bullet">
    ///   <item><description>
    /// Parameters <c>t1</c> and <c>t2</c> must lie within [0,1] for the segments to intersect.
    /// </description></item>
    ///   <item><description>
    /// Division by zero produces <c>Infinity</c>; this is used to detect parallel or degenerate cases.
    /// </description></item>
    ///   <item><description>
    /// Directional gating is based on the sign of the denominator
    /// (<c>cross(End-Start, line.End-line.Start)</c>).
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The apex side is conventionally the left side of this segment when traveling from Start to End.
    /// The denominator sign acts as a proxy for this orientation and is used for filtering according
    /// to <paramref name="crossingFilter"/>.
    /// </para>
    /// </remarks>
    public GeoCoordinates Intersect(CircuitGeoLine line, CrossingFilter crossingFilter, double ignoredEpsilon = 0.0)
    {
        if (line == null) throw new ArgumentNullException(nameof(line));

        // Coordinates convention: latitude -> X, longitude -> Y
        var dx12 = End.Latitude - Start.Latitude;
        var dy12 = End.Longitude - Start.Longitude;
        var dx34 = line.End.Latitude - line.Start.Latitude;
        var dy34 = line.End.Longitude - line.Start.Longitude;

        // Denominator: cross product of direction vectors
        var denominator = dy12 * dx34 - dx12 * dy34;

        // Legacy t1 formula
        var t1 = ((Start.Latitude - line.Start.Latitude) * dy34
                 + (line.Start.Longitude - Start.Longitude) * dx34) / denominator;

        // Parallel, collinear, or degenerate: no unique intersection
        if (double.IsInfinity(t1))
            return null;

        // Directional gating
        if (crossingFilter == CrossingFilter.TowardApex && denominator < 0)
            return null;

        if (crossingFilter == CrossingFilter.AwayFromApex && denominator > 0)
            return null;

        // Legacy t2 formula
        var t2 = ((line.Start.Latitude - Start.Latitude) * dy12
                 + (Start.Longitude - line.Start.Longitude) * dx12) / -denominator;

        // Intersection point
        var ix = Start.Latitude + dx12 * t1;
        var iy = Start.Longitude + dy12 * t1;

        // Closed-interval check
        var segmentsIntersect = t1 >= 0.0 && t1 <= 1.0 && t2 >= 0.0 && t2 <= 1.0;

        return segmentsIntersect ? new GeoCoordinates(ix, iy) : null;
    }

    /// <summary>
    /// Signed area magnitude of triangle (A,B,P), proportional to the z-component of the 2D cross product.
    /// Positive means P is to the left of AB, negative to the right, zero is collinear.
    /// </summary>
    public static double SignedArea(GeoCoordinates a, GeoCoordinates b, GeoCoordinates p)
    {
        var ax = a.Latitude; var ay = a.Longitude;
        var bx = b.Latitude; var by = b.Longitude;
        var px = p.Latitude; var py = p.Longitude;
        return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    }

    /// <summary>
    /// Parametric projection of p on this segment clamped to [0,1].
    /// 0 maps to Start, 1 maps to End.
    /// </summary>
    public double ParameterOf(GeoCoordinates p)
    {
        double x1 = Start.Latitude, y1 = Start.Longitude;
        double x2 = End.Latitude, y2 = End.Longitude;
        double dx = x2 - x1, dy = y2 - y1;
        double len2 = dx * dx + dy * dy;
        if (len2 <= double.Epsilon) return 0.0;
        double t = ((p.Latitude - x1) * dx + (p.Longitude - y1) * dy) / len2;
        if (t < 0) return 0;
        if (t > 1) return 1;
        return t;
    }
}
