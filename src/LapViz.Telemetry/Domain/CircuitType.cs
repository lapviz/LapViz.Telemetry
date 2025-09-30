namespace LapViz.Telemetry.Domain;

/// <summary>
/// Defines the type of circuit layout in telemetry analysis. More types may be added in the future.
/// </summary>
public enum CircuitType
{
    /// <summary>
    /// An open circuit where the start and end points do not coincide.
    /// Example: point-to-point road stages or hill climbs.
    /// </summary>
    Open,

    /// <summary>
    /// A closed circuit where the track forms a loop,
    /// and the start and end points coincide.
    /// Example: karting, racing circuits, or running tracks.
    /// </summary>
    Closed
}
