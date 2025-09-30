namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents a driver participating in a session.
/// Includes identification, display name, race number, and optional country code.
/// </summary>
public class Driver
{
    /// <summary>
    /// Unique identifier for the driver.
    /// This may come from a device, database, or external system.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name of the driver.
    /// Example: "Charles Leclerc".
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Race number assigned to the driver.
    /// Example: "303".
    /// </summary>
    public string Number { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code representing the driver’s nationality.
    /// Example: "GB" for Great Britain, "BE" for Belgium.
    /// </summary>
    public string CountryCode { get; set; }

    /// <summary>
    /// Returns a simple string representation of the driver,
    /// combining name and number for easy display in logs or UIs.
    /// </summary>
    public override string ToString()
    {
        return $"{Name}-{Number}";
    }
}
