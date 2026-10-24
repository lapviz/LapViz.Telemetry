using System;

namespace LapViz.Telemetry.Timing;

/// <summary>
/// The timing lines of a track. Lines 1..<see cref="SectorLines"/> are in track order, line 1 being the
/// start/finish line: crossing line k &gt; 1 ends sector k - 1 and crossing line 1 ends the last sector,
/// as the GPS lap timer numbers the lines of a closed circuit. One line (the common karting case) gives
/// laps without sectors. Pit lines are not sectors: they only change the status of the competitor.
/// </summary>
public sealed class TimingLineLayout
{
    /// <summary>The start/finish line.</summary>
    public const int FinishLine = 1;

    /// <summary>Creates a layout with <paramref name="sectorLines"/> lines on the track.</summary>
    public TimingLineLayout(int sectorLines = 1, int? pitInLine = null, int? pitOutLine = null)
    {
        if (sectorLines < 1)
            throw new ArgumentOutOfRangeException(nameof(sectorLines), "A layout has at least the finish line.");
        if (pitInLine is { } pitIn && pitIn >= 1 && pitIn <= sectorLines)
            throw new ArgumentException("The pit in line must not be a sector line.", nameof(pitInLine));
        if (pitOutLine is { } pitOut && pitOut >= 1 && pitOut <= sectorLines)
            throw new ArgumentException("The pit out line must not be a sector line.", nameof(pitOutLine));
        if (pitInLine.HasValue && pitInLine == pitOutLine)
            throw new ArgumentException("The pit in and pit out lines must differ.", nameof(pitOutLine));

        SectorLines = sectorLines;
        PitInLine = pitInLine;
        PitOutLine = pitOutLine;
    }

    /// <summary>Number of lines on the track, the finish line included (= number of sectors when &gt; 1).</summary>
    public int SectorLines { get; }

    /// <summary>Number of sectors of a lap: 0 with the finish line only.</summary>
    public int SectorCount => SectorLines > 1 ? SectorLines : 0;

    /// <summary>Line at the pit lane entry, null when there is none.</summary>
    public int? PitInLine { get; }

    /// <summary>Line at the pit lane exit, null when there is none.</summary>
    public int? PitOutLine { get; }

    /// <summary>True when <paramref name="line"/> is a line of the track (finish line or intermediate line).</summary>
    public bool IsSectorLine(int line) => line >= 1 && line <= SectorLines;

    /// <summary>True when <paramref name="line"/> is a line of the layout.</summary>
    public bool IsKnownLine(int line) => IsSectorLine(line) || line == PitInLine || line == PitOutLine;

    /// <summary>Sector ended by crossing <paramref name="line"/> (1-based), 0 without sectors.</summary>
    public int SectorEndedBy(int line)
    {
        if (SectorCount == 0 || !IsSectorLine(line))
            return 0;
        return line == FinishLine ? SectorLines : line - 1;
    }

    /// <summary>The line crossed just before <paramref name="line"/> on a lap without missed line.</summary>
    public int PreviousLine(int line) => line == FinishLine ? SectorLines : line - 1;
}
