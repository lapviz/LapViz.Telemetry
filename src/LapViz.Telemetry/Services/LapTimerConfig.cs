using System;

namespace LapViz.Telemetry.Services;

/// <summary>
/// Configuration for <see cref="LapTimerService"/>.
/// All values have safe defaults and basic validation.
/// </summary>
public sealed class LapTimerConfig
{
    // Defaults kept compatible with current behavior
    public const int DefaultMaxTelemetryDataRetention = 5;
    public static readonly TimeSpan DefaultMinimumTimeBetweenEvents = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultSessionTimeout = TimeSpan.FromMinutes(15);
    public const bool DefaultTrackPosition = false;
    public const bool DefaultAutoStartDetection = false;

    private int _maxTelemetryDataRetention = DefaultMaxTelemetryDataRetention;
    private TimeSpan _minimumTimeBetweenEvents = DefaultMinimumTimeBetweenEvents;
    private string _deviceId = Guid.NewGuid().ToString();
    private string _userId;
    private bool _trackPosition = DefaultTrackPosition;
    private TimeSpan _sessionTimeout = DefaultSessionTimeout;
    private bool _autoStartDetection = DefaultAutoStartDetection;

    /// <summary>
    /// How many recent geolocation samples the timer may retain for trajectory calculations.
    /// Must be &gt;= 2. Default: 5.
    /// </summary>
    public int MaxTelemetryDataRetention
    {
        get => _maxTelemetryDataRetention;
        set
        {
            if (value < 2) throw new ArgumentOutOfRangeException(nameof(MaxTelemetryDataRetention), "Must be >= 2.");
            _maxTelemetryDataRetention = value;
        }
    }

    /// <summary>
    /// Minimum time separation between detected events (sector crossings) when the circuit
    /// does not override it with its own <c>SectorTimeout</c>. Must be &gt;= TimeSpan.Zero.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan MinimumTimeBetweenEvents
    {
        get => _minimumTimeBetweenEvents;
        set
        {
            if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MinimumTimeBetweenEvents), "Must be >= 0.");
            _minimumTimeBetweenEvents = value;
        }
    }

    /// <summary>
    /// Identifier for the device producing the telemetry.
    /// Default: a fresh GUID string. If set to null/empty, a new GUID is generated.
    /// </summary>
    public string DeviceId
    {
        get => _deviceId;
        set => _deviceId = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString() : value;
    }

    /// <summary>
    /// Optional user/driver identifier to stamp on generated events. May be null/empty.
    /// </summary>
    public string UserId
    {
        get => _userId;
        set => _userId = value;
    }

    /// <summary>
    /// When true, emit periodic position breadcrumb events (in addition to sector/lap events).
    /// Default: false.
    /// </summary>
    public bool TrackPosition
    {
        get => _trackPosition;
        set => _trackPosition = value;
    }

    /// <summary>
    /// Idle timeout after which the active session is automatically closed if no events occur.
    /// Must be &gt;= TimeSpan.Zero. Default: 15 minutes.
    /// </summary>
    public TimeSpan SessionTimeout
    {
        get => _sessionTimeout;
        set
        {
            if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(SessionTimeout), "Must be >= 0.");
            _sessionTimeout = value;
        }
    }

    /// <summary>
    /// If true, event detection starts immediately; otherwise it begins paused
    /// until <see cref="LapTimerService.StartDetection"/> is called. Default: false.
    /// </summary>
    public bool AutoStartDetection
    {
        get => _autoStartDetection;
        set => _autoStartDetection = value;
    }

    /// <summary>
    /// Convenience: the minimum event separation as whole seconds (rounded down),
    /// matching how <see cref="LapTimerService"/> currently applies it.
    /// </summary>
    public int MinimumEventSeparationSeconds => (int)_minimumTimeBetweenEvents.TotalSeconds;

    /// <summary>
    /// Ensures required identifiers are present; generates a DeviceId if missing.
    /// Call this if you constructed the config with all-defaults and want to guarantee IDs.
    /// </summary>
    public void EnsureIdentifiers()
    {
        if (string.IsNullOrWhiteSpace(_deviceId))
            _deviceId = Guid.NewGuid().ToString();
        // UserId is optional; leave as-is.
    }
}
