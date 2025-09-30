using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Base CSV, TSV, or delimited text telemetry reader. Handles channel discovery,
/// line parsing, ZIP extraction, and basic value conversion.
/// Subclass to parse device specific event lines via <see cref="ReadRaceEvent(string)"/>.
/// </summary>
public class DelimitedDataReader : FileSystemTelemetryDataReader, ITelemetryDataReader
{
    // Column indexes resolved from the header
    protected int _timestampIndex;
    protected int _latitudeIndex;
    protected int _longitudeIndex;
    protected int _altitudeIndex;
    protected int _speedIndex;

    /// <summary>Indexes of columns to ignore from the raw file.</summary>
    protected ISet<int> _columnToIgnoreIndexes;

    /// <summary>Reader configuration for format, mapping, filters.</summary>
    protected DelimitedDataReaderConfiguration _config;

    /// <summary>Last synthesized timestamp used when there is no timestamp column.</summary>
    protected DateTimeOffset? _lastTimestamp;

    /// <summary>
    /// Initializes the reader with a configuration.
    /// </summary>
    public DelimitedDataReader(DelimitedDataReaderConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException("configuration");
        _config = configuration;
        _columnToIgnoreIndexes = new HashSet<int>();
    }

    /// <summary>
    /// Read first file, scan header area, map channels, and return the kept channel names
    /// in the same order as the parsed values that will be produced.
    /// Also computes indexes for special channels (time, lat, lon, alt, speed).
    /// </summary>
    public override IList<string> GetTelemetryChannels()
    {
        if (string.IsNullOrWhiteSpace(_filename))
            throw new InvalidOperationException("No file loaded.");

        var filesToParse = GetFilesToParse();
        if (filesToParse == null || filesToParse.Count == 0)
            throw new FileNotFoundException("No input files matched.");

        using (var reader = new StreamReader(filesToParse[0]))
        {
            for (var i = 0; i < 25 && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;

                // ChannelsSignature is optional, some files start directly with header row.
                if (!string.IsNullOrEmpty(_config.ChannelsSignature))
                {
                    if (!line.StartsWith(_config.ChannelsSignature, StringComparison.Ordinal))
                        continue; // keep scanning until we hit the header signature
                }

                var channels = ParseChannels(line);
                var channelsToKeep = new List<string>();

                // Resolve special column indexes once per header
                _timestampIndex = channels.IndexOf(GetFormatChannelName("Time"));
                _latitudeIndex = channels.IndexOf(GetFormatChannelName("Latitude"));
                _longitudeIndex = channels.IndexOf(GetFormatChannelName("Longitude"));
                _altitudeIndex = channels.IndexOf(GetFormatChannelName("Altitude"));
                _speedIndex = channels.IndexOf(GetFormatChannelName("Speed"));

                // Store ignore indexes, and build a list of kept channel names in order
                _columnToIgnoreIndexes.Clear();
                for (var c = 0; c < channels.Count; c++)
                {
                    var name = channels[c];
                    if (_config.ColumnsToIgnore.Contains(name))
                        _columnToIgnoreIndexes.Add(c);
                    else
                        channelsToKeep.Add(name);
                }

                return channelsToKeep;
            }
        }

        throw new InvalidOperationException("Failed to detect channel header.");
    }

    /// <summary>
    /// Map a normalized channel name to the format specific header name, if configured.
    /// </summary>
    private string GetFormatChannelName(string commonChannelName)
    {
        if (_config.ChannelNameMapping == null || !_config.ChannelNameMapping.Any())
            return commonChannelName;

        var mapped = _config.ChannelNameMapping
            .FirstOrDefault(x => string.Equals(x.CommonChannelName, commonChannelName, StringComparison.Ordinal));

        return mapped != null ? mapped.FormatChannelName : commonChannelName;
    }

    /// <summary>
    /// Split a header line into channel names, removing quotes and trimming spaces around delimiters.
    /// </summary>
    public virtual List<string> ParseChannels(string line)
    {
        return SplitWithQuotes(line, _config.Delimiter);
    }

