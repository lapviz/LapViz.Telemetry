using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;
using Microsoft.Extensions.Logging;

namespace LapViz.Telemetry.Sensors;

/// <summary>
/// Simulator that replays GPS telemetry either from an in-memory <see cref="DeviceSessionData"/>
/// or from an embedded CSV resource. Timestamps are time-shifted to "now" and then played back
/// in real time (with optional line skipping to simulate lower sampling rates).
///
/// Public API mirrors a sensor device:
///  - <see cref="Start"/>: begins the async worker.
///  - <see cref="Stop"/>: cancels the worker cleanly.
///  - <see cref="DataReceived"/>: fires with each <see cref="GeoTelemetryData"/> sample.
///  - <see cref="StateChanged"/>: emits lifecycle transitions.
/// </summary>
public class SimulatorGps : ITelemtrySensor, IDisposable
{
    private readonly ILogger _logger;
    private readonly DeviceSessionData _driverRace;   // optional source; if null, we use an embedded CSV
    private readonly int _skipData;                   // number of data rows to skip after each emitted row
    private int _skipDataOnce;                        // one-time skip (consumed on first loop)
    private long _messagesReceived;

    // CSV field indexes (auto-discovered from header)
    private int _latitudeColumnIndex = 1;
    private int _longitudeColumnIndex = 2;
    private int _timeIndex = 0;
    private int _distanceGps = -1;
    private int _speed = -1;

    // Control & lifecycle
    private CancellationTokenSource _cts;
    private Task _workerTask;
    private int _startedFlag;   // 0 = stopped, 1 = started (prevents double start)

    public SimulatorGps(DeviceSessionData driverRace = null, int skipData = 0, int skipDataOnce = 0, ILogger logger = null)
    {
        _driverRace = driverRace;
        _skipData = skipData;
        _skipDataOnce = skipDataOnce;
        _logger = logger;

        Debug.WriteLine("SimulatorGps: created");
        OnStateChanged(TelemetryState.Ready);
    }

    /// <summary>Total errors (simulator never increments; kept for API parity).</summary>
    public int Errors => 0;

    /// <summary>Total messages emitted.</summary>
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

    /// <summary>Static id to identify the source.</summary>
    public string UniqueId => "Simulator";

    /// <summary>Current state.</summary>
    public TelemetryState State { get; private set; }

    public bool Start()
    {
        if (Interlocked.Exchange(ref _startedFlag, 1) == 1)
            return false; // already started

        OnStateChanged(TelemetryState.Starting);
        _cts = new CancellationTokenSource();

        // choose source
        if (_driverRace == null)
        {
            _workerTask = StartParsingEmbeddedAsync(_cts.Token);
        }
        else
        {
            _workerTask = StartProcessingDriverRaceAsync(_driverRace, _cts.Token);
        }

        OnStateChanged(TelemetryState.Receiving);
        return true;
    }

    public bool Stop()
    {
        if (Interlocked.Exchange(ref _startedFlag, 0) == 0)
            return false; // not running

        OnStateChanged(TelemetryState.Stopping);

        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _workerTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }

