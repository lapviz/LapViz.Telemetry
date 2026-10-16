namespace LapViz.Telemetry.Domain;

/// <summary>
/// An external link associated with a race event.
/// </summary>
public class RaceLink
{
    /// <summary>
    /// Display text for the link.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Target URL.
    /// </summary>
    public string Url { get; set; }
}
