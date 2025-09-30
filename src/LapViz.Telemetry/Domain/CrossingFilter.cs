namespace LapViz.Telemetry.Domain;

/// <summary>
/// Defines how to filter line crossings when detecting intersections
/// between a trajectory and a circuit segment.
/// </summary>
public enum CrossingFilter
{
    /// <summary>
    /// Accept any intersection, regardless of crossing direction.
    /// Useful when only the existence of a crossing matters.
    /// </summary>
    Any = 0,

    /// <summary>
    /// Accept only if the trajectory crosses from the "non-apex" side
    /// of the segment toward the apex side.  
    /// Example: detecting valid entry into a corner or sector gate.
    /// </summary>
    TowardApex = 1,

    /// <summary>
    /// Accept only if the trajectory crosses from the apex side
    /// back to the "non-apex" side.  
    /// Example: detecting exits, invalid reverse passes,
    /// or filtering out repeated crossings in the wrong direction.
    /// </summary>
    AwayFromApex = 2
}
