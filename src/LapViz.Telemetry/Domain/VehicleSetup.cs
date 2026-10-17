namespace LapViz.Telemetry.Domain;

/// <summary>
/// State of the set of tyres at the start of a session.
/// </summary>
public enum TyreCondition
{
    /// <summary>Never driven.</summary>
    New,

    /// <summary>Heated once for a few laps: the best grip.</summary>
    Scrubbed,

    /// <summary>Driven several sessions.</summary>
    Used,

    /// <summary>At the end of their life.</summary>
    Worn,
}

/// <summary>
/// Setup sheet of a session (karting first): tyres, pressures, gearing, carburation, chassis, and how the driver
/// felt the vehicle with it. Every member is optional.
/// </summary>
public class VehicleSetup
{
    /// <summary>Highest value of <see cref="Feeling"/>.</summary>
    public const int MaxFeeling = 5;

    /// <summary>Tyres (make, compound). Example: "MG Red".</summary>
    public string? Tyres { get; set; }

    /// <summary>State of the tyres at the start of the session.</summary>
    public TyreCondition? TyreCondition { get; set; }

    /// <summary>Laps already driven on the set of tyres at the start of the session.</summary>
    public int? TyreLaps { get; set; }

    /// <summary>Cold pressure of the front tyres, in bar.</summary>
    public double? PressureFront { get; set; }

    /// <summary>Cold pressure of the rear tyres, in bar.</summary>
    public double? PressureRear { get; set; }

    /// <summary>Front sprocket (pinion), number of teeth.</summary>
    public int? GearingFront { get; set; }

    /// <summary>Rear sprocket (crown), number of teeth.</summary>
    public int? GearingRear { get; set; }

    /// <summary>Carburation settings (jets, needle...).</summary>
    public string? Carburation { get; set; }

    /// <summary>Chassis settings: axle, track width, ride height, stiffness...</summary>
    public string? Chassis { get; set; }

    /// <summary>Feeling of the driver with this setup, from 1 (bad) to <see cref="MaxFeeling"/> (perfect).</summary>
    public int? Feeling { get; set; }

    /// <summary>Gets a value indicating whether no member has a value.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Tyres) && TyreCondition == null && TyreLaps == null && PressureFront == null &&
        PressureRear == null && GearingFront == null && GearingRear == null && string.IsNullOrWhiteSpace(Carburation) &&
        string.IsNullOrWhiteSpace(Chassis) && Feeling == null;
}
