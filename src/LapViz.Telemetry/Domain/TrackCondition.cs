namespace LapViz.Telemetry.Domain;

/// <summary>
/// Track surface condition.
/// </summary>
public enum TrackCondition
{
    /// <summary>Condition is unknown or not reported.</summary>
    Unknown,

    /// <summary>Dry track surface.</summary>
    Dry,

    /// <summary>Fully wet track surface.</summary>
    Wet,

    /// <summary>Damp track surface (transitional).</summary>
    Damp,

    /// <summary>Track is drying after rain.</summary>
    Drying
}
