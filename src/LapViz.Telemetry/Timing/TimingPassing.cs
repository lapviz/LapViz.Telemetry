using System;

namespace LapViz.Telemetry.Timing;

/// <summary>
/// One passing of a competitor over a timing line: a transponder seen by a decoder loop, a LapViz device
/// (magnet, infrared beacon, GNSS line) or a line crossing detected by the GPS lap timer. The raw input of
/// <see cref="PassingTimingSession"/>; never modified once received (corrections are commands).
/// </summary>
public sealed class TimingPassing
{
    /// <summary>
    /// Unique id of the passing in the session (the decoder's passing number, or one given by the host).
    /// A passing received twice with the same id counts once.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Key of the competitor: transponder number or device id.</summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Timing line crossed: 1 = start/finish line, 2..N the intermediate lines in track order
    /// (<see cref="TimingLineLayout.SectorLines"/>), or a pit line of the layout.
    /// </summary>
    public int Line { get; set; } = TimingLineLayout.FinishLine;

    /// <summary>Time of the passing (decoder time).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Optional id of the decoder or device that saw the passing.</summary>
    public string? Source { get; set; }

    /// <summary>Optional signal strength reported by the decoder.</summary>
    public int? Strength { get; set; }

    /// <summary>Optional number of hits reported by the decoder.</summary>
    public int? Hits { get; set; }

    /// <summary>True when the operator added the passing (a passing the decoder missed).</summary>
    public bool Manual { get; set; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Id} {Key} L{Line} {Timestamp:HH:mm:ss.fff}{(Manual ? " (manual)" : "")}";
}
