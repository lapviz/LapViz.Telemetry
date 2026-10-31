using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>
/// A session in the LapViz format, version 3 or 2 (<c>docs/lapviz-format.md</c>): a ZIP archive holding the manifest
/// <c>lapviz.json</c> and the samples of each group, in columns (binary) or as CSV.
/// </summary>
/// <remarks>
/// The samples are kept in groups of channels that share a time base (<see cref="Groups"/>): the time in microseconds since
/// <see cref="LapVizSessionInfo.Start"/> and one array of values per channel, NaN for a missing value. <see cref="Time"/>,
/// <see cref="Channels"/>, <see cref="SetTime"/> and <see cref="AddChannel"/> are those of the first group, which is the
/// whole file in version 2. A file is written in version 2 when it fits (one group, float channels only), so that the
/// readers of version 2 read it; else in version 3. Use <see cref="ToDeviceSessionData"/> and
/// <see cref="FromDeviceSessionData"/> to go to and from the readers and writers of the other formats.
/// </remarks>
public sealed partial class LapVizFile
{
    /// <summary>Media type of a LapViz file.</summary>
    public const string MediaType = "application/vnd.lapviz.telemetry+zip";

    /// <summary>Latest major version of the specification, read and written by this class.</summary>
    public const int FormatVersion = 3;

    /// <summary>Oldest major version of the specification read and written by this class (one group, float channels).</summary>
    public const int MinimumFormatVersion = 2;

    /// <summary>Name of the manifest entry.</summary>
    public const string ManifestEntry = "lapviz.json";

    /// <summary>Name of the binary samples entry of version 2.</summary>
    public const string BinarySamplesEntry = "samples.bin";

    /// <summary>Name of the CSV samples entry of version 2.</summary>
    public const string CsvSamplesEntry = "samples.csv";

    /// <summary>Name of the first group when nothing names it (the samples of a version 2 file).</summary>
    public const string DefaultGroupName = "main";

    private const string FormatName = "lapviz";

    private readonly List<LapVizSampleGroup> _groups = new List<LapVizSampleGroup>();

    /// <summary>Creates an empty session starting at <paramref name="start"/>.</summary>
    public LapVizFile(DateTimeOffset start)
    {
        Session.Start = start;
        _groups.Add(new LapVizSampleGroup(this, DefaultGroupName));
    }

    /// <summary>Software that wrote the file.</summary>
    public LapVizGenerator? Generator { get; set; }

    /// <summary>Description of the session.</summary>
    public LapVizSessionInfo Session { get; set; } = new LapVizSessionInfo();

    /// <summary>
    /// The groups of samples, each with its own time base; never empty. The first one SHOULD hold the positions: it is the
    /// time base of <see cref="ToDeviceSessionData"/>.
    /// </summary>
    public IReadOnlyList<LapVizSampleGroup> Groups => _groups;

    /// <summary>The first group (<see cref="Groups"/>).</summary>
    public LapVizSampleGroup MainGroup => _groups[0];

    /// <summary>The channels of the first group, in the order of the columns.</summary>
    public IReadOnlyList<LapVizChannel> Channels => MainGroup.Channels;

    /// <summary>The channels of every group, the first group first.</summary>
    public IEnumerable<LapVizChannel> AllChannels => _groups.SelectMany(x => x.Channels);

    /// <summary>Microseconds since <see cref="LapVizSessionInfo.Start"/> of the samples of the first group, strictly increasing.</summary>
    public long[] Time => MainGroup.Time;

    /// <summary>Timing sources and their laps.</summary>
    public IList<LapVizTimingSource> Timing { get; set; } = new List<LapVizTimingSource>();

    /// <summary>Instants of the session: markers, beacons, flags... ordered by time.</summary>
    public IList<LapVizEvent> Events { get; set; } = new List<LapVizEvent>();

    /// <summary>Number of samples of the first group.</summary>
    public int Count => MainGroup.Count;

    /// <summary>The major version <see cref="Write(Stream, LapVizWriteOptions)"/> writes: 2 when the file fits, else 3.</summary>
    public int WrittenVersion => _groups.Count(x => !x.IsEmpty) > 1 || AllChannels.Any(x => x.IsInteger) ? 3 : 2;

    /// <summary>Sets the time of the samples of the first group (see <see cref="LapVizSampleGroup.SetTime"/>).</summary>
    public void SetTime(long[] microseconds) => MainGroup.SetTime(microseconds);

