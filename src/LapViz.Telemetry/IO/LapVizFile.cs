using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LapViz.Telemetry.IO;

/// <summary>
/// A session in the LapViz format, version 2 (<c>docs/lapviz-format.md</c>): a ZIP archive holding the manifest
/// <c>lapviz.json</c> and the samples, in columns (<c>samples.bin</c>) or as CSV (<c>samples.csv</c>).
/// </summary>
/// <remarks>
/// The samples are kept in columns: <see cref="Time"/> (microseconds since <see cref="LapVizSessionInfo.Start"/>) and one
/// array of values per channel, NaN for a missing value. Use <see cref="ToDeviceSessionData"/> and
/// <see cref="FromDeviceSessionData"/> to go to and from the readers and writers of the other formats.
/// </remarks>
public sealed partial class LapVizFile
{
    /// <summary>Media type of a LapViz file.</summary>
    public const string MediaType = "application/vnd.lapviz.telemetry+zip";

    /// <summary>Major version of the specification written and read by this class.</summary>
    public const int FormatVersion = 2;

    /// <summary>Name of the manifest entry.</summary>
    public const string ManifestEntry = "lapviz.json";

    /// <summary>Name of the binary samples entry.</summary>
    public const string BinarySamplesEntry = "samples.bin";

    /// <summary>Name of the CSV samples entry.</summary>
    public const string CsvSamplesEntry = "samples.csv";

    private const string FormatName = "lapviz";
    private const double MicrosecondsPerSecond = 1_000_000;

    private readonly List<LapVizChannel> _channels = new List<LapVizChannel>();
    private readonly List<double[]> _values = new List<double[]>();

    /// <summary>Creates an empty session starting at <paramref name="start"/>.</summary>
    public LapVizFile(DateTimeOffset start)
    {
        Session.Start = start;
    }

    /// <summary>Software that wrote the file.</summary>
    public LapVizGenerator? Generator { get; set; }

    /// <summary>Description of the session.</summary>
    public LapVizSessionInfo Session { get; set; } = new LapVizSessionInfo();

    /// <summary>The channels, in the order of the columns.</summary>
    public IReadOnlyList<LapVizChannel> Channels => _channels;

    /// <summary>Microseconds since <see cref="LapVizSessionInfo.Start"/>, strictly increasing.</summary>
    public long[] Time { get; private set; } = Array.Empty<long>();

    /// <summary>Timing sources and their laps.</summary>
    public IList<LapVizTimingSource> Timing { get; set; } = new List<LapVizTimingSource>();

    /// <summary>Number of samples.</summary>
    public int Count => Time.Length;

    /// <summary>
    /// Sets the time of the samples (microseconds since the start, strictly increasing). The channels are removed when the
    /// number of samples changes.
    /// </summary>
    public void SetTime(long[] microseconds)
    {
        if (microseconds == null)
            throw new ArgumentNullException(nameof(microseconds));

        for (int i = 1; i < microseconds.Length; i++)
        {
            if (microseconds[i] <= microseconds[i - 1])
                throw new ArgumentException("The time must be strictly increasing (sample " + i + ")", nameof(microseconds));
        }

        if (microseconds.Length != Time.Length)
        {
            _channels.Clear();
            _values.Clear();
        }

        Time = microseconds;
    }

    /// <summary>Adds a channel; <paramref name="values"/> has one value per sample (NaN: missing).</summary>
    public void AddChannel(LapVizChannel channel, double[] values)
    {
        if (channel == null)
            throw new ArgumentNullException(nameof(channel));
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        if (string.IsNullOrWhiteSpace(channel.Name))
            throw new ArgumentException("A channel needs a name", nameof(channel));
        if (values.Length != Count)
            throw new ArgumentException("The channel has " + values.Length + " values for " + Count + " samples", nameof(values));
        if (IndexOf(channel.Name) >= 0)
            throw new ArgumentException("Duplicate channel: " + channel.Name, nameof(channel));

        _channels.Add(channel);
        _values.Add(values);
    }

