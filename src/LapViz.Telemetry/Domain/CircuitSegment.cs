namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents a circuit sector, defined by a line (crossing boundary) and a sequential number.
/// 
/// Direction convention:
/// To establish the "valid" crossing direction, we consider the line as the base of an
/// isosceles triangle:
///   - Point B = Start of the line
///   - Point C = End of the line
///   - Point A = virtual apex of the isosceles triangle
/// The valid direction of passage is defined as crossing from the B-side to the C-side,
/// relative to the orientation implied by triangle ABC.
/// </summary>
public class CircuitSegment
{
    /// <summary>
    /// Gets or sets the geometric line that marks the boundary of this segment.
    /// This line is also used to determine crossing direction (see class remarks).
    /// </summary>
    public CircuitGeoLine Boundary { get; set; }

    /// <summary>
    /// Gets or sets the segment number (e.g., 1, 2, 3...).
    /// Determines the order of segments along the track.
    /// </summary>
    public int Number { get; set; }
}