    /// <summary>Adds a channel to the first group; <paramref name="values"/> has one value per sample (NaN: missing).</summary>
    public void AddChannel(LapVizChannel channel, double[] values) => MainGroup.AddChannel(channel, values);

    /// <summary>Adds a group of samples with its own time base, e.g. "engine" at 50 Hz beside "gps" at 10 Hz.</summary>
    public LapVizSampleGroup AddGroup(string name)
    {
        if (_groups.Any(x => x.Name == name))
            throw new ArgumentException("Duplicate group: " + name, nameof(name));

        var group = new LapVizSampleGroup(this, name);
        _groups.Add(group);
        return group;
    }

    /// <summary>Values of the channel <paramref name="name"/> of the first group (case insensitive); false when it does not have it.</summary>
    public bool TryGetValues(string name, out double[] values) => MainGroup.TryGetValues(name, out values);

    /// <summary>Values of the channel at <paramref name="index"/> in <see cref="Channels"/>.</summary>
    public double[] Values(int index) => MainGroup.Values(index);

    /// <summary>The group holding the channel <paramref name="name"/> (case insensitive), or null.</summary>
    public LapVizSampleGroup? FindChannel(string name) => _groups.FirstOrDefault(x => x.IndexOf(name) >= 0);

    /// <summary>Instant of the sample <paramref name="index"/> of the first group.</summary>
    public DateTimeOffset TimestampAt(int index) => Session.Start.AddTicks(Time[index] * 10);

    /// <summary>
    /// The timing source to use by default: the first one measured by the logger that has laps, else the first measured one
    /// (not declared) with laps, else the first one with laps, else null.
    /// </summary>
    public LapVizTimingSource? PreferredTiming =>
        Timing.FirstOrDefault(x => x.IsDevice && x.Laps.Any(l => l.IsComplete)) ??
        Timing.FirstOrDefault(x => !x.IsDeclared && x.Laps.Any(l => l.IsComplete)) ??
        Timing.FirstOrDefault(x => x.Laps.Any(l => l.IsComplete));

