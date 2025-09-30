using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Reads LapViz delimited telemetry files written by <see cref="LapVizDataWriter"/>.
/// Supports plain text or ".lvz" (zip) with a single data entry.
/// Uses a single-pass parser that:
///   - waits for "#Fields=" before reading any data,
///   - parses "#Event=" lines at any time,
///   - normalizes every data row to exactly the field count (pad/truncate),
///   - maps well-known channels (Latitude, Longitude, Altitude, Speed, Accuracy) to properties.
/// </summary>
public class LapVizDataReader : FileSystemTelemetryDataReader, ITelemetryDataReader
{
    private const string TOPHEADER = "#Format=LapViz Delimited Data";
    private const string VERSION = "#Version=1";
    private const string FIELDS_PREFIX = "#Fields=";
    private const string EVENT_PREFIX = "#Event=";
    private const string CIRCUIT_PREFX = "#CircuitCode=";

    private IList<string> _channels;
    private int _latIndex = -1, _lonIndex = -1, _altIndex = -1, _spdIndex = -1, _accIndex = -1;

    public override ITelemetryDataReader Load(string filename)
    {
        return base.Load(filename);
    }

    /// <summary>
    /// Quickly reads headers to return the channel list without parsing the full file.
    /// </summary>
    public override IList<string> GetTelemetryChannels()
    {
        EnsureFileLoaded();

        using (var dataStream = OpenDataStream(_filename))
        using (var reader = new StreamReader(dataStream, new UTF8Encoding(false), true))
        {
            string line;
            IList<string> channels = null;

            // Scan until #Fields= is found.
            for (int i = 0; i < 200 && (line = reader.ReadLine()) != null; i++)
            {
                if (!line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith(FIELDS_PREFIX, StringComparison.Ordinal))
                {
                    channels = line.Substring(FIELDS_PREFIX.Length)
                                   .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim())
                                   .ToList();
                    break;
                }
            }

            _channels = channels ?? new List<string>();
            return _channels;
        }
    }

    /// <summary>
    /// Parses the entire file into a single <see cref="DeviceSessionData"/>.
    /// One-pass parsing ensures no data row is accidentally consumed during header scan.
    /// </summary>
    public override IList<DeviceSessionData> GetSessionData()
    {
        EnsureFileLoaded();

        var session = new DeviceSessionData(string.Empty, string.Empty)
        {
            Generator = "LapViz",
            OriginalFilename = Path.GetFileName(_filename),
            SourceFileHash = Path.GetFileName(_filename) // preserved behavior
        };

        using (var dataStream = OpenDataStream(_filename))
        using (var reader = new StreamReader(dataStream, new UTF8Encoding(false), true))
        {
            bool fieldsSeen = false;
            string line;
            double maxSpeed = 0;
            GeoTelemetryData last = null;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                // Comments / directives
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    // Format & Version are informational; ignore
                    if (line.StartsWith(TOPHEADER, StringComparison.Ordinal)) continue;
                    if (line.StartsWith(VERSION, StringComparison.Ordinal)) continue;

                    if (line.StartsWith(CIRCUIT_PREFX, StringComparison.Ordinal))
                    {
                        session.CircuitCode = line.Substring(CIRCUIT_PREFX.Length).Trim();
                        continue;
                    }

                    if (line.StartsWith(FIELDS_PREFIX, StringComparison.Ordinal))
                    {
                        _channels = line.Substring(FIELDS_PREFIX.Length)
                                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim())
                                        .ToList();
                        session.TelemetryChannels = new List<string>(_channels);

                        ResolveWellKnownChannelIndexes(_channels);

                        fieldsSeen = true;
                        continue;
                    }

                    if (line.StartsWith(EVENT_PREFIX, StringComparison.Ordinal))
                    {
                        var evt = ParseEvent(line);
                        if (evt != null)
                        {
                            if (last != null && evt.Timestamp == default(DateTimeOffset))
                                evt.Timestamp = last.Timestamp;
                            session.AddEvent(evt);
                        }
                        continue;
                    }

                    // Any other "#" lines are ignored.
                    continue;
                }

                // Data lines are only valid after #Fields= has been seen
                if (!fieldsSeen)
                    continue;

                var data = ParseDataLine(line);
                if (data != null)
                {
                    session.TelemetryData.Add(data);
                    if (data.Speed.HasValue && data.Speed.Value > maxSpeed)
                        maxSpeed = data.Speed.Value;
                    last = data;
                }
            }

            session.MaxSpeed = maxSpeed;

            // If the file lacked #Fields=, ensure TelemetryChannels is non-null
            if (session.TelemetryChannels == null)
                session.TelemetryChannels = new List<string>();
        }

        return new List<DeviceSessionData> { session };
    }

    /// <summary>
    /// Quick compatibility sniff: accept ".lvz" or files that start with the #Format header.
    /// </summary>
    public override bool IsDataCompatible(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return false;

        if (filename.EndsWith(".lvz", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            using (var dataStream = OpenDataStream(filename))
            using (var reader = new StreamReader(dataStream, new UTF8Encoding(false), true))
            {
                for (int i = 0; i < 5 && !reader.EndOfStream; i++)
                {
                    var line = reader.ReadLine();
                    if (line != null && line.StartsWith(TOPHEADER, StringComparison.Ordinal))
                        return true;
                }
            }
        }
        catch
        {
            // Ignore IO/format errors during sniffing.
        }

        return false;
    }

    private void EnsureFileLoaded()
    {
        if (string.IsNullOrWhiteSpace(_filename))
            throw new InvalidOperationException("No file loaded.");
    }

    /// <summary>
    /// Returns a readable stream:
    ///  - For ".lvz", extracts the first entry to a memory stream (simple lifetime handling for C# 7.3).
    ///  - Otherwise, opens the file directly.
    /// </summary>
    private static Stream OpenDataStream(string filename)
    {
        if (IsZipFile(filename))
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault()
                            ?? throw new InvalidOperationException("Empty archive.");
                var ms = new MemoryStream();
                using (var es = entry.Open()) es.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
        }
        return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>
    /// Detects if the file is a zip archive by checking the first 4 bytes for the "PK\x03\x04" signature.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    private static bool IsZipFile(string filename)
    {
        try
        {
            byte[] sig = new byte[4];
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Length < 4) return false;
                fs.Read(sig, 0, 4);
            }
            return sig[0] == (byte)'P' && sig[1] == (byte)'K' && sig[2] == 3 && sig[3] == 4;
        }
        catch { return false; }
    }

    /// <summary>
    /// Resolves convenient indexes for well-known channels (if present).
    /// </summary>
    private void ResolveWellKnownChannelIndexes(IList<string> channels)
    {
        _latIndex = channels != null ? channels.IndexOf("Latitude") : -1;
        _lonIndex = channels != null ? channels.IndexOf("Longitude") : -1;
        _altIndex = channels != null ? channels.IndexOf("Altitude") : -1;
        _spdIndex = channels != null ? channels.IndexOf("Speed") : -1;
        _accIndex = channels != null ? channels.IndexOf("Accuracy") : -1;
    }

    /// <summary>
    /// Parses an event line in the form "#Event=tsMs,Type,Lap,Sector,TimeTicks".
    /// Returns null if the line is malformed.
    /// </summary>
    private SessionDataEvent ParseEvent(string line)
    {
        try
        {
            var payload = line.Substring(EVENT_PREFIX.Length);
            var parts = payload.Split(',');
            if (parts.Length < 5) return null;

            var ts = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[0], CultureInfo.InvariantCulture));
            var type = (SessionEventType)Enum.Parse(typeof(SessionEventType), parts[1], true);
            var lap = int.Parse(parts[2], CultureInfo.InvariantCulture);
            var sector = int.Parse(parts[3], CultureInfo.InvariantCulture);
            var ticks = long.Parse(parts[4], CultureInfo.InvariantCulture);

            return new SessionDataEvent
            {
                Timestamp = ts,
                Type = type,
                LapNumber = lap,
                Sector = sector,
                Time = new TimeSpan(ticks)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses one telemetry data line: "tsMs,v1,v2,..."
    /// Pads or truncates the value list so its length matches the channel count exactly.
    /// Also populates well-known <see cref="GeoTelemetryData"/> properties when available.
    /// </summary>
    private GeoTelemetryData ParseDataLine(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 1) return null;

        long epochMs;
        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out epochMs))
            return null;

        var channelCount = _channels != null ? _channels.Count : 0;
        var valuesParsed = parts.Length - 1;

        var data = new List<double?>(channelCount > 0 ? channelCount : Math.Max(0, valuesParsed));

        // Parse numeric values for present columns.
        for (int i = 0; i < valuesParsed; i++)
        {
            var s = parts[i + 1];
            if (string.IsNullOrWhiteSpace(s))
            {
                data.Add(null);
                continue;
            }

            double v;
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v))
                data.Add(v);
            else
                data.Add(null);
        }

        // Normalize length to EXACT channelCount (pad with nulls or truncate extras).
        if (channelCount > 0)
        {
            if (data.Count < channelCount)
            {
                for (int k = data.Count; k < channelCount; k++) data.Add(null);
            }
            else if (data.Count > channelCount)
            {
                data.RemoveRange(channelCount, data.Count - channelCount);
            }
        }

        var geo = new GeoTelemetryData
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(epochMs),
            Data = data
        };

        // Map well-known channels (bounds & HasValue checks).
        if (_latIndex >= 0 && _latIndex < data.Count && data[_latIndex].HasValue)
            geo.Latitude = data[_latIndex].Value;

        if (_lonIndex >= 0 && _lonIndex < data.Count && data[_lonIndex].HasValue)
            geo.Longitude = data[_lonIndex].Value;

        if (_altIndex >= 0 && _altIndex < data.Count && data[_altIndex].HasValue)
            geo.Altitude = data[_altIndex].Value;

        if (_spdIndex >= 0 && _spdIndex < data.Count && data[_spdIndex].HasValue)
            geo.Speed = data[_spdIndex].Value;

        if (_accIndex >= 0 && _accIndex < data.Count && data[_accIndex].HasValue)
            geo.Accuracy = data[_accIndex].Value;

        return geo;
    }
}
