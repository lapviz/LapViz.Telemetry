using System.Collections.Generic;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Configuration object for a delimited telemetry data reader (CSV, TSV, etc.).
/// Controls how files are recognized and how channels are mapped.
/// </summary>
public class DelimitedDataReaderConfiguration
{
    /// <summary>
    /// Character used to separate columns in the file.
    /// Example: ',' for CSV, '\t' for TSV.
    /// </summary>
    public char Delimiter { get; set; }

    /// <summary>
    /// String signature used to identify files of this format (e.g. header marker).
    /// </summary>
    public string FileSignature { get; set; }

    /// <summary>
    /// Optional signature string representing the expected channel layout.
    /// </summary>
    public string ChannelsSignature;

    /// <summary>
    /// List of line prefixes or patterns that should be skipped
    /// because they do not contain data (e.g. headers, comments).
    /// </summary>
    public IList<string> NonDataLineMatches { get; set; }

    /// <summary>
    /// List of columns to ignore when parsing (by name).
    /// </summary>
    public IList<string> ColumnsToIgnore { get; set; }

    /// <summary>
    /// Timestamp format used in the file (Unix time or ISO-8601).
    /// </summary>
    public TimestampFormat TimestampFormat { get; set; }

    /// <summary>
    /// Conversion factor applied to speed values.
    /// Example: 3.6 converts from m/s to km/h.
    /// </summary>
    public double SpeedConversionFactor { get; set; }

    /// <summary>
    /// Expected data resolution (samples per second).
    /// Default is 50 Hz.
    /// </summary>
    public int DataResolution { get; set; }

    /// <summary>
    /// File inclusion filter (supports wildcards).
    /// Example: "*.csv".
    /// </summary>
    public string FileNameInclude { get; set; }

    /// <summary>
    /// File exclusion filter (supports wildcards).
    /// Example: "*_backup.csv".
    /// </summary>
    public string FileNameExclude { get; set; }

    /// <summary>
    /// Identifier for the telemetry device/source associated with this configuration.
    /// Example: "TextFile", "RaceBox", "GPSLogger".
    /// </summary>
    public string TelemetryDevice { get; set; }

    /// <summary>
    /// Marker indicating that data begins after this line.
    /// Useful when skipping metadata sections.
    /// </summary>
    public string DataStartsAfter { get; set; }

    /// <summary>
    /// File encoding used when reading the file.
    /// </summary>
    public System.Text.Encoding FileEncoding { get; set; } = System.Text.Encoding.UTF8;

    /// <summary>
    /// Mapping of file-specific channel names to common/normalized channel names.
    /// </summary>
    public IList<DelimitedDataReaderChannelMap> ChannelNameMapping { get; set; }

    /// <summary>
    /// Initializes the configuration with sensible defaults and common channel mappings.
    /// </summary>
    public DelimitedDataReaderConfiguration()
    {
        ChannelNameMapping = new List<DelimitedDataReaderChannelMap>()
        {
            new DelimitedDataReaderChannelMap()
            {
                FormatChannelName = "Time",
                CommonChannelName = "Time"
            },
            new DelimitedDataReaderChannelMap()
            {
                FormatChannelName = "Latitude",
                CommonChannelName = "Latitude"
            },
            new DelimitedDataReaderChannelMap()
            {
                FormatChannelName = "Longitude",
                CommonChannelName = "Longitude"
            },
            new DelimitedDataReaderChannelMap()
            {
                FormatChannelName = "Altitude",
                CommonChannelName = "Altitude"
            },
            new DelimitedDataReaderChannelMap()
            {
                FormatChannelName = "Speed",
                CommonChannelName = "Speed"
            },
        };

        TimestampFormat = TimestampFormat.UnixTime;
        SpeedConversionFactor = 1.0;
        NonDataLineMatches = new List<string>();
        ColumnsToIgnore = new List<string>();
        DataResolution = 50;
        FileNameInclude = "*";
        FileNameExclude = string.Empty;
        TelemetryDevice = "TextFile";
    }
}