    /// <summary>True when the stream is a LapViz version 2 or later archive (a ZIP holding <c>lapviz.json</c>).</summary>
    public static bool IsLapVizArchive(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        try
        {
            using (var zip = new ZipArchive(Seekable(stream), ZipArchiveMode.Read, leaveOpen: true))
            {
                return zip.GetEntry(ManifestEntry) != null;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>True when the file is a LapViz version 2 or later archive.</summary>
    public static bool IsLapVizArchive(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            return IsLapVizArchive(stream);
        }
    }

    /// <summary>Reads a LapViz archive, version 2 or 3.</summary>
    /// <exception cref="InvalidDataException">Not a LapViz file of a supported version, or an invalid one.</exception>
    public static LapVizFile Read(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using (var zip = new ZipArchive(Seekable(stream), ZipArchiveMode.Read, leaveOpen: true))
        {
            var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("Not a LapViz file: no " + ManifestEntry);
            byte[] manifest = ZipLimits.ReadAll(manifestEntry, MaxManifestBytes);
            var file = ReadManifest(manifest, out var samples);

            for (int g = 0; g < file._groups.Count; g++)
            {
                var group = file._groups[g];
                var description = samples[g];
                if (description == null)
                {
                    group.SetEmptyColumns(); // Channels without samples
                    continue;
                }

                var entry = zip.GetEntry(description.File) ?? throw new InvalidDataException("Missing samples entry: " + description.File);
                // Binary samples have an exact size: nothing more is decompressed (ZipLimits for the CSV ones)
                var data = ZipLimits.ReadAll(entry, description.Encoding == LapVizSampleEncoding.Binary ? group.BinaryLength(description.Count) : (long?)null);
                if (description.Sha256 != null && !string.Equals(description.Sha256, Sha256(data), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(description.File + " is damaged: its SHA-256 is not the one of the manifest");

                if (description.Encoding == LapVizSampleEncoding.Binary)
                    group.ReadBinary(data, description.Count, description.ByteShuffle);
                else
                    group.ReadCsv(data);
            }

            return file;
        }
    }

    /// <summary>Reads a LapViz file, version 2 or 3.</summary>
    public static LapVizFile Read(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            return Read(stream);
        }
    }

    /// <summary>Writes the session as a LapViz archive: version 2 when it fits, else version 3 (<see cref="WrittenVersion"/>).</summary>
    /// <exception cref="InvalidOperationException">A value does not fit the integer type of its channel.</exception>
    public void Write(Stream stream, LapVizWriteOptions? options = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        options = options ?? new LapVizWriteOptions();
        var version = WrittenVersion;
        var extension = options.Encoding == LapVizSampleEncoding.Binary ? ".bin" : ".csv";

        // The samples are encoded first: the manifest, written before them, holds their SHA-256
        var written = new List<WrittenGroup>();
        foreach (var group in _groups.Where(x => !x.IsEmpty))
        {
            using (var buffer = new MemoryStream())
            {
                if (options.Encoding == LapVizSampleEncoding.Binary)
                    group.WriteBinary(buffer, options.ByteShuffle);
                else
                    group.WriteCsv(buffer);

                var data = buffer.ToArray();
                var entry = version == 2 ? "samples" + extension : "samples/" + group.Name + extension;
                written.Add(new WrittenGroup(group, entry, data, options.Checksum ? Sha256(data) : null));
            }
        }

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifest = zip.CreateEntry(ManifestEntry, options.Compression).Open())
            {
                WriteManifest(manifest, options, version, written);
            }

            foreach (var group in written)
            {
                using (var entry = zip.CreateEntry(group.Entry, options.Compression).Open())
                {
                    entry.Write(group.Data, 0, group.Data.Length);
                }
            }
        }
    }

    /// <summary>Writes the session to a file (replaced when it exists).</summary>
    public void Write(string path, LapVizWriteOptions? options = null)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Write(stream, options);
        }
    }

    /// <summary>The archive in memory.</summary>
    public byte[] ToBytes(LapVizWriteOptions? options = null)
    {
        using (var stream = new MemoryStream())
        {
            Write(stream, options);
            return stream.ToArray();
        }
    }

    /// <summary>Name of a value type in the manifest.</summary>
    internal static string TypeName(LapVizValueType type) => type.ToString().ToLowerInvariant();

    private static Stream Seekable(Stream stream)
    {
        if (stream.CanSeek)
            return stream;

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    /// <summary>SHA-256 of <paramref name="data"/>, lowercase hexadecimal.</summary>
    private static string Sha256(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(data);
            var text = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                text.Append(b.ToString("x2", CultureInfo.InvariantCulture));

            return text.ToString();
        }
    }

    /// <summary>Largest manifest read: it holds the metadata and the laps, never the samples.</summary>
    private const long MaxManifestBytes = 64L * 1024 * 1024;

    // Manifest (section 2 of the specification)
    private void WriteManifest(Stream stream, LapVizWriteOptions options, int version, List<WrittenGroup> groups)
    {
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = options.IndentManifest }))
        {
            json.WriteStartObject();
            json.WriteString("format", FormatName);
            json.WriteNumber("version", version);

            if (Generator != null && (Generator.Name != null || Generator.Version != null))
            {
                json.WriteStartObject("generator");
                WriteOptional(json, "name", Generator.Name);
                WriteOptional(json, "version", Generator.Version);
                json.WriteEndObject();
            }

            json.WriteStartObject("session");
            json.WriteString("start", Session.Start.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));
            WriteOptional(json, "clock", Session.Clock);
            WriteOptional(json, "driver", Session.Driver);
            WriteOptional(json, "vehicle", Session.Vehicle);
            if (Session.Device != null)
            {
                json.WriteStartObject("device");
                WriteOptional(json, "brand", Session.Device.Brand);
                WriteOptional(json, "model", Session.Device.Model);
                WriteOptional(json, "serial", Session.Device.Serial);
                WriteOptional(json, "firmware", Session.Device.Firmware);
                json.WriteEndObject();
            }

            WriteCircuit(json, Session.Circuit);
            WriteOptional(json, "description", Session.Description);
            WriteOptional(json, "sourceFile", Session.SourceFile);
            WriteSetup(json, Session.Setup);
            WriteOptional(json, "notes", Session.Notes);
            if (Session.Properties != null && Session.Properties.Count > 0)
            {
                json.WriteStartObject("properties");
                foreach (var property in Session.Properties)
                    json.WriteString(property.Key, property.Value);

                json.WriteEndObject();
            }

            json.WriteEndObject();

            if (version == 2)
            {
                // One group: its channels and samples at the root
                var group = groups.FirstOrDefault();
                WriteChannels(json, group?.Group.Channels ?? MainGroup.Channels, version);
                if (group != null)
                    WriteSamples(json, options, group);
            }
            else
            {
                json.WriteStartArray("groups");
                foreach (var group in groups)
                {
                    json.WriteStartObject();
                    json.WriteString("name", group.Group.Name);
                    WriteChannels(json, group.Group.Channels, version);
                    WriteSamples(json, options, group);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            if (Timing.Count > 0)
            {
                json.WriteStartArray("timing");
                foreach (var source in Timing)
                {
                    json.WriteStartObject();
                    json.WriteString("source", source.Source ?? string.Empty);
                    if (source.IsDevice)
                        json.WriteBoolean("device", true);
                    if (source.IsDeclared)
                        json.WriteBoolean("declared", true);

                    WriteCircuit(json, source.Circuit);
                    json.WriteStartArray("laps");
                    foreach (var lap in source.Laps)
                    {
                        json.WriteStartObject();
                        json.WriteNumber("number", lap.Number);
                        json.WriteNumber("start", Math.Round(lap.Start, 6));
                        if (lap.Duration.HasValue)
                            json.WriteNumber("duration", Math.Round(lap.Duration.Value, 6));

                        if (lap.Sectors != null && lap.Sectors.Count > 0)
                        {
                            json.WriteStartArray("sectors");
                            foreach (var sector in lap.Sectors)
                                json.WriteNumberValue(Math.Round(sector, 6));

                            json.WriteEndArray();
                        }

                        if (lap.Flags != null && lap.Flags.Any(x => !string.IsNullOrEmpty(x)))
                        {
                            json.WriteStartArray("flags");
                            foreach (var flag in lap.Flags.Where(x => !string.IsNullOrEmpty(x)).Distinct())
                                json.WriteStringValue(flag);

                            json.WriteEndArray();
                        }

                        WriteOptional(json, "driver", lap.Driver);
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            if (Events != null && Events.Count > 0)
            {
                json.WriteStartArray("events");
                foreach (var item in Events.Where(x => !string.IsNullOrEmpty(x.Type)).OrderBy(x => x.Time))
                {
                    json.WriteStartObject();
                    json.WriteNumber("time", Math.Round(item.Time, 6));
                    json.WriteString("type", item.Type);
                    WriteOptional(json, "label", item.Label);
                    WriteOptional(json, "source", item.Source);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }
    }

    private static void WriteChannels(Utf8JsonWriter json, IEnumerable<LapVizChannel> channels, int version)
    {
        json.WriteStartArray("channels");
        foreach (var channel in channels)
        {
            json.WriteStartObject();
            json.WriteString("name", channel.Name);
            WriteOptional(json, "unit", channel.Unit);
            json.WriteString("type", TypeName(channel.Type));
            if (version >= 3 && channel.IsInteger)
            {
                if (channel.Scale != 1)
                    json.WriteNumber("scale", channel.Scale);
                if (channel.Offset != 0)
                    json.WriteNumber("offset", channel.Offset);
            }

            WriteOptional(json, "description", channel.Description);
            if (channel.Rate > 0)
                WriteOptional(json, "rate", channel.Rate);

            WriteOptional(json, "origin", channel.Origin);
            if (channel.Interpolation == LapVizInterpolation.Step)
                json.WriteString("interpolation", "step");

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteSamples(Utf8JsonWriter json, LapVizWriteOptions options, WrittenGroup group)
    {
        json.WriteStartObject("samples");
        json.WriteString("file", group.Entry);
        json.WriteString("encoding", options.Encoding == LapVizSampleEncoding.Binary ? "binary" : "csv");
        json.WriteNumber("count", group.Group.Count);
        if (options.Encoding == LapVizSampleEncoding.Binary)
            json.WriteBoolean("byteShuffle", options.ByteShuffle);

        WriteOptional(json, "sha256", group.Sha256);
        json.WriteEndObject();
    }

    private static void WriteCircuit(Utf8JsonWriter json, LapVizCircuit? circuit)
    {
        if (circuit == null || circuit.IsEmpty)
            return;

        json.WriteStartObject("circuit");
        WriteOptional(json, "code", circuit.Code);
        WriteOptional(json, "name", circuit.Name);
        WriteOptional(json, "countryCode", circuit.CountryCode);
        if (circuit.Type.HasValue)
            json.WriteString("type", circuit.Type == CircuitType.Open ? "open" : "closed");

        if (circuit.Lines != null && circuit.Lines.Count > 0)
        {
            json.WriteStartArray("lines");
            foreach (var line in circuit.Lines)
            {
                json.WriteStartArray();
                foreach (var point in new[] { line.Start, line.End })
                {
                    json.WriteStartObject();
                    json.WriteNumber("lat", point.Latitude);
                    json.WriteNumber("lon", point.Longitude);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteSetup(Utf8JsonWriter json, VehicleSetup? setup)
    {
        if (setup == null || setup.IsEmpty)
            return;

        json.WriteStartObject("setup");
        WriteOptional(json, "tyres", setup.Tyres);
        WriteOptional(json, "tyreCondition", setup.TyreCondition?.ToString().ToLowerInvariant());
        WriteOptional(json, "tyreLaps", setup.TyreLaps);
        WriteOptional(json, "pressureFront", setup.PressureFront);
        WriteOptional(json, "pressureRear", setup.PressureRear);
        WriteOptional(json, "gearingFront", setup.GearingFront);
        WriteOptional(json, "gearingRear", setup.GearingRear);
        WriteOptional(json, "carburation", setup.Carburation);
        WriteOptional(json, "chassis", setup.Chassis);
        WriteOptional(json, "feeling", setup.Feeling);
        json.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, double? value)
    {
        if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
            json.WriteNumber(name, value.Value);
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, int? value)
    {
        if (value.HasValue)
            json.WriteNumber(name, value.Value);
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            json.WriteString(name, value);
    }

    private static LapVizFile ReadManifest(byte[] manifest, out List<SamplesDescription?> samples)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifest);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("lapviz.json is not valid JSON: " + ex.Message, ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || String(root, "format") != FormatName)
                throw new InvalidDataException("lapviz.json: format must be \"lapviz\"");

            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var major))
                throw new InvalidDataException("lapviz.json: missing version");
            if (major < MinimumFormatVersion || major > FormatVersion)
                throw new InvalidDataException("Unsupported LapViz format version " + major + " (this reader supports versions " + MinimumFormatVersion + " to " + FormatVersion + ")");

            if (!root.TryGetProperty("session", out var session) || session.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("lapviz.json: missing session");

            var startText = String(session, "start") ?? throw new InvalidDataException("lapviz.json: missing session.start");
            if (!DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
                throw new InvalidDataException("lapviz.json: invalid session.start " + startText);

            var file = new LapVizFile(start);
            file.Session.Clock = String(session, "clock");
            if (root.TryGetProperty("generator", out var generator) && generator.ValueKind == JsonValueKind.Object)
                file.Generator = new LapVizGenerator { Name = String(generator, "name"), Version = String(generator, "version") };

            file.Session.Driver = String(session, "driver");
            file.Session.Vehicle = String(session, "vehicle");
            file.Session.Description = String(session, "description");
            file.Session.SourceFile = String(session, "sourceFile");
            file.Session.Setup = Setup(session);
            file.Session.Notes = String(session, "notes");
            file.Session.Circuit = Circuit(session);
            if (session.TryGetProperty("device", out var device) && device.ValueKind == JsonValueKind.Object)
            {
                file.Session.Device = new LapVizDevice
                {
                    Brand = String(device, "brand"),
                    Model = String(device, "model"),
                    Serial = String(device, "serial"),
                    Firmware = String(device, "firmware"),
                };
            }

            if (session.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                        file.Session.Properties[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            samples = new List<SamplesDescription?>();
            if (major == 2)
            {
                // One group: channels and samples at the root
                ReadChannels(file.MainGroup, root, major);
                samples.Add(Samples(root));
            }
            else
            {
                if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
                    throw new InvalidDataException("lapviz.json: missing groups");

                foreach (var element in groups.EnumerateArray())
                {
                    var name = String(element, "name");
                    if (!LapVizSampleGroup.IsValidName(name))
                        throw new InvalidDataException("lapviz.json: invalid group name " + name);
                    if (file._groups.Skip(samples.Count == 0 ? 1 : 0).Any(x => x.Name == name))
                        throw new InvalidDataException("lapviz.json: duplicate group " + name);

                    LapVizSampleGroup group;
                    if (samples.Count == 0)
                    {
                        group = file.MainGroup;
                        group.Name = name!;
                    }
                    else
                    {
                        group = file.AddGroup(name!);
                    }

                    ReadChannels(group, element, major);
                    samples.Add(Samples(element));
                }
            }

            if (root.TryGetProperty("timing", out var timing) && timing.ValueKind == JsonValueKind.Array)
            {
                foreach (var source in timing.EnumerateArray())
                {
                    var timingSource = new LapVizTimingSource
                    {
                        Source = String(source, "source") ?? string.Empty,
                        IsDevice = source.TryGetProperty("device", out var isDevice) && isDevice.ValueKind == JsonValueKind.True,
                        IsDeclared = source.TryGetProperty("declared", out var isDeclared) && isDeclared.ValueKind == JsonValueKind.True,
                        Circuit = Circuit(source),
                    };

                    if (source.TryGetProperty("laps", out var laps) && laps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var lap in laps.EnumerateArray())
                        {
                            timingSource.Laps.Add(new LapVizLap
                            {
                                Number = lap.TryGetProperty("number", out var number) && number.TryGetInt32(out var lapNumber) ? lapNumber : throw new InvalidDataException("lapviz.json: a lap has no number"),
                                Start = Number(lap, "start") ?? throw new InvalidDataException("lapviz.json: lap " + lapNumber + " has no start"),
                                Duration = Number(lap, "duration"),
                                Sectors = lap.TryGetProperty("sectors", out var sectors) && sectors.ValueKind == JsonValueKind.Array
                                    ? sectors.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToList()
                                    : new List<double>(),
                                Flags = lap.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array
                                    ? flags.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                                    : new List<string>(),
                                Driver = String(lap, "driver"),
                            });
                        }
                    }

                    file.Timing.Add(timingSource);
                }
            }

            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                // An event without a time or a type is ignored, like an unknown member
                foreach (var item in events.EnumerateArray())
                {
                    var time = Number(item, "time");
                    var type = String(item, "type");
                    if (time.HasValue && !string.IsNullOrEmpty(type))
                        file.Events.Add(new LapVizEvent { Time = time.Value, Type = type!, Label = String(item, "label"), Source = String(item, "source") });
                }
            }

            return file;
        }
    }

    private static void ReadChannels(LapVizSampleGroup group, JsonElement parent, int version)
    {
        if (!parent.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("lapviz.json: missing channels");

        foreach (var channel in channels.EnumerateArray())
        {
            var name = String(channel, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("lapviz.json: a channel has no name");

            var typeName = String(channel, "type");
            var type = typeName == null ? LapVizValueType.Float64 : ValueType(typeName, version) ?? throw new InvalidDataException("lapviz.json: unknown type " + typeName + " of channel " + name);
            var scale = Number(channel, "scale") ?? 1;
            if (!(scale > 0) || double.IsInfinity(scale))
                throw new InvalidDataException("lapviz.json: the scale of channel " + name + " must be strictly positive");

            var integer = type != LapVizValueType.Float32 && type != LapVizValueType.Float64;
            group.DeclareChannel(new LapVizChannel
            {
                Name = name!,
                Unit = String(channel, "unit"),
                Type = type,
                Scale = integer ? scale : 1,
                Offset = integer ? Number(channel, "offset") ?? 0 : 0,
                Description = String(channel, "description"),
                Rate = Number(channel, "rate") is > 0 and var rate ? rate : null,
                Origin = String(channel, "origin"),
                Interpolation = String(channel, "interpolation") == "step" ? LapVizInterpolation.Step : LapVizInterpolation.Linear,
            });
        }
    }

    /// <summary>The type of a channel; the integer types are version 3.</summary>
    private static LapVizValueType? ValueType(string name, int version)
    {
        foreach (LapVizValueType type in Enum.GetValues(typeof(LapVizValueType)))
        {
            if (TypeName(type) == name)
                return type is LapVizValueType.Float32 or LapVizValueType.Float64 || version >= 3 ? type : null;
        }

        return null;
    }

    private static SamplesDescription? Samples(JsonElement parent)
    {
        if (!parent.TryGetProperty("samples", out var element) || element.ValueKind != JsonValueKind.Object)
            return null;

        var encoding = String(element, "encoding");
        var samples = new SamplesDescription
        {
            File = String(element, "file") ?? throw new InvalidDataException("lapviz.json: missing samples.file"),
            Encoding = encoding == "binary" ? LapVizSampleEncoding.Binary
                : encoding == "csv" ? LapVizSampleEncoding.Csv
                : throw new InvalidDataException("lapviz.json: unknown samples.encoding " + encoding),
            Count = element.TryGetProperty("count", out var count) && count.TryGetInt32(out var n) ? n : -1,
            ByteShuffle = element.TryGetProperty("byteShuffle", out var shuffle) && shuffle.ValueKind == JsonValueKind.True,
            Sha256 = String(element, "sha256"),
        };

        if (samples.Encoding == LapVizSampleEncoding.Binary && samples.Count < 0)
            throw new InvalidDataException("lapviz.json: binary samples need a count");

        return samples;
    }

    private static LapVizCircuit? Circuit(JsonElement parent)
    {
        if (!parent.TryGetProperty("circuit", out var circuit) || circuit.ValueKind != JsonValueKind.Object)
            return null;

        var type = String(circuit, "type");
        return new LapVizCircuit
        {
            Code = String(circuit, "code"),
            Name = String(circuit, "name"),
            CountryCode = String(circuit, "countryCode"),
            Type = type == "open" ? CircuitType.Open : type == "closed" ? CircuitType.Closed : null,
            Lines = Lines(circuit),
        };
    }

    /// <summary>The timing lines of a circuit; a line that is not two points of a valid latitude and longitude is ignored.</summary>
    private static List<CircuitGeoLine> Lines(JsonElement circuit)
    {
        var lines = new List<CircuitGeoLine>();
        if (!circuit.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array)
            return lines;

        foreach (var line in array.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Array || line.GetArrayLength() != 2)
                continue;

            var points = line.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.Object ? (Latitude: Number(x, "lat"), Longitude: Number(x, "lon")) : (null, null))
                .ToList();
            if (points.All(x => x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180))
            {
                lines.Add(new CircuitGeoLine(
                    new GeoCoordinates(points[0].Latitude!.Value, points[0].Longitude!.Value),
                    new GeoCoordinates(points[1].Latitude!.Value, points[1].Longitude!.Value)));
            }
        }

        return lines;
    }

    /// <summary>The setup of the session; members of the wrong type or out of their range are ignored.</summary>
    private static VehicleSetup? Setup(JsonElement session)
    {
        if (!session.TryGetProperty("setup", out var element) || element.ValueKind != JsonValueKind.Object)
            return null;

        var condition = String(element, "tyreCondition");
        var feeling = Integer(element, "feeling");
        var setup = new VehicleSetup
        {
            Tyres = String(element, "tyres"),
            TyreCondition = condition != null && Enum.TryParse<TyreCondition>(condition, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(typeof(TyreCondition), parsed) && !int.TryParse(condition, out _) ? parsed : null,
            TyreLaps = Integer(element, "tyreLaps") is >= 0 and var laps ? laps : null,
            PressureFront = Number(element, "pressureFront"),
            PressureRear = Number(element, "pressureRear"),
            GearingFront = Integer(element, "gearingFront"),
            GearingRear = Integer(element, "gearingRear"),
            Carburation = String(element, "carburation"),
            Chassis = String(element, "chassis"),
            Feeling = feeling is >= 1 and <= VehicleSetup.MaxFeeling ? feeling : null,
        };
        return setup.IsEmpty ? null : setup;
    }

    private static int? Integer(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) ? i : (int?)null;

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : (double?)null;

    /// <summary>A group as written: its archive entry, its encoded samples and their SHA-256.</summary>
    private sealed class WrittenGroup
    {
        public WrittenGroup(LapVizSampleGroup group, string entry, byte[] data, string? sha256)
        {
            Group = group;
            Entry = entry;
            Data = data;
            Sha256 = sha256;
        }

        public LapVizSampleGroup Group { get; }

        public string Entry { get; }

        public byte[] Data { get; }

        public string? Sha256 { get; }
    }

    private sealed class SamplesDescription
    {
        public string File { get; set; } = string.Empty;

        public LapVizSampleEncoding Encoding { get; set; }

        public int Count { get; set; }

        public bool ByteShuffle { get; set; }

        public string? Sha256 { get; set; }
    }
}
