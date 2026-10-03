using System;
using System.Collections.Generic;

namespace LapViz.Telemetry.IO;

/// <summary>Binary encoding of the values of a channel (LapViz format, <c>docs/lapviz-format.md</c>).</summary>
public enum LapVizValueType
{
    /// <summary>IEEE 754 double precision (8 bytes). The default; required for positions.</summary>
    Float64,

    /// <summary>IEEE 754 single precision (4 bytes).</summary>
    Float32,
}

/// <summary>How the samples are stored in the archive.</summary>
public enum LapVizSampleEncoding
{
    /// <summary><c>samples.bin</c>: little endian columns, compact and fast to read.</summary>
    Binary,

    /// <summary><c>samples.csv</c>: one line per sample, can be written while recording.</summary>
    Csv,
}

/// <summary>Software that wrote a LapViz file.</summary>
public sealed class LapVizGenerator
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

/// <summary>Logger of the session.</summary>
public sealed class LapVizDevice
{
    public string? Brand { get; set; }

    public string? Model { get; set; }

    public string? Serial { get; set; }

    public string? Firmware { get; set; }
}

/// <summary>Circuit of the session or of a timing source.</summary>
public sealed class LapVizCircuit
{
    /// <summary>Short identifier, e.g. the LapViz circuit code.</summary>
    public string? Code { get; set; }

    public string? Name { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? CountryCode { get; set; }
}

/// <summary>Description of the session.</summary>
public sealed class LapVizSessionInfo
{
    /// <summary>Time origin of the samples and of the laps.</summary>
    public DateTimeOffset Start { get; set; }

    public string? Driver { get; set; }

    public string? Vehicle { get; set; }

    public LapVizDevice? Device { get; set; }

    public LapVizCircuit? Circuit { get; set; }

    public string? Description { get; set; }

    /// <summary>Name of the file the session was converted from.</summary>
    public string? SourceFile { get; set; }

    /// <summary>Free metadata.</summary>
    public IDictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
}

/// <summary>A channel of the samples.</summary>
public sealed class LapVizChannel
{
    public LapVizChannel()
    {
    }

    public LapVizChannel(string name, string? unit = null, LapVizValueType type = LapVizValueType.Float64)
    {
        Name = name;
        Unit = unit;
        Type = type;
    }

    /// <summary>Name, unique in the file (case insensitive).</summary>
    public string Name { get; set; } = string.Empty;

    public string? Unit { get; set; }

    /// <summary>Encoding of the values in the binary samples.</summary>
    public LapVizValueType Type { get; set; }

    public string? Description { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Unit == null ? Name : Name + " (" + Unit + ")";
}

/// <summary>A lap of a timing source.</summary>
public sealed class LapVizLap
{
    /// <summary>Lap number; 0 is the out lap.</summary>
    public int Number { get; set; }

    /// <summary>Seconds from the start of the session to the crossing that started the lap.</summary>
    public double Start { get; set; }

    /// <summary>Lap time in seconds; null for an incomplete lap (the session ended before the line).</summary>
    public double? Duration { get; set; }

    /// <summary>The lap ended at the line (it has a lap time).</summary>
    public bool IsComplete => Duration.HasValue;

    /// <summary>Sector times in seconds, in order (empty without sectors).</summary>
    public IList<double> Sectors { get; set; } = new List<double>();

    /// <inheritdoc/>
    public override string ToString() => "Lap " + Number + ": " + (Duration.HasValue ? Duration.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s" : "incomplete");
}

/// <summary>Laps measured by one source: the logger, a lap timer, a timekeeping system, an analysis software.</summary>
public sealed class LapVizTimingSource
{
    /// <summary>Name of the source ("AiM", "LapViz", "Transponder"...).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True when the logger of the session measured these laps.</summary>
    public bool IsDevice { get; set; }

    public LapVizCircuit? Circuit { get; set; }

    /// <summary>The laps, ordered by start.</summary>
    public IList<LapVizLap> Laps { get; set; } = new List<LapVizLap>();
}

/// <summary>Options of <see cref="LapVizFile.Write(System.IO.Stream, LapVizWriteOptions)"/>.</summary>
public sealed class LapVizWriteOptions
{
    public LapVizSampleEncoding Encoding { get; set; } = LapVizSampleEncoding.Binary;

    /// <summary>Shuffle the bytes of the binary columns (much better compression of sensor data).</summary>
    public bool ByteShuffle { get; set; } = true;

    /// <summary>
    /// Compression of the entries. <see cref="System.IO.Compression.CompressionLevel.NoCompression"/> stores them,
    /// e.g. when the transport (HTTP) compresses anyway.
    /// </summary>
    public System.IO.Compression.CompressionLevel Compression { get; set; } = System.IO.Compression.CompressionLevel.Optimal;

    /// <summary>Indented manifest (readable by a human).</summary>
    public bool IndentManifest { get; set; } = true;
}
