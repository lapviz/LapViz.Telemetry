using LapViz.LiveTiming;
using LapViz.LiveTiming.Models;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;
using LapViz.Telemetry.Sensors;
using LapViz.Telemetry.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console.Cli;

namespace LapViz.Telemetry.CLI.Commands;

public sealed class LaptimerCommand : Command<LaptimerSettings>
{
    private ILapTimer? _lapTimerService;
    private ITelemtrySensor? _telemetry;
    private ObservableLiveTimingClient? _liveTimingClient;

    private string? _sessionId;
    private GeoTelemetryData? _lastLocation;

    private readonly object _sessionBestSectorsLock = new();
    private readonly Dictionary<int, TimeSpan> _sessionBestSectors = new();

    private readonly object _sessionBestLapLock = new();
    private TimeSpan? _sessionBestLap;

    private readonly LaptimerKonsoleUi _ui = new();

    public override int Execute(CommandContext context, LaptimerSettings options)
    {
        _ui.Initialize();
        ConfigureAndStartLaptimer(options);

        do
        {
            while (!Console.KeyAvailable)
                Task.Delay(500).Wait();
        }
        while (Console.ReadKey(true).Key != ConsoleKey.Escape);

        StopLaptimer();
        _ui.Close();
        Task.Delay(2000).Wait();
        return 0;
    }

    private async void ConfigureAndStartLaptimer(LaptimerSettings options)
    {
        Console.CursorVisible = false;

        var circuitService = new StaticCircuitService();
        var circuit = circuitService.GetByCode("genk").Result;
        _telemetry = new SimulatorGps();

        if (!string.IsNullOrWhiteSpace(options.SessionFile))
        {
            ITelemetryDataReader reader = TelemetryDataReaderFactory.Instance.GetReader(options.SessionFile);
            var driverSessionData = reader.Load(options.SessionFile).GetSessionData().First();

            _telemetry = new SimulatorGps(driverSessionData, 0, 0);

            // Detect circuit
            foreach (var telemetryData in driverSessionData.TelemetryData)
            {
                circuit = await circuitService.Detect(telemetryData as GeoTelemetryData);
                if (circuit != null) break;
            }
            if (circuit == null) throw new Exception("Failed to detect circuit from data");
        }

        _telemetry.DataReceived += (o, i) => { _lastLocation = i.Message; _lapTimerService!.AddGeolocation(i.Message); };

        _liveTimingClient = new ObservableLiveTimingClient(new NullLogger<ObservableLiveTimingClient>());
        _liveTimingClient.SessionEventReceived += LiveTimingClient_SessionEventReceived;

        var config = new LapTimerConfig() { DeviceId = options.DeviceId };

        _lapTimerService = new LapTimerService(null, config);
        _lapTimerService.SetCircuit(circuit);
        _lapTimerService.EventAdded += (o, i) => HandleRaceEvents(i);
        _lapTimerService.Error += (o, i) => HandleError(i);

        // Start everything
        _telemetry.Start();
        _lapTimerService.CreateSession();

        await _liveTimingClient.ConnectAsync(options.Hub).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(options.SessionId))
        {
            _sessionId = options.SessionId;
            await _liveTimingClient.JoinSession(_sessionId, null).ConfigureAwait(true);
            _ui.Log("Joined session: " + _sessionId);
        }
        else if (options.CreateSession)
        {
            _sessionId = await _liveTimingClient.CreateSession(new SessionCreateRequestDto()
            {
                CircuitCode = circuit.Code
            }).ConfigureAwait(true);
            _ui.Log("Created session: " + _sessionId);
            await _liveTimingClient.JoinSession(_sessionId, null).ConfigureAwait(true);
            _ui.Log("Joined session: " + _sessionId);
        }

