namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents the synchronization progress of circuit configuration data,
/// </summary>
public class CircuitSyncProgress
{
    /// <summary>
    /// Overall progress as a value between 0.0 and 1.0.
    /// - 0.0 means no progress
    /// - 1.0 means fully completed
    /// Useful for binding to progress bars or reporting percentage done.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// The total number of steps or items that must be processed
    /// in order to complete the synchronization.
    /// Example: total number of laps or telemetry records.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// The current step or item index that has been processed so far.
    /// Starts at 0 and increases until it reaches <see cref="Total"/>.
    /// </summary>
    public int Current { get; set; }
}
