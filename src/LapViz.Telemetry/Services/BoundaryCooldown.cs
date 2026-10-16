using System;
using System.Collections.Generic;

namespace LapViz.Telemetry.Services;

/// <summary>
/// Minimum time between two crossings of the same boundary, shared by <see cref="LapTimerService"/>
/// and <see cref="GeolocationSingleSessionManager"/>.
/// The cooldown is per boundary: a crossing holds its own line (a noisy fix that crosses it back and forth),
/// not the other lines, so two boundaries closer in time than the cooldown (a short sector) both count.
/// The LapViz devices apply the same rule (LapViz.Devices, gps_lap_timer.h).
/// </summary>
internal sealed class BoundaryCooldown
{
    // Last crossing per sector number (as stamped on the sector events)
    private readonly Dictionary<int, DateTimeOffset> _lastCrossing = new Dictionary<int, DateTimeOffset>();

    /// <summary>True when the boundary of <paramref name="sectorNumber"/> may be crossed at <paramref name="timestamp"/>.</summary>
    public bool IsOpen(int sectorNumber, DateTimeOffset timestamp, TimeSpan cooldown)
        => !_lastCrossing.TryGetValue(sectorNumber, out var last) || last + cooldown <= timestamp;

    /// <summary>Records a crossing of the boundary of <paramref name="sectorNumber"/>.</summary>
    public void Register(int sectorNumber, DateTimeOffset timestamp)
        => _lastCrossing[sectorNumber] = timestamp;

    /// <summary>Forgets all crossings (new session or new circuit).</summary>
    public void Clear() => _lastCrossing.Clear();
}
