using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Thread-safety: all public members are synchronized, so fixes can be fed from a background thread.
/// Event handlers are invoked on the feeding thread while the internal lock is held.
/// </summary>
public class LapTimerService : ILapTimer
{
    private readonly ILogger<LapTimerService> _logger;
    private readonly LapTimerConfig _config;
    private readonly Version _version;

    private CircuitConfiguration _circuitConfiguration;
    private DeviceSessionData _activeSession;

    // Guards all mutable state; public members can be called from any thread
    private readonly object _sync = new object();

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
    public CircuitConfiguration CircuitConfiguration
    {
        get { lock (_sync) return _circuitConfiguration; }
    }

    /// <summary>Sets/replaces the circuit. Closes any active session.</summary>
    public void SetCircuit(CircuitConfiguration circuitConfiguration)
    {
        if (circuitConfiguration == null) throw new ArgumentNullException(nameof(circuitConfiguration));
        lock (_sync)
        {
            if (_activeSession != null) CloseSession();
            _circuitConfiguration = circuitConfiguration;
        }
    }

    /// <summary>
    /// Adds one geolocation fix and performs crossing detection.
    /// </summary>
    /// <param name="geoTelemetryData">Fix (must include Timestamp, Latitude, Longitude).</param>
    /// <param name="device">Optional device id to stamp on generated events (falls back to config).</param>
    public void AddGeolocation(GeoTelemetryData geoTelemetryData, string device = null)
    {
        if (geoTelemetryData == null) return;

        lock (_sync)
        {
            if (_circuitConfiguration == null) return; // circuit not set yet
            if (_detectionPaused == 1) return;

            // Maintain a small rolling window
            var node = _telemetryData.AddLast(geoTelemetryData);
            if (_telemetryData.Count > _config.MaxTelemetryDataRetention)
                _telemetryData.RemoveFirst();

            // Store all telemetry in active session (if any)
            _activeSession?.TelemetryData.Add(geoTelemetryData);

            // Need a previous fix to build a trajectory
            if (node.Previous == null) return;

            var prev = node.Previous.Value;
            var curr = node.Value;

            // Global cooldown / sector-timeout rule
            if (!CanDetectEvent(curr)) return;

            var deviceId = string.IsNullOrWhiteSpace(device) ? _config.DeviceId : device;

            // 1) Sector crossing
            var sectorEvent = SessionEventDetection.DetectSectorCrossing(_circuitConfiguration, prev, curr);
            if (sectorEvent != null)
            {
                sectorEvent.UserId = _config.UserId;
                sectorEvent.DeviceId = deviceId;
                RegisterEvent(sectorEvent);
            }

            // 2) Optional position breadcrumb (every fix)
            if (_config.TrackPosition)
            {
                RegisterEvent(new SessionDataEvent
                {
                    Timestamp = curr.Timestamp,
                    Type = SessionEventType.Position,
                    FirstGeoCoordinates = prev,
                    SecondGeoCoordinates = curr,
                    UserId = _config.UserId,
                    DeviceId = deviceId
                });
            }

            // 3) Auto-close session on idle (only if there was at least one event)
            if (_activeSession != null &&
                _activeSession.LastEvent != null &&
                _activeSession.LastEvent.Timestamp.Add(_config.SessionTimeout) < curr.Timestamp)
            {
                CloseSession();
            }
        }
    }

    /// <summary>Adds the event to the active session (creating one if needed) and derives lap events.</summary>
    private void RegisterEvent(SessionDataEvent sessionEvent)
    {
        var session = _activeSession ?? CreateSession();
        SessionEventDetection.Register(session, _circuitConfiguration, sessionEvent, OnEventAdded);
    }

    /// <summary>Creates a new active session bound to the current circuit.</summary>
    public DeviceSessionData CreateSession()
    {
        lock (_sync)
        {
            if (_circuitConfiguration == null)
                throw new InvalidOperationException("Circuit must be set before creating a session.");

            var now = DateTimeOffset.UtcNow; // use UTC for consistency
            var session = new DeviceSessionData
            {
                Id = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
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
    }

    /// <summary>Closes and returns the active session.</summary>
    public DeviceSessionData CloseSession()
    {
        lock (_sync)
        {
            var stopped = _activeSession;
            if (stopped != null)
                OnSessionEnded(stopped);

            _activeSession = null;
            _telemetryData.Clear();
            return stopped;
        }
    }

    public void StopDetection()
    {
        lock (_sync)
        {
            Interlocked.Exchange(ref _detectionPaused, 1);
            if (_activeSession != null)
                OnSessionPaused(_activeSession);
        }
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

        var cooldown = _circuitConfiguration.SectorTimeout > 0
            ? TimeSpan.FromSeconds(_circuitConfiguration.SectorTimeout)
            : _config.MinimumTimeBetweenEvents;

        if (_activeSession != null && _activeSession.LastEvent != null &&
            _activeSession.LastEvent.Timestamp + cooldown > current.Timestamp)
        {
            _activeSession.LastPosition = current;
            return false;
        }

        return true;
    }

    #region ILapTimer members

    public DeviceSessionData ActiveSession
    {
        get { lock (_sync) return _activeSession; }
    }

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
