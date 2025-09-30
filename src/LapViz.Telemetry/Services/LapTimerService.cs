using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.Services;

/// <summary>
/// Takes geolocation fixes and a circuit configuration as input and produces a session
/// made of sector/lap (and optional position) events.
/// </summary>
public class LapTimerService : ILapTimer
{
    private readonly ILogger<LapTimerService> _logger;
    private readonly LapTimerConfig _config;
    private readonly Version _version;

    private CircuitConfiguration _circuitConfiguration;
    private DeviceSessionData _activeSession;

    // 0 = running, 1 = paused (use Interlocked)
    private int _detectionPaused = 1;

    // Keep a short history so we can build a trajectory between last & current point
    private readonly LinkedList<GeoTelemetryData> _telemetryData = new LinkedList<GeoTelemetryData>();

    public LapTimerService(ILogger<LapTimerService> logger, LapTimerConfig lapTimerServiceConfig)
    {
        _logger = logger ?? new NullLogger<LapTimerService>();
        _config = lapTimerServiceConfig ?? throw new ArgumentNullException(nameof(lapTimerServiceConfig));
        _detectionPaused = _config.AutoStartDetection ? 0 : 1;

        _version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    }

    /// <summary>True when detection is running (not paused).</summary>
    public bool IsRunning => _detectionPaused != 1;

    /// <summary>Current circuit used for event detection.</summary>
    public CircuitConfiguration CircuitConfiguration => _circuitConfiguration;

    /// <summary>Sets/replaces the circuit. Closes any active session.</summary>
    public void SetCircuit(CircuitConfiguration circuitConfiguration)
    {
        if (circuitConfiguration == null) throw new ArgumentNullException(nameof(circuitConfiguration));
        if (_activeSession != null) CloseSession();
        _circuitConfiguration = circuitConfiguration;
    }

    /// <summary>
    /// Adds one geolocation fix and performs crossing detection.
    /// </summary>
    /// <param name="geoTelemetryData">Fix (must include Timestamp, Latitude, Longitude).</param>
    /// <param name="device">Optional device id to stamp on generated events (falls back to config).</param>
    public void AddGeolocation(GeoTelemetryData geoTelemetryData, string device = null)
    {
        if (geoTelemetryData == null) return;
        if (_circuitConfiguration == null) return; // circuit not set yet
        if (_detectionPaused == 1) return;

        // Maintain a small rolling window
        var node = _telemetryData.AddLast(geoTelemetryData);
        if (_telemetryData.Count > _config.MaxTelemetryDataRetention)
            _telemetryData.RemoveFirst();

        // Store all telemetry in active session (if any)
        if (_activeSession != null)
            _activeSession.TelemetryData.Add(geoTelemetryData);

        // Need a previous fix to build a trajectory
        if (node.Previous == null) return;

        var prev = node.Previous.Value;
        var curr = node.Value;

        // Global cooldown / sector-timeout rule
        if (!CanDetectEvent(curr)) return;

        var trajectory = new CircuitGeoLine(prev, curr);

        // 1) Sector crossings
        foreach (var sector in _circuitConfiguration.Segments)
        {
            var intersect = sector.Boundary.Intersect(
                trajectory,
                _circuitConfiguration.UseDirection ? CrossingFilter.TowardApex : CrossingFilter.Any);

            if (intersect == null) continue;

            // Interpolate timestamp at boundary crossing
            var factor = trajectory.CenterFactor(intersect);
            var dtMs = (curr.Timestamp - prev.Timestamp).TotalMilliseconds;
            var adjustedTs = prev.Timestamp.AddMilliseconds(dtMs * factor);

            // Convert to "completed sector number" on the previous lap
            var sectorNumber = sector.Number == 1
                ? _circuitConfiguration.Segments.Count
                : sector.Number - 1;

            var sectorEvent = CreateSectorEvent(adjustedTs, sectorNumber, trajectory, factor, device);
            RegisterEvent(sectorEvent);

            if (_activeSession != null)
            {
                _activeSession.TelemetryData.Add(geoTelemetryData);
            }
        }

        // 2) Optional position breadcrumb (every fix)
        if (_config.TrackPosition)
        {
            var positionEvent = CreatePositionEvent(curr.Timestamp, trajectory, device);
            RegisterEvent(positionEvent);
        }

        // 3) Auto-close session on idle (only if there was at least one event)
        if (_activeSession != null &&
            _activeSession.LastEvent != null &&
            _activeSession.LastEvent.Timestamp.Add(_config.SessionTimeout) < curr.Timestamp)
        {
            CloseSession();
        }
    }

