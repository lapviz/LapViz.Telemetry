using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Samples that share a time base: the channels a logger measures at the same rate (LapViz format, section 2.3). The
/// time is in microseconds since <see cref="LapVizSessionInfo.Start"/>, the values of a channel are one array per channel,
/// NaN for a missing value.
/// </summary>
public sealed class LapVizSampleGroup
{
    private static readonly Regex NamePattern = new Regex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);

    private readonly LapVizFile _file;
    private readonly List<LapVizChannel> _channels = new List<LapVizChannel>();
    private readonly List<double[]> _values = new List<double[]>();
    private string _name;

    internal LapVizSampleGroup(LapVizFile file, string name)
    {
        _file = file;
        _name = CheckName(name);
    }

    /// <summary>Name of the group, unique in the file: lowercase letters, digits, '-' and '_' ("gps", "engine", "imu").</summary>
    public string Name
    {
        get => _name;
        set
        {
            var name = CheckName(value);
            if (_file.Groups.Any(x => !ReferenceEquals(x, this) && x.Name == name))
                throw new ArgumentException("Duplicate group: " + name, nameof(value));

            _name = name;
        }
    }

    /// <summary>The channels, in the order of the columns.</summary>
    public IReadOnlyList<LapVizChannel> Channels => _channels;

    /// <summary>Microseconds since <see cref="LapVizSessionInfo.Start"/>, strictly increasing.</summary>
    public long[] Time { get; private set; } = Array.Empty<long>();

    /// <summary>Number of samples.</summary>
    public int Count => Time.Length;

    /// <summary>The group has samples or channels: it is written.</summary>
    internal bool IsEmpty => Count == 0 && _channels.Count == 0;

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

    /// <summary>Adds a channel; <paramref name="values"/> has one value per sample (NaN: missing). Names are unique in the file.</summary>
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
        if (_file.FindChannel(channel.Name) != null)
            throw new ArgumentException("Duplicate channel: " + channel.Name, nameof(channel));

        _channels.Add(channel);
        _values.Add(values);
    }

    /// <summary>Values of the channel <paramref name="name"/> (case insensitive); false when the group does not have it.</summary>
    public bool TryGetValues(string name, out double[] values)
    {
        var index = IndexOf(name);
        values = index < 0 ? Array.Empty<double>() : _values[index];
        return index >= 0;
    }

    /// <summary>Values of the channel at <paramref name="index"/> in <see cref="Channels"/>.</summary>
    public double[] Values(int index) => _values[index];

    /// <inheritdoc/>
    public override string ToString() => Name + ": " + Count + " samples, " + _channels.Count + " channels";

    internal int IndexOf(string name)
    {
        for (int i = 0; i < _channels.Count; i++)
        {
            if (string.Equals(_channels[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>A channel of the manifest, before its values are read.</summary>
    internal void DeclareChannel(LapVizChannel channel)
    {
        if (_file.FindChannel(channel.Name) != null)
            throw new InvalidDataException("lapviz.json: duplicate channel " + channel.Name);

        _channels.Add(channel);
    }

    /// <summary>The channels of the manifest without samples: empty columns.</summary>
    internal void SetEmptyColumns()
    {
        _values.Clear();
        _values.AddRange(_channels.Select(_ => Array.Empty<double>()));
    }

    internal static bool IsValidName(string? name) => name != null && NamePattern.IsMatch(name);

    private static string CheckName(string name) =>
        IsValidName(name) ? name : throw new ArgumentException("Invalid group name '" + name + "': lowercase letters, digits, '-' and '_', 64 characters at most", nameof(name));

    // Binary samples (section 3 of the specification)
    internal long BinaryLength(int count) =>
        count < 0 ? 0 : (long)count * (sizeof(long) + _channels.Sum(x => Size(x.Type)));

    internal void WriteBinary(Stream stream, bool shuffle)
    {
        WriteColumn(stream, MemoryMarshal.AsBytes(Time.AsSpan()).ToArray(), sizeof(long), shuffle);
        for (int k = 0; k < _channels.Count; k++)
            WriteColumn(stream, Encode(_channels[k], _values[k]), Size(_channels[k].Type), shuffle);
    }

    internal void ReadBinary(byte[] data, int count, bool shuffle)
    {
        if (count < 0)
            throw new InvalidDataException("Negative sample count");

        long expected = BinaryLength(count);
        if (data.Length != expected)
            throw new InvalidDataException("The binary samples of " + Name + " have " + data.Length + " bytes, " + expected + " expected for " + count + " samples");

        int offset = 0;
        var time = MemoryMarshal.Cast<byte, long>(ReadColumn(data, ref offset, count, sizeof(long), shuffle)).ToArray();
        var values = new List<double[]>(_channels.Count);
        foreach (var channel in _channels)
            values.Add(Decode(channel, ReadColumn(data, ref offset, count, Size(channel.Type), shuffle), count));

        SetSamples(time, values);
    }

    internal static int Size(LapVizValueType type)
    {
        switch (type)
        {
            case LapVizValueType.Int8:
            case LapVizValueType.UInt8:
                return 1;
            case LapVizValueType.Int16:
            case LapVizValueType.UInt16:
                return 2;
            case LapVizValueType.Int32:
            case LapVizValueType.Float32:
                return 4;
            default:
                return 8;
        }
    }

    /// <summary>
    /// Smallest and largest raw values of an integer type that are values: the missing value is the minimum of a signed
    /// type and the maximum of an unsigned one.
    /// </summary>
    internal static (long Min, long Max, long Missing) Range(LapVizValueType type)
    {
        switch (type)
        {
            case LapVizValueType.Int8:
                return (sbyte.MinValue + 1, sbyte.MaxValue, sbyte.MinValue);
            case LapVizValueType.UInt8:
                return (0, byte.MaxValue - 1, byte.MaxValue);
            case LapVizValueType.Int16:
                return (short.MinValue + 1, short.MaxValue, short.MinValue);
            case LapVizValueType.UInt16:
                return (0, ushort.MaxValue - 1, ushort.MaxValue);
            case LapVizValueType.Int32:
                return (int.MinValue + 1, int.MaxValue, int.MinValue);
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Not an integer type");
        }
    }

    /// <summary>The little endian bytes of a column: floats as they are, integers as (value - offset) / scale.</summary>
    private static byte[] Encode(LapVizChannel channel, double[] values)
    {
        switch (channel.Type)
        {
            case LapVizValueType.Float64:
                return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
            case LapVizValueType.Float32:
                return MemoryMarshal.AsBytes(values.Select(x => (float)x).ToArray().AsSpan()).ToArray();
        }

        var raw = Raw(channel, values);
        switch (channel.Type)
        {
            case LapVizValueType.Int8:
                return raw.Select(x => unchecked((byte)(sbyte)x)).ToArray();
            case LapVizValueType.UInt8:
                return raw.Select(x => (byte)x).ToArray();
            case LapVizValueType.Int16:
                return MemoryMarshal.AsBytes(raw.Select(x => (short)x).ToArray().AsSpan()).ToArray();
            case LapVizValueType.UInt16:
                return MemoryMarshal.AsBytes(raw.Select(x => (ushort)x).ToArray().AsSpan()).ToArray();
            default:
                return MemoryMarshal.AsBytes(raw.Select(x => (int)x).ToArray().AsSpan()).ToArray();
        }
    }

    private static long[] Raw(LapVizChannel channel, double[] values)
    {
        var (min, max, missing) = Range(channel.Type);
        var raw = new long[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (double.IsNaN(values[i]))
            {
                raw[i] = missing;
                continue;
            }

            var scaled = Math.Round((values[i] - channel.Offset) / channel.Scale);
            if (double.IsInfinity(scaled) || scaled < min || scaled > max)
            {
                throw new InvalidOperationException(
                    "The value " + values[i].ToString(CultureInfo.InvariantCulture) + " of the channel " + channel.Name + " does not fit its type " +
                    LapVizFile.TypeName(channel.Type) + " (scale " + channel.Scale.ToString(CultureInfo.InvariantCulture) + ", offset " + channel.Offset.ToString(CultureInfo.InvariantCulture) + ")");
            }

            raw[i] = (long)scaled;
        }

        return raw;
    }

    private static double[] Decode(LapVizChannel channel, byte[] bytes, int count)
    {
        switch (channel.Type)
        {
            case LapVizValueType.Float64:
                return MemoryMarshal.Cast<byte, double>(bytes).ToArray();
            case LapVizValueType.Float32:
                var floats = MemoryMarshal.Cast<byte, float>(bytes);
                var column = new double[count];
                for (int i = 0; i < count; i++)
                    column[i] = floats[i];

                return column;
        }

        long[] raw;
        switch (channel.Type)
        {
            case LapVizValueType.Int8:
                raw = bytes.Select(x => (long)unchecked((sbyte)x)).ToArray();
                break;
            case LapVizValueType.UInt8:
                raw = bytes.Select(x => (long)x).ToArray();
                break;
            case LapVizValueType.Int16:
                raw = MemoryMarshal.Cast<byte, short>(bytes).ToArray().Select(x => (long)x).ToArray();
                break;
            case LapVizValueType.UInt16:
                raw = MemoryMarshal.Cast<byte, ushort>(bytes).ToArray().Select(x => (long)x).ToArray();
                break;
            default:
                raw = MemoryMarshal.Cast<byte, int>(bytes).ToArray().Select(x => (long)x).ToArray();
                break;
        }

        var missing = Range(channel.Type).Missing;
        return raw.Select(x => x == missing ? double.NaN : (x * channel.Scale) + channel.Offset).ToArray();
    }

    /// <summary>Writes a column of little endian values, transposed when <paramref name="shuffle"/>.</summary>
    private static void WriteColumn(Stream stream, byte[] bytes, int size, bool shuffle)
    {
        EnsureLittleEndian(bytes, size);
        if (!shuffle || size == 1)
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
        if (BitConverter.IsLittleEndian || size == 1)
            return;

        for (int i = 0; i < bytes.Length; i += size)
            Array.Reverse(bytes, i, size);
    }

    // CSV samples (section 4 of the specification): the values, never the raw integers
    internal void WriteCsv(Stream stream)
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

    internal void ReadCsv(byte[] data)
    {
        using (var reader = new StreamReader(new MemoryStream(data), new UTF8Encoding(false), true))
        {
            var header = reader.ReadLine() ?? throw new InvalidDataException("The CSV samples of " + Name + " are empty");
            var names = SplitCsv(header);
            if (names.Count == 0 || !string.Equals(names[0], "time", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The CSV samples must start with the time column");
            if (names.Count - 1 != _channels.Count || names.Skip(1).Where((name, k) => !string.Equals(name, _channels[k].Name, StringComparison.Ordinal)).Any())
                throw new InvalidDataException("The columns of the CSV samples do not match the channels of the manifest");

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
                    throw new InvalidDataException("CSV line " + lineNumber + ": invalid time");

                time.Add((long)Math.Round(seconds * 1_000_000));
                for (int k = 0; k < columns.Count; k++)
                {
                    var field = k + 1 < fields.Count ? fields[k + 1] : string.Empty;
                    columns[k].Add(field.Length == 0
                        ? double.NaN
                        : double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException("CSV line " + lineNumber + ": invalid value for " + _channels[k].Name));
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
                throw new InvalidDataException("The time of the samples must be strictly increasing (" + Name + ", sample " + i + ")");
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
}
