namespace LapViz.Telemetry.IO;

/// <summary>
/// Maps a format-specific channel name (as found in the file) to a
/// standardized/common channel name used in the application.
/// </summary>
public class DelimitedDataReaderChannelMap
{
    /// <summary>
    /// The raw column name as it appears in the file format.
    /// Example: "Lat" or "Longitude(deg)".
    /// </summary>
    public string FormatChannelName { get; set; }

    /// <summary>
    /// The normalized/common channel name used internally.
    /// Example: "Latitude", "Longitude", "Speed".
    /// </summary>
    public string CommonChannelName { get; set; }
}