    private List<string> SplitWithQuotes(string line, char delimiter)
    {
        var result = new List<string>();
        if (line == null) return result;

        bool inQuotes = false;
        var sb = new System.Text.StringBuilder(line.Length);

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                // Handle escaped quotes ("")
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
                continue;
            }
            if (c == delimiter && !inQuotes) { result.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    /// <summary>
    /// Parse all files, returning a single DeviceSessionData with telemetry points and events.
    /// Subclasses can override <see cref="ReadRaceEvent(string)"/> to extract device specific events.
    /// </summary>
    public override IList<DeviceSessionData> GetSessionData()
    {
        if (string.IsNullOrWhiteSpace(_filename))
            throw new InvalidOperationException("No file loaded.");

        var driverSessionData = new DeviceSessionData(string.Empty, string.Empty);
        driverSessionData.SourceFileHash = GetHash();
        driverSessionData.Generator = _config.TelemetryDevice;

        var fields = GetTelemetryChannels();
        driverSessionData.TelemetryChannels = fields;

        var filesToParse = GetFilesToParse();
        GeoTelemetryData lastGeoTelemetryData = null;

        foreach (var fileToParse in filesToParse)
        {
            using (var reader = new StreamReader(fileToParse))
            {
                // Optional gating until a marker line is seen
                var readyToStart = string.IsNullOrEmpty(_config.DataStartsAfter);
                double maxSpeed = 0;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (!readyToStart)
                    {
                        readyToStart = line.StartsWith(_config.DataStartsAfter, StringComparison.Ordinal);
                        continue;
                    }

                    // Try parse device specific event
                    var sessionEvent = ReadRaceEvent(line);
                    if (sessionEvent != null)
                    {
                        // If the device event line does not carry its own time, align to last GPS ts
                        if (lastGeoTelemetryData != null)
                            sessionEvent.Timestamp = lastGeoTelemetryData.Timestamp;

                        driverSessionData.AddEvent(sessionEvent);
                        continue;
                    }

                    // Parse regular telemetry line
                    var location = ParseLine(line);
                    if (location != null)
                    {
                        driverSessionData.TelemetryData.Add(location);

                        if (location.Speed.HasValue && location.Speed > maxSpeed)
                            maxSpeed = location.Speed.Value;

                        lastGeoTelemetryData = location;
                    }
                }

                driverSessionData.MaxSpeed = maxSpeed;
            }
        }

        // Clean temporary directory if we extracted a ZIP
        var originDir = Path.GetDirectoryName(_filename);
        var parsedDir = Path.GetDirectoryName(filesToParse[0]);
        if (!string.Equals(originDir, parsedDir, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(parsedDir)
            && Directory.Exists(parsedDir))
        {
            try
            {
                Directory.Delete(parsedDir, true);
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        return new List<DeviceSessionData> { driverSessionData };
    }

    /// <summary>
    /// Expand a ZIP into a temp folder and return matching files, or return the single filename.
    /// Filters include and exclude patterns when scanning expanded folder.
    /// </summary>
    private IList<string> GetFilesToParse()
    {
        if (string.Equals(Path.GetExtension(_filename), ".ZIP", StringComparison.OrdinalIgnoreCase))
        {
            var temporaryDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            ZipFile.ExtractToDirectory(_filename, temporaryDirectory);

            var matchedFiles = Directory
                .GetFiles(temporaryDirectory, _config.FileNameInclude)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(_config.FileNameExclude))
            {
                // C# 7.3: string.Contains(StringComparison) not available, use IndexOf >= 0
                matchedFiles.RemoveAll(x =>
                    x.IndexOf(_config.FileNameExclude, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return matchedFiles;
        }

        return new List<string> { _filename };
    }

    /// <summary>
    /// Parse one CSV line into a GeoTelemetryData object, handling ignored columns,
    /// ISO timestamps, and speed conversion. Returns null for comments or metadata lines.
    /// </summary>
    protected virtual GeoTelemetryData ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        if (!string.IsNullOrEmpty(_config.ChannelsSignature) &&
            line.StartsWith(_config.ChannelsSignature, StringComparison.Ordinal))
            return null;

        if (!string.IsNullOrEmpty(_config.FileSignature) &&
            line.StartsWith(_config.FileSignature, StringComparison.Ordinal))
            return null;

        if (line.StartsWith("#", StringComparison.Ordinal)) // comment
            return null;

        if (_config.NonDataLineMatches != null)
        {
            foreach (var match in _config.NonDataLineMatches)
            {
                if (!string.IsNullOrEmpty(match) && line.StartsWith(match, StringComparison.Ordinal))
                    return null;
            }
        }

        var textValues = SplitWithQuotes(line, _config.Delimiter);
        var values = new List<double?>(textValues.Count);

        // Extract numeric values using invariant culture, keep null for non numeric fields
        for (int i = 0; i < textValues.Count; i++)
        {
            if (_columnToIgnoreIndexes.Contains(i))
            {
                values.Add(null);
                continue;
            }

            var raw = textValues[i];
            double d;
            if (!string.IsNullOrWhiteSpace(raw) &&
                double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out d))
            {
                values.Add(d);
            }
            else
            {
                values.Add(null);
            }
        }

        var telemetry = ParseValues(values);

        // Remove ignored values to keep Data aligned with TelemetryChannels
        if (_columnToIgnoreIndexes.Any() && telemetry != null && telemetry.Data != null)
        {
            for (int i = telemetry.Data.Count - 1; i >= 0; i--)
                if (_columnToIgnoreIndexes.Contains(i))
                    telemetry.Data.RemoveAt(i);
        }

        // Optional ISO-8601 timestamp override from raw text
        if (_config.TimestampFormat == TimestampFormat.ISO8601 &&
            _timestampIndex >= 0 &&
            _timestampIndex < textValues.Count)
        {
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(textValues[_timestampIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dto))
                telemetry.Timestamp = dto;
        }

        return telemetry;
    }

    /// <summary>
    /// Override in subclasses to detect and parse device specific event lines
    /// such as "Lap", "Sector", etc. Return null if the line is not an event.
    /// </summary>
    protected virtual SessionDataEvent ReadRaceEvent(string line)
    {
        return null;
    }

    /// <summary>
    /// Convert an array of numeric values into a GeoTelemetryData. Uses configured indexes
    /// for lat, lon, alt, speed, and timestamp. If no timestamp column, synthesizes one
    /// using DataResolution (samples per second).
    /// </summary>
    protected virtual GeoTelemetryData ParseValues(IList<double?> values)
    {
        if (_latitudeIndex < 0 || _longitudeIndex < 0)
            return null;

        var latVal = values[_latitudeIndex];
        var lonVal = values[_longitudeIndex];
        if (!latVal.HasValue || !lonVal.HasValue)
            return null;

        var lat = latVal.Value;
        var lon = lonVal.Value;

        // Timestamp: either from column (epoch seconds or millis, possibly fractional), else synthesized
        DateTimeOffset timestamp;
        if (_timestampIndex >= 0 &&
            _timestampIndex < values.Count &&
            values[_timestampIndex].HasValue)
        {
            // Preserve sub-second precision, do not round.
            // Heuristic: if value looks like ms since epoch (>= 1e11 for modern dates), treat as ms.
            // Otherwise treat as seconds (can have fractional part).
            double raw = values[_timestampIndex].Value;
            var epoch0 = DateTimeOffset.FromUnixTimeMilliseconds(0);

            if (raw >= 100000000000.0) // ms scale
            {
                // raw may include fractional milliseconds; AddMilliseconds(double) preserves them
                timestamp = epoch0.AddMilliseconds(raw);
            }
            else
            {
                // raw may be fractional seconds; AddSeconds(double) preserves ms precision
                timestamp = epoch0.AddSeconds(raw);
            }
        }
        else
        {
            // Synthesize based on DataResolution samples per second
            // Step size in ms = 1000 / DataResolution
            var stepMs = _config.DataResolution > 0 ? 1000.0 / _config.DataResolution : 20.0;
            if (!_lastTimestamp.HasValue)
                _lastTimestamp = DateTimeOffset.Now;

            timestamp = _lastTimestamp.Value.AddMilliseconds(stepMs);
            _lastTimestamp = timestamp;
        }

        var geo = new GeoTelemetryData();
        geo.Latitude = lat;
        geo.Longitude = lon;
        geo.Timestamp = timestamp;
        geo.Data = values;

        if (_altitudeIndex >= 0 &&
            _altitudeIndex < values.Count &&
            values[_altitudeIndex].HasValue)
        {
            geo.Altitude = values[_altitudeIndex].Value;
        }

        if (_speedIndex >= 0 &&
            _speedIndex < values.Count &&
            values[_speedIndex].HasValue)
        {
            geo.Speed = values[_speedIndex].Value * _config.SpeedConversionFactor;
        }

        return geo;
    }

    /// <summary>
    /// Open a StreamReader for the given path, honoring encoding and BOMs.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private StreamReader OpenReader(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // detectEncodingFromByteOrderMarks: true will honor BOMs
        return new StreamReader(fs, _config.FileEncoding, detectEncodingFromByteOrderMarks: true);
    }

    /// <summary>
    /// Quick format sniffing by checking the first two lines for the configured FileSignature.
    /// </summary>
    public override bool IsDataCompatible(string filename)
    {
        using (var reader = OpenReader(filename))
        {
            for (int i = 0; i < 2 && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (!string.IsNullOrEmpty(_config.FileSignature) &&
                    !string.IsNullOrEmpty(line) &&
                    line.StartsWith(_config.FileSignature, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }
}