        OnStateChanged(TelemetryState.Stopped);
        return true;
    }

    public void Dispose()
    {
        Stop();

        // detach subscribers (defensive cleanup)
        if (DataReceived != null)
            foreach (var d in DataReceived.GetInvocationList())
                DataReceived -= (EventHandler<GeoDataReceivedEventArgs>)d;

        if (Error != null)
            foreach (var d in Error.GetInvocationList())
                Error -= (EventHandler<Exception>)d;

        _cts?.Dispose();

        Debug.WriteLine("SimulatorGps: destroyed");
    }

    /// <summary>
    /// Replays telemetry from an in-memory session: shifts timestamps so the first sample lands "now",
    /// then emits samples in real-time order (honoring <see cref="_skipData"/> throttling).
    /// </summary>
    private async Task StartProcessingDriverRaceAsync(DeviceSessionData driverRace, CancellationToken ct)
    {
        try
        {
            DateTimeOffset? firstInputTs = null;
            var timeDelta = TimeSpan.Zero;
            long emitted = 0;

            // Use the enumerator so we can truly skip items.
            using (var it = driverRace.TelemetryData.GetEnumerator())
            {
                while (!ct.IsCancellationRequested && it.MoveNext())
                {
                    if (ct.IsCancellationRequested) break;

                    var td = it.Current;
                    var geo = td as GeoTelemetryData;
                    if (geo == null) continue;

                    // Keep your original "Latitude == 0" guard
                    if (geo.Latitude == 0) continue;

                    if (!firstInputTs.HasValue)
                    {
                        firstInputTs = geo.Timestamp;
                        timeDelta = DateTimeOffset.UtcNow - firstInputTs.Value;
                    }

                    var adjusted = geo.Timestamp + timeDelta;

                    // Pace to wall clock to simulate real time
                    var delay = adjusted - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, ct).ConfigureAwait(false);

                    // Emit a copy
                    var sample = new GeoTelemetryData
                    {
                        Latitude = geo.Latitude,
                        Longitude = geo.Longitude,
                        Altitude = geo.Altitude,
                        Speed = geo.Speed,
                        Accuracy = geo.Accuracy,
                        Distance = geo.Distance,
                        Provider = "Sim",
                        Timestamp = adjusted
                    };

                    SendData(sample);
                    emitted++;

                    var count = Interlocked.Increment(ref _messagesReceived);
                    if ((count % 100) == 0)
                    {
                        Debug.WriteLine($"SimulatorGps: {count} messages emitted (DriverRace). Last: {sample}");
                        _logger?.LogInformation("SimulatorGps emitted {count} messages (DriverRace). Last: {sample}", count, sample);
                    }

                    // Properly skip next N samples if requested
                    if (_skipData > 0)
                    {
                        int skipped = 0;
                        while (skipped < _skipData && !ct.IsCancellationRequested && it.MoveNext())
                        {
                            // Optionally, you could validate the skipped item type here,
                            // but we simply drop it to simulate a lower sampling rate.
                            skipped++;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful
        }
        catch (Exception ex)
        {
            OnError(ex);
            OnStateChanged(TelemetryState.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Opens a default embedded CSV resource and replays it as real-time telemetry.
    /// </summary>
    private async Task StartParsingEmbeddedAsync(CancellationToken ct)
    {
        try
        {
            // Default embedded sample (kept from your original code)
            const string resourceName = "LapViz.Telemetry.Resources.GenkSessionWithPit.csv";
            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException("Embedded resource not found.", resourceName);

                using (var reader = new StreamReader(stream))
                {
                    await ProcessStreamAsync(reader, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { /* graceful */ }
        catch (Exception ex)
        {
            OnError(ex);
            OnStateChanged(TelemetryState.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Reads a LapViz CSV-like stream and emits <see cref="GeoTelemetryData"/> in real time.
    /// Supports headers, "#"-commented lines, and one-time and steady skipping.
    /// </summary>
    private async Task ProcessStreamAsync(StreamReader reader, CancellationToken ct)
    {
        long read = 0;

        DateTimeOffset? firstInputTs = null;
        var timeDelta = TimeSpan.Zero;
        double? lastDistance = null;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            string line = reader.ReadLine();
            if (line == null) break;

            // Skip comments/empty
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            // Header rows (quoted fields or start with 'T' in some formats)
            if (line.StartsWith("\"", StringComparison.Ordinal) || line.StartsWith("T", StringComparison.Ordinal))
            {
                ParseFieldDefinition(line);
                continue;
            }

            // One-time skip (consume N lines and reset)
            if (_skipDataOnce > 0)
            {
                for (int i = 0; i < _skipDataOnce && !reader.EndOfStream; i++)
                    reader.ReadLine();
                _skipDataOnce = 0;
                continue;
            }

            // Data row
            var elements = line.Split(',');
            if (elements.Length <= Math.Max(_longitudeColumnIndex, _timeIndex))
                continue; // malformed

            // Original guard: ignore rows where column 1 starts with "0"
            if (elements.Length > 1 && elements[1].StartsWith("0", StringComparison.Ordinal))
                continue;

            // Parse essentials
            var secondsSinceEpoch = Convert.ToDouble(elements[_timeIndex], CultureInfo.InvariantCulture);
            var eventDateTime = FromUnixTime((long)(secondsSinceEpoch * 1000)); // preserves ms
            var latitude = Convert.ToDouble(elements[_latitudeColumnIndex], CultureInfo.InvariantCulture);
            var longitude = Convert.ToDouble(elements[_longitudeColumnIndex], CultureInfo.InvariantCulture);

            // Optional channels
            var distance = _distanceGps > -1 && _distanceGps < elements.Length
                ? Convert.ToDouble(elements[_distanceGps], CultureInfo.InvariantCulture)
                : 0.0;

            double? speed = null;
            if (_speed > -1 && _speed < elements.Length)
                speed = Convert.ToDouble(elements[_speed], CultureInfo.InvariantCulture);

            // Align first input timestamp to "now"
            if (!firstInputTs.HasValue)
            {
                firstInputTs = eventDateTime;
                timeDelta = DateTimeOffset.UtcNow - firstInputTs.Value;
            }

            var adjusted = eventDateTime + timeDelta;

            // Pace to wall clock
            var delay = adjusted - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            // Build sample
            var geo = new GeoTelemetryData
            {
                Latitude = latitude,
                Longitude = longitude,
                Timestamp = adjusted,
                Accuracy = 0,
                Distance = lastDistance.HasValue ? distance - lastDistance.Value : 0,
                Provider = "Sim",
                Speed = speed
            };

            lastDistance = distance;

            SendData(geo);
            read++;

            var count = Interlocked.Increment(ref _messagesReceived);
            if ((count % 100) == 0)
            {
                Debug.WriteLine($"SimulatorGps: {count} messages emitted (CSV). Last: {geo}");
                _logger?.LogInformation("SimulatorGps emitted {count} messages (CSV). Last: {geo}", count, geo);
            }

            // Continuous skip to simulate lower rate
            for (int i = 0; i < _skipData && !reader.EndOfStream; i++)
                reader.ReadLine();
        }
    }

    private readonly DateTime s_epochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Converts Unix time in milliseconds to a UTC <see cref="DateTime"/>.
    /// (Kept for backward compatibility.)
    /// </summary>
    private DateTime FromUnixTime(long unixTimeMs)
    {
        // Guard against overflow: DateTime supports roughly years 0001..9999
        return s_epochUtc.AddMilliseconds(unixTimeMs);
    }

    /// <summary>
    /// Parses the header row and maps column indices. 
    /// Looks for common field names: Latitude, Longitude, Time, "Distance GPS", KPH.
    /// </summary>
    private void ParseFieldDefinition(string line)
    {
        var fields = line.Split(',');

        for (int i = 0; i < fields.Length; i++)
        {
            var token = fields[i].Trim().Trim('"');
            if (string.Equals(token, "Latitude", StringComparison.OrdinalIgnoreCase))
                _latitudeColumnIndex = i;
            else if (string.Equals(token, "Longitude", StringComparison.OrdinalIgnoreCase))
                _longitudeColumnIndex = i;
            else if (string.Equals(token, "Time", StringComparison.OrdinalIgnoreCase))
                _timeIndex = i;
            else if (string.Equals(token, "Distance GPS", StringComparison.OrdinalIgnoreCase))
                _distanceGps = i;
            else if (string.Equals(token, "KPH", StringComparison.OrdinalIgnoreCase))
                _speed = i;
        }
    }

    private void SendData(GeoTelemetryData data)
    {
        OnDataReceived(new GeoDataReceivedEventArgs
        {
            Message = data,
            Timestamp = DateTime.UtcNow
        });
    }

    public event EventHandler<GeoDataReceivedEventArgs> DataReceived;

    protected virtual void OnDataReceived(GeoDataReceivedEventArgs e)
    {
        var handler = DataReceived;
        if (handler == null) return;

        try { handler(this, e); }
        catch (Exception ex) { OnError(ex); }
    }

    public event EventHandler<Exception> Error;

    protected virtual void OnError(Exception e)
    {
        var handler = Error;
        if (handler == null) return;

        try { handler(this, e); }
        catch { /* never throw from error events */ }
    }

    public event EventHandler<TelemetryStateChangedEventArgs> StateChanged;

    protected virtual void OnStateChanged(TelemetryState state, string message = null)
    {
        State = state;
        var handler = StateChanged;
        if (handler == null) return;

        try
        {
            handler(this, new TelemetryStateChangedEventArgs
            {
                State = state,
                Message = message,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { /* ignore listener exceptions */ }
    }
}
