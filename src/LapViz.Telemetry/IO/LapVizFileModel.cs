using System;
using System.Collections.Generic;
using LapViz.Telemetry.Domain;

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

/// <summary>How a reader goes from one value of a channel to the next.</summary>
public enum LapVizInterpolation
{
    /// <summary>A continuous quantity (speed, temperature): the values between two samples are interpolated. The default.</summary>
    Linear,

    /// <summary>A discrete quantity (gear, state): a value holds until the next one.</summary>
    Step,
}

/// <summary>Conventional values of <see cref="LapVizSessionInfo.Clock"/>.</summary>
public static class LapVizClock
{
    /// <summary>Time of the GPS satellites: exact to the millisecond.</summary>
    public const string Gps = "gps";

    /// <summary>Clock synchronized over a network (a phone): exact to the second.</summary>
    public const string Network = "network";

    /// <summary>Clock of the logger, set by hand or never: may be off by minutes, or by years.</summary>
    public const string Device = "device";

    /// <summary>Typed by a person (a session read from a results sheet).</summary>
    public const string Manual = "manual";
}

/// <summary>Conventional values of <see cref="LapVizLap.Flags"/>.</summary>
public static class LapVizLapFlags
{
    /// <summary>The lap started in the pit lane.</summary>
    public const string PitOut = "pitOut";

    /// <summary>The lap ended in the pit lane.</summary>
    public const string PitIn = "pitIn";

    /// <summary>The lap does not count (track limits, penalty): never a best lap.</summary>
    public const string Invalid = "invalid";

    /// <summary>The lap was driven under a yellow flag, a safety car or a red flag.</summary>
    public const string Neutralized = "neutralized";
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

    /// <summary>Closed (a loop: the first line starts and ends the lap) or open (the last line ends the lap); null: unknown.</summary>
    public CircuitType? Type { get; set; }

    /// <summary>
    /// The timing lines, in order: the start line (also the finish line of a closed circuit), then the sector lines, then
    /// the finish line of an open circuit. Empty when unknown.
    /// </summary>
    public IList<CircuitGeoLine> Lines { get; set; } = new List<CircuitGeoLine>();

    internal bool IsEmpty => Code == null && Name == null && CountryCode == null && Type == null && (Lines == null || Lines.Count == 0);
}

/// <summary>Description of the session.</summary>
public sealed class LapVizSessionInfo
{
    /// <summary>Time origin of the samples and of the laps.</summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>Clock <see cref="Start"/> was taken from (<see cref="LapVizClock"/>); null: unknown.</summary>
    public string? Clock { get; set; }

    public string? Driver { get; set; }

    public string? Vehicle { get; set; }

    public LapVizDevice? Device { get; set; }

    public LapVizCircuit? Circuit { get; set; }

    public string? Description { get; set; }

    /// <summary>Name of the file the session was converted from.</summary>
    public string? SourceFile { get; set; }

    /// <summary>Setup sheet of the vehicle and feeling of the driver, as known when the file was written.</summary>
    public VehicleSetup? Setup { get; set; }

    /// <summary>Free notes of the driver or the team (conditions, changes, feeling).</summary>
    public string? Notes { get; set; }

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

    /// <summary>Rate in Hz at which the logger measured the channel, which may differ from the rate of the samples; null: unknown.</summary>
    public double? Rate { get; set; }

    /// <summary>What measured the channel: "gps", "imu", "ecu", "obd", "analog", "computed"...; null: unknown.</summary>
    public string? Origin { get; set; }

    /// <summary>How to read the values between two samples.</summary>
    public LapVizInterpolation Interpolation { get; set; }

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

    /// <summary>Status of the lap (<see cref="LapVizLapFlags"/>; unknown values are kept).</summary>
    public IList<string> Flags { get; set; } = new List<string>();

    /// <summary>Driver of the lap when it is not the driver of the session (a change of driver in an endurance race).</summary>
    public string? Driver { get; set; }

    /// <summary>The lap may be a best lap: it is complete and not <see cref="LapVizLapFlags.Invalid"/>.</summary>
    public bool IsValid => IsComplete && !HasFlag(LapVizLapFlags.Invalid);

    /// <summary>The lap has the flag <paramref name="flag"/> (case sensitive, as in the specification).</summary>
    public bool HasFlag(string flag) => Flags != null && Flags.Contains(flag);

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

    /// <summary>The laps were not measured but typed in or read from a document (a results sheet).</summary>
    public bool IsDeclared { get; set; }

    public LapVizCircuit? Circuit { get; set; }

    /// <summary>The laps, ordered by start.</summary>
    public IList<LapVizLap> Laps { get; set; } = new List<LapVizLap>();
}

/// <summary>Something that happened at an instant of the session: a marker of the driver, a beacon, a flag...</summary>
public sealed class LapVizEvent
{
    /// <summary>Seconds from the start of the session.</summary>
    public double Time { get; set; }

    /// <summary>Type: "marker", "beacon", "pitIn", "pitOut", "flag"... (unknown types are kept).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Free text: the colour of a flag, the name of a marker.</summary>
    public string? Label { get; set; }

    /// <summary>What recorded the event (the name of a timing source, "driver"...).</summary>
    public string? Source { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Type + " at " + Time.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s" + (Label == null ? string.Empty : ": " + Label);
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

    /// <summary>Writes the SHA-256 of the samples in the manifest, so that a reader detects a damaged file.</summary>
    public bool Checksum { get; set; } = true;
}
