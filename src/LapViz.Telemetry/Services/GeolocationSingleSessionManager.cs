using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.Services;

/// <summary>
/// Manages a single, continuous telemetry session stream for one driver/device at a time.
/// - Detects circuit changes (via <see cref="ICircuitService"/>).
/// - Creates/ends <see cref="DeviceSessionData"/> sessions automatically.
/// - Detects sector/lap events by intersecting motion with segment boundaries.
/// - (Optional) emits periodic "position" events when <c>trackPosition</c> is enabled.
///
/// Thread-safety: all public mutations are protected by a private lock, so you can feed it
/// from a background thread safely.
/// </summary>
public class GeolocationSingleSessionManager
{
    private readonly object _sync = new object();

    // Sessions we produced in lifetime (most recent is "current")
    private readonly List<DeviceSessionData> _driverSessions = new List<DeviceSessionData>();

    private CircuitConfiguration _circuit;
    private DateTimeOffset _lastCircuitCheck = DateTimeOffset.MinValue;

    // Raw telemetry we last saw; used to form the motion segment for intersection
    private GeoTelemetryData _previousTelemetryData;

    // The active session being built
    private DeviceSessionData _currentDriverSessionData;

    // Telemetry timestamp at which the active session started (idle timeout reference before any event)
    private DateTimeOffset _currentSessionStart;

    // Configuration
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(15);   // idle timeout to end a session
    private readonly bool _trackPosition;
    private readonly int _minSecondsBetweenSectors;
    private readonly ICircuitService _circuitService;
    private readonly ILogger _logger;

    // Identity / tagging
    private string _deviceId;
    private string _driverId;

    public Version Version { get; } = new Version(1, 0, 0, 3);

    public GeolocationSingleSessionManager(
        ICircuitService circuitService,
        bool trackPosition = false,
        int minSecondsBetweenSectors = 5,
        string deviceId = "notset",
        string driverId = "notset")
        : this(circuitService, null, trackPosition, minSecondsBetweenSectors, deviceId, driverId)
    {
    }

    public GeolocationSingleSessionManager(
        ICircuitService circuitService,
        ILogger<GeolocationSingleSessionManager> logger,
        bool trackPosition = false,
        int minSecondsBetweenSectors = 5,
        string deviceId = "notset",
        string driverId = "notset")
    {
        _circuitService = circuitService ?? throw new ArgumentNullException(nameof(circuitService));
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _trackPosition = trackPosition;
        _minSecondsBetweenSectors = minSecondsBetweenSectors;
        _deviceId = deviceId ?? "notset";
        _driverId = driverId ?? "notset";
    }

    /// <summary>The currently active session being built, or null if none.</summary>
    public DeviceSessionData CurrentDriverSession
    {
        get { lock (_sync) return _currentDriverSessionData; }
    }

    /// <summary>All sessions produced by this manager, newest last.</summary>
    public IReadOnlyList<DeviceSessionData> DriverSessions
    {
        get { lock (_sync) return _driverSessions.AsReadOnly(); }
    }

    /// <summary>The circuit currently detected, or null if none.</summary>
    public CircuitConfiguration CurrentCircuit
    {
        get { lock (_sync) return _circuit; }
    }

    public void SetDeviceId(string deviceId)
    {
        lock (_sync) _deviceId = deviceId ?? "notset";
    }

    public void SetDriverId(string driverId)
    {
        lock (_sync) _driverId = driverId ?? "notset";
    }

    /// <summary>
    /// Feed one GPS telemetry sample. The manager will:
    /// - periodically attempt circuit detection (throttled),
    /// - create/end sessions on circuit change and idle timeout,
    /// - detect sector crossings (and derive laps),
    /// - optionally emit position events if <c>trackPosition</c> is true.
    /// </summary>
    public void AddGeolocation(GeoTelemetryData geoTelemetryData)
    {
        if (geoTelemetryData == null) return;

        // Circuit detection is async; run it on the thread pool so blocking here can never
        // deadlock a caller that owns a synchronization context (UI thread). The built-in circuits
        // are detected synchronously: no thread pool, which also keeps this method usable where
        // nothing may block on a task (browser, WebAssembly).
        CircuitConfiguration? detected = null;
        var checkCircuit = ShouldCheckCircuit(geoTelemetryData);
        if (checkCircuit)
        {
            detected = _circuitService is StaticCircuitService
                ? _circuitService.Detect(geoTelemetryData).GetAwaiter().GetResult()
                : Task.Run(() => _circuitService.Detect(geoTelemetryData)).GetAwaiter().GetResult();
        }

        Process(geoTelemetryData, checkCircuit, detected);
    }