    /// <summary>Values of the channel <paramref name="name"/> (case insensitive); false when the file does not have it.</summary>
    public bool TryGetValues(string name, out double[] values)
    {
        var index = IndexOf(name);
        values = index < 0 ? Array.Empty<double>() : _values[index];
        return index >= 0;
    }

    /// <summary>Values of the channel at <paramref name="index"/> in <see cref="Channels"/>.</summary>
    public double[] Values(int index) => _values[index];

    /// <summary>Instant of the sample <paramref name="index"/>.</summary>
    public DateTimeOffset TimestampAt(int index) => Session.Start.AddTicks(Time[index] * 10);

    /// <summary>
    /// The timing source to use by default: the first one measured by the logger that has laps, else the first one with
    /// laps, else null.
    /// </summary>
    public LapVizTimingSource? PreferredTiming =>
        Timing.FirstOrDefault(x => x.IsDevice && x.Laps.Any(l => l.IsComplete)) ?? Timing.FirstOrDefault(x => x.Laps.Any(l => l.IsComplete));

    /// <summary>True when the stream is a LapViz version 2 archive (a ZIP holding <c>lapviz.json</c>).</summary>
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

    /// <summary>True when the file is a LapViz version 2 archive.</summary>
    public static bool IsLapVizArchive(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            return IsLapVizArchive(stream);
        }
    }

    /// <summary>Reads a LapViz version 2 archive.</summary>
    /// <exception cref="InvalidDataException">Not a LapViz version 2 file, or an invalid one.</exception>
    public static LapVizFile Read(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using (var zip = new ZipArchive(Seekable(stream), ZipArchiveMode.Read, leaveOpen: true))
        {
            var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("Not a LapViz file: no " + ManifestEntry);
            byte[] manifest = ReadAll(manifestEntry);
            var file = ReadManifest(manifest, out var samples);

            if (samples != null)
            {
                var entry = zip.GetEntry(samples.File) ?? throw new InvalidDataException("Missing samples entry: " + samples.File);
                var data = ReadAll(entry);
                if (samples.Encoding == LapVizSampleEncoding.Binary)
                    file.ReadBinarySamples(data, samples.Count, samples.ByteShuffle);
                else
                    file.ReadCsvSamples(data);
            }
            else if (file._channels.Count > 0)
            {
                // Channels without samples: empty columns
                file._values.AddRange(file._channels.Select(_ => Array.Empty<double>()));
            }

            return file;
        }
    }

    /// <summary>Reads a LapViz version 2 file.</summary>
    public static LapVizFile Read(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            return Read(stream);
        }
    }

    /// <summary>Writes the session as a LapViz version 2 archive.</summary>
    public void Write(Stream stream, LapVizWriteOptions? options = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        options = options ?? new LapVizWriteOptions();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var hasSamples = Count > 0 || _channels.Count > 0;
            var samplesEntry = options.Encoding == LapVizSampleEncoding.Binary ? BinarySamplesEntry : CsvSamplesEntry;

            using (var manifest = zip.CreateEntry(ManifestEntry, options.Compression).Open())
            {
                WriteManifest(manifest, options, hasSamples ? samplesEntry : null);
            }

            if (hasSamples)
            {
                using (var samples = zip.CreateEntry(samplesEntry, options.Compression).Open())
                {
                    if (options.Encoding == LapVizSampleEncoding.Binary)
                        WriteBinarySamples(samples, options.ByteShuffle);
                    else
                        WriteCsvSamples(samples);
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

    private int IndexOf(string name)
    {
        for (int i = 0; i < _channels.Count; i++)
        {
            if (string.Equals(_channels[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static Stream Seekable(Stream stream)
    {
        if (stream.CanSeek)
            return stream;

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using (var input = entry.Open())
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    // Binary samples (section 3 of the specification)
    private void WriteBinarySamples(Stream stream, bool shuffle)
    {
        WriteColumn(stream, MemoryMarshal.AsBytes(Time.AsSpan()).ToArray(), sizeof(long), shuffle);
        for (int k = 0; k < _channels.Count; k++)
        {
            var values = _values[k];
            byte[] bytes;
            if (_channels[k].Type == LapVizValueType.Float32)
            {
                var floats = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                    floats[i] = (float)values[i];

                bytes = MemoryMarshal.AsBytes(floats.AsSpan()).ToArray();
                WriteColumn(stream, bytes, sizeof(float), shuffle);
            }
            else
            {
                bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
                WriteColumn(stream, bytes, sizeof(double), shuffle);
            }
        }
    }

    private void ReadBinarySamples(byte[] data, int count, bool shuffle)
    {
        if (count < 0)
            throw new InvalidDataException("Negative sample count");

        long expected = (long)count * (sizeof(long) + _channels.Sum(x => x.Type == LapVizValueType.Float32 ? sizeof(float) : sizeof(double)));
        if (data.Length != expected)
            throw new InvalidDataException("samples.bin has " + data.Length + " bytes, " + expected + " expected for " + count + " samples");

        int offset = 0;
        var time = MemoryMarshal.Cast<byte, long>(ReadColumn(data, ref offset, count, sizeof(long), shuffle)).ToArray();
        var values = new List<double[]>(_channels.Count);
        foreach (var channel in _channels)
        {
            if (channel.Type == LapVizValueType.Float32)
            {
                var floats = MemoryMarshal.Cast<byte, float>(ReadColumn(data, ref offset, count, sizeof(float), shuffle));
                var column = new double[count];
                for (int i = 0; i < count; i++)
                    column[i] = floats[i];

                values.Add(column);
            }
            else
            {
                values.Add(MemoryMarshal.Cast<byte, double>(ReadColumn(data, ref offset, count, sizeof(double), shuffle)).ToArray());
            }
        }

        SetSamples(time, values);
    }

    /// <summary>Writes a column of little endian values, transposed when <paramref name="shuffle"/>.</summary>
    private static void WriteColumn(Stream stream, byte[] bytes, int size, bool shuffle)
    {
        EnsureLittleEndian(bytes, size);
        if (!shuffle)
        {
            stream.Write(bytes, 0, bytes.Length);
            return;
        }

        int count = bytes.Length / size;
        var shuffled = new byte[bytes.Length];
        for (int i = 0; i < count; i++)
        {
            for (int b = 0; b < size; b++)
                shuffled[(b * count) + i] = bytes[(i * size) + b];
        }

        stream.Write(shuffled, 0, shuffled.Length);
    }

    private static byte[] ReadColumn(byte[] data, ref int offset, int count, int size, bool shuffle)
    {
        var bytes = new byte[count * size];
        if (shuffle)
        {
            for (int i = 0; i < count; i++)
            {
                for (int b = 0; b < size; b++)
                    bytes[(i * size) + b] = data[offset + (b * count) + i];
            }
        }
        else
        {
            Buffer.BlockCopy(data, offset, bytes, 0, bytes.Length);
        }

        offset += bytes.Length;
        EnsureLittleEndian(bytes, size);
        return bytes;
    }

    /// <summary>The format is little endian: reverses the bytes of each value on a big endian machine.</summary>
    private static void EnsureLittleEndian(byte[] bytes, int size)
    {
        if (BitConverter.IsLittleEndian)
            return;

        for (int i = 0; i < bytes.Length; i += size)
            Array.Reverse(bytes, i, size);
    }

    // CSV samples (section 4 of the specification)
    private void WriteCsvSamples(Stream stream)
    {
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true))
        {
            writer.NewLine = "\n";
            writer.Write("time");
            foreach (var channel in _channels)
            {
                writer.Write(',');
                writer.Write(CsvField(channel.Name));
            }

            writer.WriteLine();
            var line = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                line.Clear();
                line.Append(FormatSeconds(Time[i]));
                for (int k = 0; k < _channels.Count; k++)
                {
                    line.Append(',');
                    var value = _values[k][i];
                    if (!double.IsNaN(value))
                        line.Append(FormatValue(value, _channels[k].Type));
                }

                writer.WriteLine(line.ToString());
            }
        }
    }

    private void ReadCsvSamples(byte[] data)
    {
        using (var reader = new StreamReader(new MemoryStream(data), new UTF8Encoding(false), true))
        {
            var header = reader.ReadLine() ?? throw new InvalidDataException("samples.csv is empty");
            var names = SplitCsv(header);
            if (names.Count == 0 || !string.Equals(names[0], "time", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("samples.csv must start with the time column");
            if (names.Count - 1 != _channels.Count || names.Skip(1).Where((name, k) => !string.Equals(name, _channels[k].Name, StringComparison.Ordinal)).Any())
                throw new InvalidDataException("The columns of samples.csv do not match the channels of the manifest");

            var time = new List<long>();
            var columns = _channels.Select(_ => new List<double>()).ToList();
            string? line;
            int lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length == 0)
                    continue;

                var fields = SplitCsv(line);
                if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    throw new InvalidDataException("samples.csv line " + lineNumber + ": invalid time");

                time.Add((long)Math.Round(seconds * MicrosecondsPerSecond));
                for (int k = 0; k < columns.Count; k++)
                {
                    var field = k + 1 < fields.Count ? fields[k + 1] : string.Empty;
                    columns[k].Add(field.Length == 0
                        ? double.NaN
                        : double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException("samples.csv line " + lineNumber + ": invalid value for " + _channels[k].Name));
                }
            }

            SetSamples(time.ToArray(), columns.Select(x => x.ToArray()).ToList());
        }
    }

    private void SetSamples(long[] time, List<double[]> values)
    {
        for (int i = 1; i < time.Length; i++)
        {
            if (time[i] <= time[i - 1])
                throw new InvalidDataException("The time of the samples must be strictly increasing (sample " + i + ")");
        }

        Time = time;
        _values.Clear();
        _values.AddRange(values);
    }

    private static string FormatSeconds(long microseconds)
    {
        var sign = microseconds < 0 ? "-" : string.Empty;
        var absolute = Math.Abs(microseconds);
        var text = (absolute / 1_000_000).ToString(CultureInfo.InvariantCulture);
        var fraction = absolute % 1_000_000;
        return fraction == 0 ? sign + text : sign + text + "." + fraction.ToString("000000", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    private static string FormatValue(double value, LapVizValueType type) =>
        type == LapVizValueType.Float32
            ? ((float)value).ToString("R", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    private static string CsvField(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    // Manifest (section 2 of the specification)
    private void WriteManifest(Stream stream, LapVizWriteOptions options, string? samplesEntry)
    {
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = options.IndentManifest }))
        {
            json.WriteStartObject();
            json.WriteString("format", FormatName);
            json.WriteNumber("version", FormatVersion);

            if (Generator != null && (Generator.Name != null || Generator.Version != null))
            {
                json.WriteStartObject("generator");
                WriteOptional(json, "name", Generator.Name);
                WriteOptional(json, "version", Generator.Version);
                json.WriteEndObject();
            }

            json.WriteStartObject("session");
            json.WriteString("start", Session.Start.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));
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
            if (Session.Properties != null && Session.Properties.Count > 0)
            {
                json.WriteStartObject("properties");
                foreach (var property in Session.Properties)
                    json.WriteString(property.Key, property.Value);

                json.WriteEndObject();
            }

            json.WriteEndObject();

            json.WriteStartArray("channels");
            foreach (var channel in _channels)
            {
                json.WriteStartObject();
                json.WriteString("name", channel.Name);
                WriteOptional(json, "unit", channel.Unit);
                json.WriteString("type", channel.Type == LapVizValueType.Float32 ? "float32" : "float64");
                WriteOptional(json, "description", channel.Description);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            if (samplesEntry != null)
            {
                json.WriteStartObject("samples");
                json.WriteString("file", samplesEntry);
                json.WriteString("encoding", options.Encoding == LapVizSampleEncoding.Binary ? "binary" : "csv");
                json.WriteNumber("count", Count);
                if (options.Encoding == LapVizSampleEncoding.Binary)
                    json.WriteBoolean("byteShuffle", options.ByteShuffle);

                json.WriteEndObject();
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

                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }
    }

    private static void WriteCircuit(Utf8JsonWriter json, LapVizCircuit? circuit)
    {
        if (circuit == null || (circuit.Code == null && circuit.Name == null && circuit.CountryCode == null))
            return;

        json.WriteStartObject("circuit");
        WriteOptional(json, "code", circuit.Code);
        WriteOptional(json, "name", circuit.Name);
        WriteOptional(json, "countryCode", circuit.CountryCode);
        json.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            json.WriteString(name, value);
    }

    private static LapVizFile ReadManifest(byte[] manifest, out SamplesDescription? samples)
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
            if (major != FormatVersion)
                throw new InvalidDataException("Unsupported LapViz format version " + major + " (this reader supports version " + FormatVersion + ")");

            if (!root.TryGetProperty("session", out var session) || session.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("lapviz.json: missing session");

            var startText = String(session, "start") ?? throw new InvalidDataException("lapviz.json: missing session.start");
            if (!DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
                throw new InvalidDataException("lapviz.json: invalid session.start " + startText);

            var file = new LapVizFile(start);
            if (root.TryGetProperty("generator", out var generator) && generator.ValueKind == JsonValueKind.Object)
                file.Generator = new LapVizGenerator { Name = String(generator, "name"), Version = String(generator, "version") };

            file.Session.Driver = String(session, "driver");
            file.Session.Vehicle = String(session, "vehicle");
            file.Session.Description = String(session, "description");
            file.Session.SourceFile = String(session, "sourceFile");
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

            if (!root.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("lapviz.json: missing channels");

            foreach (var channel in channels.EnumerateArray())
            {
                var name = String(channel, "name");
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("lapviz.json: a channel has no name");
                if (file.IndexOf(name!) >= 0)
                    throw new InvalidDataException("lapviz.json: duplicate channel " + name);

                var type = String(channel, "type");
                file._channels.Add(new LapVizChannel
                {
                    Name = name!,
                    Unit = String(channel, "unit"),
                    Description = String(channel, "description"),
                    Type = type == null || type == "float64" ? LapVizValueType.Float64
                        : type == "float32" ? LapVizValueType.Float32
                        : throw new InvalidDataException("lapviz.json: unknown type " + type + " of channel " + name),
                });
            }

            samples = null;
            if (root.TryGetProperty("samples", out var samplesElement) && samplesElement.ValueKind == JsonValueKind.Object)
            {
                var encoding = String(samplesElement, "encoding");
                samples = new SamplesDescription
                {
                    File = String(samplesElement, "file") ?? throw new InvalidDataException("lapviz.json: missing samples.file"),
                    Encoding = encoding == "binary" ? LapVizSampleEncoding.Binary
                        : encoding == "csv" ? LapVizSampleEncoding.Csv
                        : throw new InvalidDataException("lapviz.json: unknown samples.encoding " + encoding),
                    Count = samplesElement.TryGetProperty("count", out var count) && count.TryGetInt32(out var n) ? n : -1,
                    ByteShuffle = samplesElement.TryGetProperty("byteShuffle", out var shuffle) && shuffle.ValueKind == JsonValueKind.True,
                };

                if (samples.Encoding == LapVizSampleEncoding.Binary && samples.Count < 0)
                    throw new InvalidDataException("lapviz.json: binary samples need a count");
            }

            if (root.TryGetProperty("timing", out var timing) && timing.ValueKind == JsonValueKind.Array)
            {
                foreach (var source in timing.EnumerateArray())
                {
                    var timingSource = new LapVizTimingSource
                    {
                        Source = String(source, "source") ?? string.Empty,
                        IsDevice = source.TryGetProperty("device", out var isDevice) && isDevice.ValueKind == JsonValueKind.True,
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
                            });
                        }
                    }

                    file.Timing.Add(timingSource);
                }
            }

            return file;
        }
    }

    private static LapVizCircuit? Circuit(JsonElement parent)
    {
        if (!parent.TryGetProperty("circuit", out var circuit) || circuit.ValueKind != JsonValueKind.Object)
            return null;

        return new LapVizCircuit { Code = String(circuit, "code"), Name = String(circuit, "name"), CountryCode = String(circuit, "countryCode") };
    }

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : (double?)null;

    private sealed class SamplesDescription
    {
        public string File { get; set; } = string.Empty;

        public LapVizSampleEncoding Encoding { get; set; }

        public int Count { get; set; }

        public bool ByteShuffle { get; set; }
    }
}