    private SessionDataEvent CreateSectorEvent(
        DateTimeOffset timestamp,
        int sectorNumber,
        CircuitGeoLine trajectory,
        double factor,
        string deviceOverride)
    {
        return new SessionDataEvent
        {
            Timestamp = timestamp,
            Sector = sectorNumber,
            Type = SessionEventType.Sector,
            FirstGeoCoordinates = trajectory.Start,
            SecondGeoCoordinates = trajectory.End,
            UserId = _config.UserId,
            DeviceId = string.IsNullOrWhiteSpace(deviceOverride) ? _config.DeviceId : deviceOverride,
            Factor = factor
        };
    }

    private SessionDataEvent CreatePositionEvent(
        DateTimeOffset timestamp,
        CircuitGeoLine trajectory,
        string deviceOverride)
    {
        return new SessionDataEvent
        {
            Timestamp = timestamp,
            Type = SessionEventType.Position,
            FirstGeoCoordinates = trajectory.Start,
            SecondGeoCoordinates = trajectory.End,
            UserId = _config.UserId,
            DeviceId = string.IsNullOrWhiteSpace(deviceOverride) ? _config.DeviceId : deviceOverride
        };
    }

    /// <summary>Adds the event to the active session (creating one if needed) and derives lap events.</summary>
    private void RegisterEvent(SessionDataEvent sessionEvent)
    {
        if (_activeSession == null)
            CreateSession();

        // Lap number: 0 until first pass across start/finish
        var lapNumber = _activeSession.LastLap == null ? 0 : _activeSession.LastLap.LapNumber + 1;

        // Per-event sector time (first event → zero)
        var delta = _activeSession.LastEvent != null
            ? sessionEvent.Timestamp - _activeSession.LastEvent.Timestamp
            : TimeSpan.Zero;

        sessionEvent.Time = delta;
        sessionEvent.LapNumber = lapNumber;
        sessionEvent.DriverRace = _activeSession;
        sessionEvent.CircuitCode = _circuitConfiguration.Code;

        // Mark bestness w.r.t current session sector bests
        sessionEvent.IsBestOverall = _activeSession.IsBestSector(sessionEvent);

        _activeSession.AddEvent(sessionEvent);
        OnEventAdded(sessionEvent);

        // If this crossing completes the lap, create the LAP event
        bool completesLap =
            (_circuitConfiguration.Type == CircuitType.Closed && sessionEvent.Sector == _circuitConfiguration.Segments.Count) ||
            (_circuitConfiguration.Type == CircuitType.Open && sessionEvent.Sector == _circuitConfiguration.Segments.Count - 1);

        if (completesLap)
        {
            TimeSpan lapTime;
            if (_activeSession.LastLap == null)
            {
                lapTime = TimeSpan.Zero; // first time across start line
            }
            else
            {
                // Closed: from last lap timestamp. Open: from last crossing of finish (Segments.Count)
                var reference = (_circuitConfiguration.Type == CircuitType.Closed)
                    ? _activeSession.LastLap.Timestamp
                    : _activeSession.Events
                        .Where(x => x.Sector == _circuitConfiguration.Segments.Count)
                        .Select(x => x.Timestamp)
                        .DefaultIfEmpty(sessionEvent.Timestamp)
                        .Last();

                lapTime = sessionEvent.Timestamp - reference;
            }

            var lapEvent = (SessionDataEvent)sessionEvent.Clone();
            lapEvent.Type = SessionEventType.Lap;
            lapEvent.Sector = 0;
            lapEvent.Time = lapTime;

            if (lapEvent.Time != TimeSpan.Zero)
                lapEvent.IsBestOverall = _activeSession.BestLap == null || _activeSession.BestLap.Time >= lapEvent.Time;

            _activeSession.AddEvent(lapEvent);
            OnEventAdded(lapEvent);
        }

        _activeSession.LastPositionTS = sessionEvent.Timestamp;
    }

