namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents weather conditions associated with a session.
/// Can be populated from a weather API, manual input, or device sensors.
/// </summary>
public class WeatherInfo
{
    /// <summary>
    /// Ambient temperature at the track, in the given <see cref="Unit"/>.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Unit of the <see cref="Temperature"/> value.
    /// </summary>
    public TemperatureUnit Unit { get; set; }

    /// <summary>
    /// Relative humidity in percent (0–100).
    /// </summary>
    public double? Humidity { get; set; }

    /// <summary>
    /// Atmospheric pressure in hPa (hectopascals).
    /// </summary>
    public double? Pressure { get; set; }

    /// <summary>
    /// Precipitation amount in millimeters per hour, if available.
    /// </summary>
    public double? Precipitation { get; set; }

    /// <summary>
    /// Wind speed in kilometers per hour (or the preferred unit).
    /// </summary>
    public double? WindSpeed { get; set; }

    /// <summary>
    /// Cloud cover in percent (0–100).
    /// </summary>
    public double? CloudCover { get; set; }

    /// <summary>
    /// Free-text description of conditions (e.g. "Sunny", "Overcast", "Light rain").
    /// </summary>
    public string Conditions { get; set; }

    /// <summary>
    /// Identifier for a weather icon (e.g. "sunny.png" or a weather API icon code).
    /// </summary>
    public string Icon { get; set; }
}
