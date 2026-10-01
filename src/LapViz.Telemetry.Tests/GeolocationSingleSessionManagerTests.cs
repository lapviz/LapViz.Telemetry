using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;
using LapViz.Telemetry.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.Tests.Services;

public class GeolocationSingleSessionManagerTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2020, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static CircuitConfiguration MakeCircuit(string code, double centerLat)
    {
        var cfg = new CircuitConfiguration
        {
            Name = code,
            Code = code,
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(
                new GeoCoordinates(centerLat + 0.01, -0.01),
                new GeoCoordinates(centerLat - 0.01, 0.01)),
        };
        cfg.Segments.Add(new CircuitSegment
        {
            Number = 1,
            Boundary = new CircuitGeoLine(
                new GeoCoordinates(centerLat + 0.005, 0.0),
                new GeoCoordinates(centerLat - 0.005, 0.0))
        });
        return cfg;
    }

    private static GeoTelemetryData Fix(double lat, double lon, DateTimeOffset ts)
        => new GeoTelemetryData { Latitude = lat, Longitude = lon, Timestamp = ts };

    [Fact]
    public void CircuitChange_After_IdleTimeout_Ends_Previous_Session_Only_Once()
    {
        var a = MakeCircuit("A", 0);
        var b = MakeCircuit("B", 1);
        var manager = new GeolocationSingleSessionManager(new StaticCircuitService(new[] { a, b }));

        var ended = new List<DeviceSessionData>();
        manager.DriverSessionEnded += (_, s) => ended.Add(s);

        manager.AddGeolocation(Fix(0, -0.001, T0));                  // circuit A detected, session 1
        manager.AddGeolocation(Fix(0, -0.0011, T0.AddMinutes(20)));  // idle timeout ends session 1
        Assert.Single(ended);

        manager.AddGeolocation(Fix(1, -0.001, T0.AddMinutes(21)));   // circuit B detected, session 2

        Assert.Single(ended);
        Assert.Equal(2, manager.DriverSessions.Count);
        Assert.Equal("B", manager.CurrentDriverSession.CircuitCode);
    }

    [Fact]
    public void IdleTimeout_Uses_Telemetry_Time_When_Replaying_Old_Recordings()
    {
        var manager = new GeolocationSingleSessionManager(new StaticCircuitService(MakeCircuit("A", 0)));
        var ended = 0;
        manager.DriverSessionEnded += (_, _) => ended++;

        // Recording from 2020, no crossing at all: session must still time out after 15 minutes of data
        manager.AddGeolocation(Fix(0, -0.001, T0));
        manager.AddGeolocation(Fix(0, -0.0011, T0.AddMinutes(16)));

        Assert.Equal(1, ended);
        Assert.Null(manager.CurrentDriverSession);
    }

    [Fact]
    public async Task AddGeolocationAsync_Detects_Circuit_And_Laps()
    {
        var manager = new GeolocationSingleSessionManager(
            new StaticCircuitService(MakeCircuit("A", 0)), minSecondsBetweenSectors: 0);
        var laps = new List<SessionDataEvent>();
        manager.SessionEventAdded += (_, e) => { if (e.Type == SessionEventType.Lap) laps.Add(e); };

        await manager.AddGeolocationAsync(Fix(0, -0.001, T0));
        await manager.AddGeolocationAsync(Fix(0, 0.001, T0.AddSeconds(2)));    // first crossing, lap 0
        // Go around the finish line (it spans lat ±0.005) to come back before it
        await manager.AddGeolocationAsync(Fix(0.008, 0.001, T0.AddSeconds(20)));
        await manager.AddGeolocationAsync(Fix(0.008, -0.001, T0.AddSeconds(30)));
        await manager.AddGeolocationAsync(Fix(0, -0.001, T0.AddSeconds(40)));
        await manager.AddGeolocationAsync(Fix(0, 0.001, T0.AddSeconds(62)));   // lap 1: crossings interpolated at t=1 s and t=51 s

        Assert.Equal("A", manager.CurrentCircuit.Code);
        Assert.Equal(2, laps.Count);
        Assert.Equal(TimeSpan.FromSeconds(50), laps[1].Time);
        Assert.True(laps[1].IsBestOverall);
    }

    [Fact]
    public void Handler_Exception_Does_Not_Break_Processing()
    {
        var manager = new GeolocationSingleSessionManager(
            new StaticCircuitService(MakeCircuit("A", 0)), NullLogger<GeolocationSingleSessionManager>.Instance);
        manager.SessionEventAdded += (_, _) => throw new InvalidOperationException("boom");

        manager.AddGeolocation(Fix(0, -0.001, T0));
        manager.AddGeolocation(Fix(0, 0.001, T0.AddSeconds(2)));

        Assert.Equal(2, manager.CurrentDriverSession.Events.Count); // sector + lap
    }

    [Fact]
    public void Sync_AddGeolocation_Does_Not_Deadlock_On_SynchronizationContext()
    {
        // A circuit service that resumes on the captured context would deadlock a naive .Result
        var service = new ContextCapturingCircuitService(MakeCircuit("A", 0));
        var manager = new GeolocationSingleSessionManager(service);

        var done = false;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SingleThreadBlockingContext());
            manager.AddGeolocation(Fix(0, -0.001, T0));
            done = true;
        });
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "AddGeolocation deadlocked");
        Assert.True(done);
        Assert.Equal("A", manager.CurrentCircuit.Code);
    }

    [Fact]
    public void Manager_And_LapTimerService_Produce_The_Same_Laps_On_Real_Data()
    {
        var parser = new DelimitedDataReader(new DelimitedDataReaderConfiguration
        {
            ChannelsSignature = "\"Time",
            Delimiter = ',',
            TelemetryDevice = "Test"
        });
        parser.Load(Path.Combine(AppContext.BaseDirectory, "runs", "MariembourgFreeTests.csv"));
        var fixes = parser.GetSessionData().First().TelemetryData.Cast<GeoTelemetryData>().ToList();

        var circuitService = new StaticCircuitService();
        var circuit = circuitService.Detect(fixes[0]).Result;

        var timer = new LapTimerService(new NullLogger<LapTimerService>(), new LapTimerConfig());
        timer.SetCircuit(circuit);
        timer.StartDetection();
        var manager = new GeolocationSingleSessionManager(circuitService);

        foreach (var fix in fixes)
        {
            timer.AddGeolocation(fix);
            manager.AddGeolocation(fix);
        }

        static List<(int, TimeSpan, bool)> Laps(DeviceSessionData s) => s.Events
            .Where(e => e.Type == SessionEventType.Lap)
            .Select(e => (e.LapNumber, e.Time, e.IsBestOverall))
            .ToList();

        var timerLaps = Laps(timer.ActiveSession);
        Assert.NotEmpty(timerLaps);
        Assert.Equal(timerLaps, Laps(manager.CurrentDriverSession));
    }

    private sealed class ContextCapturingCircuitService : ICircuitService
    {
        private readonly CircuitConfiguration _circuit;
        public ContextCapturingCircuitService(CircuitConfiguration circuit) => _circuit = circuit;

        public async Task<CircuitConfiguration> Detect(GeoTelemetryData geoLocation)
        {
            await Task.Delay(10); // resumes on the captured SynchronizationContext, if any
            return _circuit;
        }

        public Task<CircuitConfiguration> GetByCode(string code) => Task.FromResult(_circuit);
        public Task Sync(double lat, double lon, int radius) => Task.CompletedTask;
        public event EventHandler<CircuitSyncProgress> SyncProgress { add { } remove { } }
    }

    /// <summary>Context whose posted callbacks never run while its thread is blocked (like a UI thread).</summary>
    private sealed class SingleThreadBlockingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object state)
        {
            // Dropped on purpose: the owning thread is blocked, so the continuation can never run
        }
    }
}