    /// <summary>
    /// Asynchronous variant of <see cref="AddGeolocation"/>: circuit detection is awaited
    /// instead of blocking the calling thread.
    /// </summary>
    public async Task AddGeolocationAsync(GeoTelemetryData geoTelemetryData, CancellationToken cancellationToken = default)
    {
        if (geoTelemetryData == null) return;
        cancellationToken.ThrowIfCancellationRequested();

        CircuitConfiguration? detected = null;
        var checkCircuit = ShouldCheckCircuit(geoTelemetryData);
        if (checkCircuit)
            detected = await _circuitService.Detect(geoTelemetryData).ConfigureAwait(false);

        Process(geoTelemetryData, checkCircuit, detected);
    }

    /// <summary>Circuit detection is throttled to every 2 seconds, or done on every sample while no circuit is known.</summary>
    private bool ShouldCheckCircuit(GeoTelemetryData sample)
    {
        lock (_sync)
            return _circuit == null || _lastCircuitCheck + TimeSpan.FromSeconds(2) <= sample.Timestamp;
    }

    private void Process(GeoTelemetryData geoTelemetryData, bool circuitChecked, CircuitConfiguration? detected)
    {
        lock (_sync)
        {
            // 1) Circuit change
            bool circuitChanged = false;
            if (circuitChecked)
            {
                _lastCircuitCheck = geoTelemetryData.Timestamp;
                if (detected != null && (_circuit == null || !string.Equals(detected.Code, _circuit.Code, StringComparison.Ordinal)))
                {
                    _circuit = detected;
                    circuitChanged = true;
                }
            }

            if (circuitChanged)
            {
                // Notify listeners about circuit change and close the active session (if any)
                OnCircuitChanged(_circuit);
                EndCurrentSession();

                // Create a fresh session bound to the new circuit
                CreateSession(geoTelemetryData.Timestamp);
            }

            // If no circuit is known yet, hold onto the sample as "previous" and return
            if (_circuit == null)
            {
                _previousTelemetryData = geoTelemetryData;
                return;
            }

            // 2) Detect events for this motion (previous sample -> current sample)
            var evt = DetectSessionEvents(geoTelemetryData);
            if (evt != null)
                RegisterEvent(evt, geoTelemetryData.Timestamp);

            // 3) Update last-known telemetry and position timestamps for the active session
            _previousTelemetryData = geoTelemetryData;
            if (_currentDriverSessionData != null)
            {
                _currentDriverSessionData.LastPosition = geoTelemetryData;
                _currentDriverSessionData.LastPositionTS = geoTelemetryData.Timestamp;
            }

            // 4) Session idle timeout: if no events for _sessionTimeout, close the session.
            //    Reference is telemetry time (not wall-clock), so replayed recordings behave like live ones.
            if (_currentDriverSessionData != null)
            {
                var lastTs = _currentDriverSessionData.LastEvent?.Timestamp ?? _currentSessionStart;
                if (lastTs + _sessionTimeout < geoTelemetryData.Timestamp)
                    EndCurrentSession();
            }
        }
    }

    private void EndCurrentSession()
    {
        if (_currentDriverSessionData == null) return;
        var ended = _currentDriverSessionData;
        _currentDriverSessionData = null;
        OnDriverSessionEnded(ended);
    }

    /// <summary>
    /// Create a new current session bound to the current circuit and identity.
    /// Raises <see cref="DriverSessionStarted"/>.
    /// </summary>
    private DeviceSessionData CreateSession(DateTimeOffset telemetryTimestamp)
    {
        var session = new DeviceSessionData
        {
            Id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            CircuitCode = _circuit?.Code,
            Generator = "LapViz",
            Version = Version.ToString(),
            DeviceId = _deviceId,
            Driver = { Id = _driverId, Name = _driverId }
        };

        _driverSessions.Add(session);
        _currentDriverSessionData = session;
        _currentSessionStart = telemetryTimestamp;
        OnDriverSessionStarted(session);
        return session;
    }