    /// <summary>Creates a new active session bound to the current circuit.</summary>
    public DeviceSessionData CreateSession()
    {
        if (_circuitConfiguration == null)
            throw new InvalidOperationException("Circuit must be set before creating a session.");

        var now = DateTimeOffset.UtcNow; // use UTC for consistency
        var session = new DeviceSessionData
        {
            Id = now.ToString("yyyyMMddHHmmss"),
            CircuitCode = _circuitConfiguration.Code,
            Generator = "LapViz.LapTimer.Service",
            Version = _version.ToString(),
            CircuitConfiguration = _circuitConfiguration,
            CreatedDate = now.UtcDateTime
        };

        _activeSession = session;
        OnSessionStarted(session);
        Interlocked.Exchange(ref _detectionPaused, 0);
        return session;
    }

    /// <summary>Closes and returns the active session.</summary>
    public DeviceSessionData CloseSession()
    {
        var stopped = _activeSession;
        if (stopped != null)
            OnSessionEnded(stopped);

        _activeSession = null;
        _telemetryData.Clear();
        return stopped;
    }

    public void StopDetection()
    {
        Interlocked.Exchange(ref _detectionPaused, 1);
        if (_activeSession != null)
            OnSessionPaused(_activeSession);
    }

    public void StartDetection()
    {
        Interlocked.Exchange(ref _detectionPaused, 0);
    }

    /// <summary>
    /// Global cooldown before considering another sector/position event.
    /// Uses circuit.SectorTimeout if set; otherwise falls back to config.MinimumTimeBetweenEvents.
    /// </summary>
    private bool CanDetectEvent(GeoTelemetryData current)
    {
        if (_circuitConfiguration == null) return false;

        var seconds = _circuitConfiguration.SectorTimeout > 0
            ? _circuitConfiguration.SectorTimeout
            : (int)_config.MinimumTimeBetweenEvents.TotalSeconds;

        if (_activeSession != null && _activeSession.LastEvent != null &&
            _activeSession.LastEvent.Timestamp.AddSeconds(seconds) > current.Timestamp)
        {
            _activeSession.LastPosition = current;
            return false;
        }

        return true;
    }

    #region ILapTimer members

    public DeviceSessionData ActiveSession => _activeSession;

    public event EventHandler<SessionDataEvent> EventAdded;
    protected virtual void OnEventAdded(SessionDataEvent e)
    {
        try { EventAdded?.Invoke(this, e); }
        catch (Exception ex) { _logger.LogError(ex, "LapTimerService: EventAdded handler failed."); }
    }

    public event EventHandler<DeviceSessionData> SessionStarted;
    protected virtual void OnSessionStarted(DeviceSessionData e)
    {
        try { SessionStarted?.Invoke(this, e); }
        catch (Exception ex) { _logger.LogError(ex, "LapTimerService: SessionStarted handler failed."); }
    }

    public event EventHandler<DeviceSessionData> SessionEnded;
    protected virtual void OnSessionEnded(DeviceSessionData e)
    {
        try { SessionEnded?.Invoke(this, e); }
        catch (Exception ex) { _logger.LogError(ex, "LapTimerService: SessionEnded handler failed."); }
    }

    public event EventHandler<DeviceSessionData> SessionPaused;
    protected virtual void OnSessionPaused(DeviceSessionData e)
    {
        try { SessionPaused?.Invoke(this, e); }
        catch (Exception ex) { _logger.LogError(ex, "LapTimerService: SessionPaused handler failed."); }
    }

    public event EventHandler<Exception> Error;
    protected virtual void OnError(Exception e)
    {
        try { Error?.Invoke(this, e); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LapTimer failed to handle error: {message}", ex.Message);
        }
    }

    #endregion
}
