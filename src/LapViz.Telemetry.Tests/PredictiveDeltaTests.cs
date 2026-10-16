using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;
using LapViz.Telemetry.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.Tests.Services;

public class PredictiveDeltaTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static GeoCoordinates P(double lat, double lon) => new GeoCoordinates(lat, lon);

    /// <summary>
    /// Same check as the LapViz.Devices firmware (tests/gps_test.cpp) on the same session: at the
    /// first fix after each sector line, the delta must be the difference between this lap's time
    /// at the line and the reference lap's (plus the few milliseconds between the line and the
    /// fix, the same on both laps at this precision).
    /// </summary>
    [Fact]
    public async Task Delta_At_The_Lines_Matches_The_Split_Differences_On_A_Recorded_Session()
    {
        var parser = new DelimitedDataReader(new DelimitedDataReaderConfiguration
        {
            ChannelsSignature = "\"Time",
            Delimiter = ',',
            TelemetryDevice = "Test"
        });
        parser.Load(Path.Combine(AppContext.BaseDirectory, "runs", "MariembourgFreeTests.csv"));
        var fixes = parser.GetSessionData().First().TelemetryData.Cast<GeoTelemetryData>().ToList();

        var circuit = await new StaticCircuitService().Detect(fixes[0]);
        var timer = new LapTimerService(NullLogger<LapTimerService>.Instance, new LapTimerConfig { AutoStartDetection = true });
        timer.SetCircuit(circuit);

        var delta = new PredictiveDelta();
        var splits = new Dictionary<int, TimeSpan>();
        var referenceSplits = new Dictionary<int, TimeSpan>();
        TimeSpan? referenceLap = null;
        DateTimeOffset? lapStart = null;
        TimeSpan? expected = null;
        var checks = new List<(TimeSpan Expected, TimeSpan? Actual)>();
        var deltasAfterReference = new List<(DateTimeOffset Time, bool HasDelta)>();
        var lapStarts = new List<DateTimeOffset>();

        timer.EventAdded += (_, e) =>
        {
            if (e.Type == SessionEventType.Lap)
            {
                if (lapStart.HasValue && (referenceLap == null || e.Time < referenceLap))
                {
                    referenceLap = e.Time;
                    referenceSplits = new Dictionary<int, TimeSpan>(splits);
                }

                delta.OnLapEvent(e);
                lapStart = e.Timestamp;
                lapStarts.Add(e.Timestamp);
            }
            else if (e.Type == SessionEventType.Sector && lapStart.HasValue && e.Sector != circuit.Segments.Count)
            {
                var split = e.Timestamp - lapStart.Value;
                splits[e.Sector] = split;
                if (referenceLap.HasValue && referenceSplits.TryGetValue(e.Sector, out var referenceSplit))
                    expected = split - referenceSplit;
            }
        };

        foreach (var fix in fixes)
        {
            timer.AddGeolocation(fix);
            delta.OnFix(fix, fix.Timestamp);

            if (referenceLap.HasValue)
                deltasAfterReference.Add((fix.Timestamp, delta.Delta.HasValue));

            if (expected.HasValue)
            {
                checks.Add((expected.Value, delta.Delta));
                expected = null;
            }

            if (delta.HasReference)
                Assert.Equal(referenceLap!.Value.TotalMilliseconds, delta.ReferenceLapTime.TotalMilliseconds, 3);
        }

        Assert.True(checks.Count >= 10, $"only {checks.Count} delta checks");
        foreach (var (exp, actual) in checks)
        {
            Assert.True(actual.HasValue, "no delta right after a line");
            Assert.True((actual!.Value - exp).Duration() <= TimeSpan.FromMilliseconds(60), $"delta {actual} at a line, expected {exp}");
        }

        // Every fix of the laps after the reference has a delta (the session then ends in the pit lane)
        var lastLapStart = lapStarts.Last();
        var inLaps = deltasAfterReference.Where(d => d.Time <= lastLapStart).ToList();
        Assert.NotEmpty(inLaps);
        Assert.All(inLaps, d => Assert.True(d.HasDelta, $"no delta at {d.Time:HH:mm:ss.fff}"));

        Assert.Equal(timer.ActiveSession!.BestLap!.Time.TotalMilliseconds, delta.ReferenceLapTime.TotalMilliseconds, 3);
    }

    // A closed "lap" at a constant speed, at 10 Hz: north along `lon` from 50.000 to 50.010 in
    // the first half, back south 0.002 degree (about 140 m) east in the second half
    private static void DriveLap(PredictiveDelta delta, DateTimeOffset start, double seconds, double lon = 5.0)
    {
        var steps = (int)(seconds * 10);
        delta.OnLapStart(P(50.000, lon), start);
        for (var i = 1; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var position = progress <= 0.5
                ? P(50.000 + (0.01 * progress / 0.5), lon)
                : P(50.010 - (0.01 * (progress - 0.5) / 0.5), lon + 0.002);
            delta.OnFix(position, start.AddSeconds(seconds * progress));
        }
    }

    [Fact]
    public void No_Delta_Before_A_Reference_Lap()
    {
        var delta = new PredictiveDelta();
        delta.OnLapStart(P(50.0, 5.0), T0);
        delta.OnFix(P(50.001, 5.0), T0.AddSeconds(1));

        Assert.False(delta.HasReference);
        Assert.Null(delta.Delta);
    }

    [Fact]
    public void Delta_Is_The_Time_Difference_At_The_Same_Place()
    {
        var delta = new PredictiveDelta();
        DriveLap(delta, T0, seconds: 60);                 // reference: 60 s
        delta.OnLapStart(P(50.000, 5.0), T0.AddSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(60), delta.ReferenceLapTime);

        // Half way up the north leg after 18 s instead of 15 s: 3 s behind
        delta.OnFix(P(50.005, 5.0), T0.AddSeconds(78));
        Assert.Equal(3000, delta.Delta!.Value.TotalMilliseconds, 0);

        // Three quarters up after 19.5 s instead of 22.5 s: 3 s ahead
        delta.OnFix(P(50.0075, 5.0), T0.AddSeconds(79.5));
        Assert.Equal(-3000, delta.Delta!.Value.TotalMilliseconds, 0);
    }

    [Fact]
    public void Only_A_Faster_Complete_Lap_Becomes_The_Reference()
    {
        var delta = new PredictiveDelta();
        DriveLap(delta, T0, seconds: 60);
        DriveLap(delta, T0.AddSeconds(60), seconds: 70);  // slower: not the reference
        DriveLap(delta, T0.AddSeconds(130), seconds: 55); // faster
        delta.OnLapStart(P(50.000, 5.0), T0.AddSeconds(185));

        Assert.Equal(TimeSpan.FromSeconds(55), delta.ReferenceLapTime);
    }

    [Fact]
    public void A_Lap_With_A_GPS_Gap_Never_Becomes_The_Reference()
    {
        var delta = new PredictiveDelta();
        DriveLap(delta, T0, seconds: 60);

        // Faster lap, but 2 s without fix in the middle
        delta.OnLapStart(P(50.000, 5.0), T0.AddSeconds(60));
        delta.OnFix(P(50.002, 5.0), T0.AddSeconds(70));
        delta.OnFix(P(50.006, 5.0), T0.AddSeconds(72));
        delta.OnFix(P(50.010, 5.0), T0.AddSeconds(74));
        delta.OnLapStart(P(50.000, 5.0), T0.AddSeconds(110));

        Assert.Equal(TimeSpan.FromSeconds(60), delta.ReferenceLapTime);
    }

    [Fact]
    public void Far_From_The_Reference_Trace_There_Is_No_Delta()
    {
        var delta = new PredictiveDelta();
        DriveLap(delta, T0, seconds: 60);
        delta.OnLapStart(P(50.000, 5.0), T0.AddSeconds(60));

        // About 70 m east of the trace (pit lane)
        delta.OnFix(P(50.005, 5.001), T0.AddSeconds(90));
        Assert.Null(delta.Delta);

        // Back on track
        delta.OnFix(P(50.006, 5.0), T0.AddSeconds(96));
        Assert.NotNull(delta.Delta);
    }

    [Fact]
    public void Reset_Forgets_The_Reference()
    {
        var delta = new PredictiveDelta();
        DriveLap(delta, T0, seconds: 60);
        delta.OnLapStart(P(50.000, 5.0), T0.AddSeconds(60));
        Assert.True(delta.HasReference);

        delta.Reset();
        Assert.False(delta.HasReference);
        Assert.Null(delta.Delta);
    }
}
