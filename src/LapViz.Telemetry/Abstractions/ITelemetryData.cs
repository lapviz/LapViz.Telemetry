using System;
using System.Collections.Generic;

namespace LapViz.Telemetry.Abstractions;

/// <summary>
/// Represents a single telemetry sample at a given timestamp,
/// containing values across multiple channels.
/// </summary>
public interface ITelemetryData
{
    /// <summary>Vector of values aligned with chanell order. Null if missing.</summary>
    IList<double?> Data { get; }
    /// <summary>
    /// Timestamp of the sample, ideally in UTC.
    /// </summary>
    DateTimeOffset Timestamp { get; set; }
}
