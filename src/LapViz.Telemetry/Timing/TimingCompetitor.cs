namespace LapViz.Telemetry.Timing;

/// <summary>A competitor of a timed session, identified by the key its passings carry (transponder number).</summary>
public sealed class TimingCompetitor
{
    /// <summary>Transponder number or device id of the passings.</summary>
    public string Key { get; set; } = "";

    /// <summary>Racing number; the key when empty.</summary>
    public string Number { get; set; } = "";

    /// <summary>Display name of the driver or the team.</summary>
    public string Name { get; set; } = "";

    /// <summary>Class or category.</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Grid position (1-based), 0 when unknown.</summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// True when the key was not registered: the competitor was created by its first passing and waits for
    /// the operator to assign it.
    /// </summary>
    public bool IsUnknown { get; set; }

    /// <summary>The racing number, or the key when there is none.</summary>
    public string DisplayNumber => string.IsNullOrWhiteSpace(Number) ? Key : Number;
}
