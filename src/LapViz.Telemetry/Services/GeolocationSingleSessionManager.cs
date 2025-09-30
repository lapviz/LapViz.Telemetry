using System;
using System.Collections.Generic;
using System.Linq;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

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

    // Configuration
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(15);   // idle timeout to end a session
    private readonly bool _trackPosition;
    private readonly int _minSecondsBetweenSectors;
    private readonly ICircuitService _circuitService;

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
    {
        _circuitService = circuitService ?? throw new ArgumentNullException(nameof(circuitService));
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

        lock (_sync)
        {
            // 1) Circuit detection (throttled to every 2 seconds or on first call)
            bool circuitChanged = false;
            if (_circuit == null || (_lastCircuitCheck + TimeSpan.FromSeconds(2) <= geoTelemetryData.Timestamp))
            {
                circuitChanged = TryDetectCircuit(geoTelemetryData);
                _lastCircuitCheck = geoTelemetryData.Timestamp;
            }

            if (circuitChanged && _circuit != null)
            {
                // Notify listeners about circuit change and close previous session (if any)
                OnCircuitChanged(_circuit);
                if (_driverSessions.Count > 0)
                    OnDriverSessionEnded(_driverSessions[_driverSessions.Count - 1]);

                // Create a fresh session bound to the new circuit
                CreateSession();
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
                RegisterEvent(evt);

            // 3) Update last-known telemetry and position timestamps for the active session
            _previousTelemetryData = geoTelemetryData;
            if (_currentDriverSessionData != null)
            {
                _currentDriverSessionData.LastPosition = geoTelemetryData;
                _currentDriverSessionData.LastPositionTS = geoTelemetryData.Timestamp;
            }

            // 4) Session idle timeout: if no events for _sessionTimeout, close the session
            if (_currentDriverSessionData != null)
            {
                var lastTs = _currentDriverSessionData.LastEvent?.Timestamp ?? _currentDriverSessionData.CreatedDate;
                if (lastTs + _sessionTimeout < geoTelemetryData.Timestamp)
                {
                    OnDriverSessionEnded(_currentDriverSessionData);
                    _currentDriverSessionData = null;
                }
            }
        }
    }

    /// <summary>
    /// Create a new current session bound to the current circuit and identity.
    /// Raises <see cref="DriverSessionStarted"/>.
    /// </summary>
    private void CreateSession()
    {
        var session = new DeviceSessionData
        {
            Id = DateTime.Now.ToString("yyyyMMddHHmmss"),
            CircuitCode = _circuit?.Code,
            Generator = "LapViz",
            Version = Version.ToString(),
            DeviceId = _deviceId,
            Driver = { Id = _driverId, Name = _driverId }
        };

        _driverSessions.Add(session);
        _currentDriverSessionData = session;
        OnDriverSessionStarted(session);
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

        // Segment from previous point to current point
        var trajectory = new CircuitGeoLine(_previousTelemetryData, current);

        // Check crossing with each circuit segment (respect direction setting)
        foreach (var sector in _circuit.Segments)
        {
            var intersect = sector.Boundary.Intersect(
                trajectory,
                _circuit.UseDirection ? CrossingFilter.TowardApex : CrossingFilter.Any);

            if (intersect == null)
                continue;

            // Linear interpolation on the segment to adjust timestamp at crossing
            var factor = trajectory.CenterFactor(intersect);
            var dt = (current.Timestamp - _previousTelemetryData.Timestamp).TotalMilliseconds;
            var adjustedTimestamp = _previousTelemetryData.Timestamp.AddMilliseconds(dt * factor);

            // For sector numbering: when crossing segment "1" we are *ending* the previous lap/sector set.
            // Example:
            //   - Closed circuits: last sector is Segments.Count (finish line == start line)
            //   - Open circuits:   finish line is last segment; start line is Segments[0]
            int sectorNumber = (sector.Number == 1) ? _circuit.Segments.Count : (sector.Number - 1);

            var evt = new SessionDataEvent
            {
                Timestamp = adjustedTimestamp,
                Sector = sectorNumber,
                Type = SessionEventType.Sector,
                FirstGeoCoordinates = trajectory.Start,
                SecondGeoCoordinates = trajectory.End,
                UserId = _driverId,
                DeviceId = _deviceId,
                Factor = factor
            };

            return evt;
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
    private void RegisterEvent(SessionDataEvent sessionDataEvent)
    {
        // Ensure we have an active session
        if (_currentDriverSessionData == null)
            CreateSession();

        // Lap number: 0 until we first cross start line; then increment when sector 1 is crossed
        int lapNumber = (_currentDriverSessionData.LastLap == null)
            ? 0
            : _currentDriverSessionData.LastLap.LapNumber + 1;

        // Per-event time is the delta since the last event (first event -> 0)
        var time = (_currentDriverSessionData.LastEvent != null)
            ? sessionDataEvent.Timestamp - _currentDriverSessionData.LastEvent.Timestamp
            : TimeSpan.Zero;

        sessionDataEvent.Time = time;
        sessionDataEvent.LapNumber = lapNumber;
        sessionDataEvent.DriverRace = _currentDriverSessionData;
        sessionDataEvent.CircuitCode = _circuit?.Code;

        _currentDriverSessionData.AddEvent(sessionDataEvent);
        OnSessionEventAdded(sessionDataEvent);

        // Lap registration rule:
        // - CLOSED: crossing the last sector means the lap has just completed.
        // - OPEN:   crossing the (Count - 1) sector completes the lap (finish line).
        bool completesLap =
            (_circuit.Type == CircuitType.Closed && sessionDataEvent.Sector == _circuit.Segments.Count) ||
            (_circuit.Type == CircuitType.Open && sessionDataEvent.Sector == _circuit.Segments.Count - 1);

        if (completesLap)
        {
            // Lap time is delta since last lap reference:
            // - CLOSED: since previous lap's timestamp
            // - OPEN:   since last crossing of the finish segment (Segments.Count)
            TimeSpan lapTime;
            if (_currentDriverSessionData.LastLap == null)
            {
                lapTime = TimeSpan.Zero; // first time across start line
            }
            else
            {
                var reference = (_circuit.Type == CircuitType.Closed)
                    ? _currentDriverSessionData.LastLap.Timestamp
                    : _currentDriverSessionData.Events
                        .Where(x => x.Sector == _circuit.Segments.Count)
                        .Select(x => x.Timestamp)
                        .DefaultIfEmpty(sessionDataEvent.Timestamp)
                        .Last();

                lapTime = sessionDataEvent.Timestamp - reference;
            }

            var lapEvent = (SessionDataEvent)sessionDataEvent.Clone();
            lapEvent.Type = SessionEventType.Lap;
            lapEvent.Sector = 0;
            lapEvent.Time = lapTime;

            _currentDriverSessionData.AddEvent(lapEvent);
            OnSessionEventAdded(lapEvent);
        }

        _currentDriverSessionData.LastPositionTS = sessionDataEvent.Timestamp;
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

    /// <summary>
    /// Ask the circuit service to detect the current circuit given a sample.
    /// Returns true if the circuit changed.
    /// </summary>
    private bool TryDetectCircuit(GeoTelemetryData sample)
    {
        var detected = _circuitService.Detect(sample).Result;
        if (detected != null && (_circuit == null || !string.Equals(detected.Code, _circuit.Code, StringComparison.Ordinal)))
        {
            _circuit = detected;
            return true;
        }
        return false;
    }

    public event EventHandler<CircuitConfiguration> CircuitChanged;
    protected virtual void OnCircuitChanged(CircuitConfiguration cfg)
    {
        try { CircuitChanged?.Invoke(this, cfg); } catch { /* ignore */ }
    }

    public event EventHandler<SessionDataEvent> SessionEventAdded;
    protected virtual void OnSessionEventAdded(SessionDataEvent e)
    {
        try { SessionEventAdded?.Invoke(this, e); } catch { /* ignore */ }
    }

    public event EventHandler<DeviceSessionData> DriverSessionStarted;
    protected virtual void OnDriverSessionStarted(DeviceSessionData s)
    {
        try { DriverSessionStarted?.Invoke(this, s); } catch { /* ignore */ }
    }

    public event EventHandler<DeviceSessionData> DriverSessionEnded;
    protected virtual void OnDriverSessionEnded(DeviceSessionData s)
    {
        try { DriverSessionEnded?.Invoke(this, s); } catch { /* ignore */ }
    }
}