    /// <summary>
    /// Clears all sessions and resets current state. Useful when you want to start fresh.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _driverSessions.Clear();
            _currentDriverSessionData = null;
            _previousTelemetryData = null;
            _circuit = null;
            _lastCircuitCheck = DateTimeOffset.MinValue;
            _currentSessionStart = default;
        }
    }

    /// <summary>
    /// Detects a sector (and possibly lap) event based on the line from the previous fix to the current fix.
    /// Returns null if no boundary is crossed or if detection is throttled by timeout rules.
    /// </summary>
    private SessionDataEvent DetectSessionEvents(GeoTelemetryData current)
    {
        if (_previousTelemetryData == null || _circuit == null)
            return null;

        // Throttle sector detection close in time to avoid double-triggers on noisy signals
        if (IsInDetectionTimeout(current))
            return null;

        var sectorEvent = SessionEventDetection.DetectSectorCrossing(_circuit, _previousTelemetryData, current);
        if (sectorEvent != null)
        {
            sectorEvent.UserId = _driverId;
            sectorEvent.DeviceId = _deviceId;
            return sectorEvent;
        }

        // Optional: position breadcrumb every ~1s, independent of sectors (only if enabled)
        if (_trackPosition && _currentDriverSessionData != null)
        {
            var lastTs = _currentDriverSessionData.LastPositionTS;
            if (lastTs == default(DateTimeOffset) || (lastTs + TimeSpan.FromSeconds(1) < current.Timestamp))
            {
                return new SessionDataEvent
                {
                    Timestamp = current.Timestamp,
                    Type = SessionEventType.Position,
                    FirstGeoCoordinates = _currentDriverSessionData.LastPosition != null
                        ? new GeoCoordinates
                        {
                            Latitude = _currentDriverSessionData.LastPosition.Latitude,
                            Longitude = _currentDriverSessionData.LastPosition.Longitude
                        }
                        : null,
                    SecondGeoCoordinates = new GeoCoordinates
                    {
                        Latitude = current.Latitude,
                        Longitude = current.Longitude
                    },
                    UserId = _currentDriverSessionData.Driver?.Id,
                    DeviceId = _currentDriverSessionData.DeviceId
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Adds the event to the current session (creating a session if needed),
    /// computes its lap number and per-event time delta, and if this completes
    /// a lap, also emits a Lap event.
    /// </summary>
    private void RegisterEvent(SessionDataEvent sessionDataEvent, DateTimeOffset telemetryTimestamp)
    {
        // Ensure we have an active session
        var session = _currentDriverSessionData ?? CreateSession(telemetryTimestamp);
        SessionEventDetection.Register(session, _circuit, sessionDataEvent, OnSessionEventAdded);
    }

    /// <summary>
    /// Returns true if we should suppress detection because another sector event fired too recently.
    /// Updates last position while we wait.
    /// </summary>
    private bool IsInDetectionTimeout(GeoTelemetryData current)
    {
        if (_currentDriverSessionData == null) return false;

        var windowSec = (_circuit?.SectorTimeout > 0) ? _circuit.SectorTimeout : _minSecondsBetweenSectors;

        var lastEvtTs = _currentDriverSessionData.LastEvent?.Timestamp ?? DateTimeOffset.MinValue;
        if (lastEvtTs + TimeSpan.FromSeconds(windowSec) > current.Timestamp)
        {
            _currentDriverSessionData.LastPosition = current;
            _currentDriverSessionData.LastPositionTS = current.Timestamp;
            return true;
        }

        return false;
    }

    public event EventHandler<CircuitConfiguration> CircuitChanged;
    protected virtual void OnCircuitChanged(CircuitConfiguration cfg)
    {
        try { CircuitChanged?.Invoke(this, cfg); }
        catch (Exception ex) { _logger.LogError(ex, "GeolocationSingleSessionManager: CircuitChanged handler failed."); }
    }

    public event EventHandler<SessionDataEvent> SessionEventAdded;
    protected virtual void OnSessionEventAdded(SessionDataEvent e)
    {
        try { SessionEventAdded?.Invoke(this, e); }
        catch (Exception ex) { _logger.LogError(ex, "GeolocationSingleSessionManager: SessionEventAdded handler failed."); }
    }

    public event EventHandler<DeviceSessionData> DriverSessionStarted;
    protected virtual void OnDriverSessionStarted(DeviceSessionData s)
    {
        try { DriverSessionStarted?.Invoke(this, s); }
        catch (Exception ex) { _logger.LogError(ex, "GeolocationSingleSessionManager: DriverSessionStarted handler failed."); }
    }

    public event EventHandler<DeviceSessionData> DriverSessionEnded;
    protected virtual void OnDriverSessionEnded(DeviceSessionData s)
    {
        try { DriverSessionEnded?.Invoke(this, s); }
        catch (Exception ex) { _logger.LogError(ex, "GeolocationSingleSessionManager: DriverSessionEnded handler failed."); }
    }
}