        _ = Run(() => ReportStatus(), TimeSpan.FromMilliseconds(100), CancellationToken.None);
    }

    public static async Task Run(Action action, TimeSpan period, CancellationToken cancellationToken)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));

        var next = DateTimeOffset.UtcNow + period;

        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = next - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            else
            {
                // If we're late, execute immediately and reschedule
            }

            if (cancellationToken.IsCancellationRequested) break;

            try { action(); }
            catch { /* swallow to keep scheduling alive */ }

            // Advance by exactly 'period' to reduce drift (fixed-rate scheduling)
            next = next + period;
        }
    }

    private void LiveTimingClient_SessionEventReceived(object? sender, SessionDataDeviceDto e)
    {
        foreach (var eventData in e.Events)
        {
            if (eventData.Time == TimeSpan.Zero) continue;

            if (eventData.Type == SessionEventTypeDto.Lap)
            {
                lock (_sessionBestLapLock)
                {
                    if (!_sessionBestLap.HasValue || _sessionBestLap.Value > eventData.Time)
                    {
                        _sessionBestLap = eventData.Time;
                        UpdateIndicators(_lastSessionEvent);
                        _ui.Log($"Event from [{e.DeviceId}] received and is best: " + eventData);
                    }
                    else
                    {
                        _ui.Log($"Event from [{e.DeviceId}] received: " + eventData);
                    }
                }
            }
            else if (eventData.Type == SessionEventTypeDto.Sector)
            {
                lock (_sessionBestSectorsLock)
                {
                    if (!_sessionBestSectors.ContainsKey(eventData.SectorNumber) || _sessionBestSectors[eventData.SectorNumber] > eventData.Time)
                    {
                        _sessionBestSectors[eventData.SectorNumber] = eventData.Time;
                        UpdateIndicators(_lastSessionEvent);
                        _ui.Log($"Event from [{e.DeviceId}] received and is best: " + eventData);
                    }
                    else
                    {
                        _ui.Log($"Event from [{e.DeviceId}] received: " + eventData);
                    }
                }
            }
        }
    }

    private void ReportStatus()
    {
        if (_lapTimerService?.ActiveSession == null)
            return;

        if (_lapTimerService.ActiveSession.LastLap != null)
        {
            _ui.UpdateCurrentLap(
                _lapTimerService.ActiveSession.LastLap.LapNumber + 1,
                _lapTimerService.ActiveSession.CurrentLapTime.ToString("m\\:ss\\.fff"));
        }

        string locationString = _lastLocation != null ? $"{_lastLocation.Latitude};{_lastLocation.Longitude}" : "?";
        var textSummary =
            $"CR:{_lapTimerService.ActiveSession.CircuitConfiguration.Name} " +
            $"TS:{_telemetry?.State} " +
            $"RX:{_telemetry?.MessagesReceived} " +
            $"TX:{_liveTimingClient?.MessagesSent}({_liveTimingClient?.QueueSize}) " +
            $"ER:{_telemetry?.Errors} " +
            $"AC:{_lastLocation?.Accuracy} " +
            $"LC:{locationString} " +
            $"SE:{_liveTimingClient?.JoinedSessions?.FirstOrDefault().Key}";

        _ui.UpdateStatus(textSummary);
    }

    private SessionDataEvent? _lastSessionEvent;

    private void HandleRaceEvents(ITelemetryData evt)
    {
        try
        {
            if (evt is not SessionDataEvent sessionEvent) return;
            _lastSessionEvent = sessionEvent;

            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                sessionEvent.SessionId = _sessionId;
                _liveTimingClient?.AddEventData(sessionEvent.ToSessionEventDto());
            }

            UpdateIndicators(sessionEvent);
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private void UpdateIndicators(SessionDataEvent sessionEvent)
    {
        if (sessionEvent == null)
            return;

        if (sessionEvent.Type == SessionEventType.Sector)
        {
            TimeSpan? bestSectorTime = _sessionBestSectors.ContainsKey(sessionEvent.Sector)
                ? _sessionBestSectors[sessionEvent.Sector]
                : null;

            if (sessionEvent.Time > TimeSpan.Zero)
            {
                var bestSecText = "--";
                var bestSecColor = ConsoleColor.White;

                if (sessionEvent.DriverRace.BestSectors.TryGetValue(sessionEvent.Sector, out var bestSec) && bestSec != null)
                {
                    bestSecText = bestSec.Time.ToString("m\\:ss\\.fff");
                    bestSecColor = GetConsoleColorFromRaceEvent(bestSec, bestSectorTime);
                }

                _ui.UpdateSectors(
                    sessionEvent.Time.ToString("m\\:ss\\.fff"),
                    GetConsoleColorFromRaceEvent(sessionEvent, bestSectorTime),
                    bestSecText,
                    bestSecColor);
            }
        }
        else if (sessionEvent.Type == SessionEventType.Lap)
        {
            var last = sessionEvent.DriverRace.LastLap;
            var best = sessionEvent.DriverRace.BestLap;

            if (last != null && last.Time > TimeSpan.Zero)
            {
                _ui.UpdateLapTimes(
                    last.Time.ToString("m\\:ss\\.fff"),
                    GetConsoleColorFromRaceEvent(last, _sessionBestLap),
                    best != null ? best.Time.ToString("m\\:ss\\.fff") : "--",
                    best != null ? GetConsoleColorFromRaceEvent(best, _sessionBestLap) : ConsoleColor.White);
            }
        }
    }

    private void HandleError(Exception i) => _ui.Log($"ERROR: {i}");

    private void StopLaptimer()
    {
        try { _telemetry?.Stop(); } catch { }
        try { _lapTimerService?.CloseSession(); } catch { }
        try { _ = _liveTimingClient?.DisconnectAsync(); } catch { }
    }

    private ConsoleColor GetConsoleColorFromRaceEvent(SessionDataEvent raceEvent, TimeSpan? bestEvent)
    {
        if (!bestEvent.HasValue || bestEvent >= raceEvent.Time || raceEvent.IsPersonnalBest)
            return ConsoleColor.Magenta;

        if (raceEvent.IsBestOverall)
            return ConsoleColor.Green;

        return ConsoleColor.Yellow;
    }
}
