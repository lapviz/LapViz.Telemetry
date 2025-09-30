using LapViz.LiveTiming;
using LapViz.LiveTiming.Models;
using LapViz.LiveTiming.Models.Views;
using LapViz.LiveTiming.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LapViz.Telemetry.CLI.Commands;
public sealed class LiveTimingCommand : Command<LiveTimingSettings>
{
    private readonly ILogger<LiveTimingCommand> _logger;

    private ObservableLiveTimingClient _client = new(new NullLogger<ObservableLiveTimingClient>());

    private LiveTimingDataView _view = new();
    private LiveTimingDataRankingTableView? _currentRanking;
    private CancellationTokenSource? _playbackCts;

    private readonly object _rankFlashLock = new();
    private readonly Dictionary<string, (int Delta, DateTimeOffset ExpiresAt)> _rankFlash = new();
    private const int RankFlashSeconds = 5;

    private static string MultipleDriverSessionDataCsvPath => Path.Combine(AppContext.BaseDirectory, "resources", "MultipleDriverSessionTestData.csv");

    private readonly Queue<string> _lastEvents = new Queue<string>();
    private const int MaxEventsLog = 5;

    public LiveTimingCommand(ILogger<LiveTimingCommand> logger)
    {
        _logger = logger;
    }

    public override int Execute(CommandContext context, LiveTimingSettings settings)
    {
        Console.TreatControlCAsInput = true;

        // Run and wait synchronously in Spectre command
        _logger.LogInformation("Connecting to session {SessionId}", settings.SessionId);
        var sessionId = RunInternalAsync(settings).GetAwaiter().GetResult();
        _logger.LogInformation("Connected to session {SessionId}", sessionId);

        if (settings.Test)
        {
            _playbackCts = new CancellationTokenSource();
            _ = Task.Run(() => ReplayCsvAsync(sessionId, _playbackCts.Token));
            _logger.LogInformation("Test mode enabled, CSV playback started from {CsvPath}", MultipleDriverSessionDataCsvPath);
        }

        // Idle loop until ESC, Ctrl+C, or Q
        while (true)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Escape || key == ConsoleKey.Q)
                    break;
                if (key == ConsoleKey.C && (ConsoleModifiers.Control & ConsoleModifiers.Control) == ConsoleModifiers.Control)
                    break;
            }
            Thread.Sleep(100);
        }

        // Cleanup
        try
        {
            _playbackCts?.Cancel();
        }
        catch { }

        try { _client.Dispose(); } catch { }
        AnsiConsole.Clear();
        return 0;
    }

    private async Task<string> RunInternalAsync(LiveTimingSettings options)
    {
        _client = new ObservableLiveTimingClient(new NullLogger<ObservableLiveTimingClient>());

        _client.SessionEventReceived += Client_SessionEventReceived;
        _client.BoardUpdated += Client_BoardUpdated;
        _client.DeviceInfoUpdated += Client_DeviceInfoUpdated;

        await _client.ConnectAsync(options.Hub).ConfigureAwait(false);
        _logger.LogInformation("Connected to hub {Hub}", options.Hub);

        if (!string.IsNullOrWhiteSpace(options.SessionId))
        {
            await _client.JoinSession(options.SessionId!, null).ConfigureAwait(false);
            return options.SessionId!;
        }
        else
        {
            var createdSessionId = await _client.CreateSession(new SessionCreateRequestDto()).ConfigureAwait(false);
            await _client.JoinSession(createdSessionId, null).ConfigureAwait(false);
            return createdSessionId;
        }
    }

    private void Client_DeviceInfoUpdated(object? sender, DeviceInfoDto e)
    {
        _logger.LogDebug("Device info updated: {DeviceId}", e.DeviceId);
        UpdateView();
    }

    private void Client_BoardUpdated(object? sender, SessionDataDto e)
    {
        _logger.LogDebug("Board updated for session {SessionId}", e.Id);
        _view = e.ToLiveTimingView();
        UpdateView();
    }

    private void Client_SessionEventReceived(object? sender, SessionDataDeviceDto e)
    {
        _logger.LogDebug("Event received from device {DeviceId}", e.DeviceId);
        foreach (var ev in e.Events)
        {
            var logLine = $"{DateTime.Now:HH:mm:ss} | {e.DeviceId} | {ev.Type} | Lap {ev.LapNumber} | Sector {ev.SectorNumber} | {ev.Time.TotalMilliseconds:F0} ms";
            lock (_lastEvents)
            {
                _lastEvents.Enqueue(logLine);
                while (_lastEvents.Count > MaxEventsLog)
                    _lastEvents.Dequeue();
            }
        }

        if (_client.Views.TryGetValue(e.SessionId, out var v))
            _view = v;
        UpdateView();
    }

    private void UpdateView()
    {
        try
        {
            _currentRanking = _view.GetRanking(LiveTimingDataRankingType.Qualifying, _currentRanking);

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn("#").LeftAligned());
            table.AddColumn(new TableColumn("Change").Centered());
            table.AddColumn(new TableColumn("Device").LeftAligned());
            table.AddColumn(new TableColumn("Driver").LeftAligned());
            table.AddColumn(new TableColumn("Laps").LeftAligned());

            for (int s = 1; s <= _currentRanking.Sectors; s++)
                table.AddColumn(new TableColumn($"Sector {s}").Centered());

            table.AddColumn(new TableColumn("Last Lap").Centered());
            table.AddColumn(new TableColumn("Best Lap").Centered());
            table.AddColumn(new TableColumn("Gap").Centered());
            table.AddColumn(new TableColumn("Interval").Centered());

            foreach (var row in _currentRanking.Rows)
            {
                var cells = new List<string>
                {
                    row.Rank.ToString(),
                    GetRankChangeString(row.DeviceId, row.RankChange),
                    row.DeviceId,
                    row.DisplayName,
                    row.LastLap != null && row.LastLap.Event != null ? row.LastLap.Event.Lap.ToString() : "0"
                };

                for (int s = 1; s <= _currentRanking.Sectors; s++)
                {
                    if (row.Sectors.TryGetValue(s, out var sector))
                        cells.Add($"[{HexConverter(sector.Color)}]{sector}[/]");
                    else
                        cells.Add(string.Empty);
                }

                cells.Add($"[{HexConverter(row.LastLap.Color)}]{row.LastLap}[/]");
                cells.Add($"[{HexConverter(row.BestLap.Color)}]{row.BestLap}[/]");
                cells.Add(FormattingHelper.GetFormattedTime(row.Gap, "-", true));
                cells.Add(FormattingHelper.GetFormattedTime(row.Interval, "-", true));

                table.AddRow(cells.ToArray());
            }

            AnsiConsole.Clear();
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine($"Session ID: {_view.SessionId} | Generated in {_currentRanking.Duration} ms");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold underline]Last events:[/]");
            lock (_lastEvents)
            {
                foreach (var ev in _lastEvents)
                    AnsiConsole.MarkupLine($"[grey]{ev}[/]");
            }

            AnsiConsole.WriteLine("Press ESC or Q to quit");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while updating view");
        }
    }

    private static string HexConverter(System.Drawing.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private string GetRankChangeString(string deviceId, int? currentDelta)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_rankFlashLock)
        {
            // Si un nouveau changement est détecté, on met a jour le cache et on redémarre le timer.
            if (currentDelta.HasValue && currentDelta.Value != 0)
            {
                _rankFlash[deviceId] = (currentDelta.Value, now.AddSeconds(RankFlashSeconds));
            }

            // Si on a un flash en cours pour ce device et qu’il n’est pas expiré, on l’affiche.
            if (_rankFlash.TryGetValue(deviceId, out var state))
            {
                if (state.ExpiresAt > now)
                {
                    var delta = state.Delta;
                    if (delta > 0) return "[green]█[/] +" + delta;
                    if (delta < 0) return "[maroon]█[/] " + delta;
                }
                else
                {
                    // Expiré, on nettoie pour éviter la croissance du dictionnaire.
                    _rankFlash.Remove(deviceId);
                }
            }
        }

        // Par défaut, pas de flash actif.
        return "[silver]█[/]       ";
    }

    private static SessionDeviceEventDto Ev(
    string id,
    SessionEventTypeDto type,
    int lap,
    int sector,
    DateTimeOffset baseTs,
    double ms,
    bool deleted = false)
    => new SessionDeviceEventDto
    {
        Id = id,
        Type = type,
        LapNumber = lap,
        SectorNumber = sector,
        Time = TimeSpan.FromMilliseconds(ms),
        Timestamp = baseTs.AddMilliseconds(ms),
        Deleted = deleted ? DateTime.UtcNow : (DateTime?)null
    };

    private static SessionDataDeviceDto DeviceDto(
        string deviceId,
        string displayName,
        string category,
        IEnumerable<SessionDeviceEventDto> events)
        => new SessionDataDeviceDto
        {
            DeviceId = deviceId,
            DisplayName = displayName,
            Category = category,
            Type = DeviceTypeDto.LapTimer,
            Events = events.ToList()
        };

    private async Task ReplayCsvAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(MultipleDriverSessionDataCsvPath))
            {
                _logger.LogWarning("CSV not found at {Path}", MultipleDriverSessionDataCsvPath);
                return;
            }

            // First load all events to determine t0
            var events = new List<(string deviceId,
                                   long tsMs,
                                   SessionEventTypeDto dtoType,
                                   int lap,
                                   int sector,
                                   TimeSpan timeFull,
                                   bool isPB,
                                   bool isBO)>();

            // In the tests, "start" is a base DateTime offset for calculating ts.
            // Here we're just manipulating offsets in milliseconds, t0 will be used as a reference.
            using (var reader = new StreamReader(MultipleDriverSessionDataCsvPath))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("DeviceId", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = line.Split(';');
                    if (parts.Length != 9)
                        continue;

                    var deviceId = parts[0];
                    var tsMs = Convert.ToInt64(parts[1]); // offset in ms from file reference "start
                    var dtoType = parts[2] == "Lap" ? SessionEventTypeDto.Lap : SessionEventTypeDto.Sector;
                    var lapNumber = Convert.ToInt32(parts[3]);
                    var sectorNumber = Convert.ToInt32(parts[4]);

                    // TimeFull is "ticks/10000", i.e. milliseconds * 1000 * 10
                    var timeFull = TimeSpan.FromMilliseconds(Convert.ToInt64(parts[5]) / 10000d);

                    var isPersonnalBest = parts[7] != "False";
                    var isBestOverall = parts[8] != "False";

                    events.Add((deviceId, tsMs, dtoType, lapNumber, sectorNumber, timeFull, isPersonnalBest, isBestOverall));
                }
            }

            if (events.Count == 0)
            {
                _logger.LogWarning("CSV contains no playable events.");
                return;
            }

            // t0 is used as a synchronization reference, and is replayed respecting the deltas (ts - t0).
            var t0 = events.Min(e => e.tsMs);
            var start = DateTimeOffset.UtcNow;

            var deviceOffsets = events
                .Select(e => e.deviceId)
                .Distinct()
                .OrderBy(id => id) // ordre stable
                .Select((id, idx) => new { id, offset = TimeSpan.FromSeconds(5 * idx) })
                .ToDictionary(x => x.id, x => x.offset);

            // Sort by timestamp to replay in order
            events.Sort((a, b) => a.tsMs.CompareTo(b.tsMs));

            foreach (var e in events)
            {
                ct.ThrowIfCancellationRequested();

                var deltaMs = e.tsMs - t0;
                var deviceOffset = deviceOffsets.TryGetValue(e.deviceId, out var off) ? off : TimeSpan.Zero;
                var dueAt = start.AddMilliseconds(deltaMs).Add(deviceOffset);
                var delay = dueAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                // Reconstruct the base for the event:
                // Ev(...) constructs Timestamp = base + duration, so to obtain a Timestamp "ts",
                // we give a base of (ts - timeFull).
                var tsAbsolute = start.AddMilliseconds(deltaMs).Add(deviceOffset);
                var baseForEvent = tsAbsolute.AddMilliseconds(-e.timeFull.TotalMilliseconds);

                // Building a 1-event "device events" DTO, as in your test
                var dto = DeviceDto(
                    e.deviceId,
                    e.deviceId,
                    string.Empty,
                    new[]
                    {
                    Ev(Guid.NewGuid().ToString(),
                       e.dtoType,
                       e.lap,
                       e.sector,
                       baseForEvent,
                       e.timeFull.TotalMilliseconds // duration ms
                    ),
                    });

                // Make sure SessionId is present if necessary
                dto.SessionId = sessionId;

                _client.AddEventData(dto);
            }

            _logger.LogInformation("CSV playback completed.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CSV playback canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CSV playback.");
        }
    }
}
