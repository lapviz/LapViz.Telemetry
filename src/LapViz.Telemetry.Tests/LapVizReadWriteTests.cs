using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;
using LapViz.Telemetry.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.Tests.Services;

public class LapVizReadWriteTests
{
    private static string CsvPath =>
        Path.Combine(AppContext.BaseDirectory, "runs", "MariembourgFreeTests.csv");

    private static DeviceSessionData LoadCsvSession()
    {
        var parser = new DelimitedDataReader(new DelimitedDataReaderConfiguration
        {
            ChannelsSignature = "\"Time",
            Delimiter = ',',
            TelemetryDevice = "Test"
        });
        parser.Load(CsvPath);
        return parser.GetSessionData().First();
    }

    private static DeviceSessionData BuildSessionWithEventsFromCsv()
    {
        var csv = LoadCsvSession();

        var circuit = new StaticCircuitService()
            .Detect((GeoTelemetryData)csv.TelemetryData.First()).GetAwaiter().GetResult();
        Assert.NotNull(circuit);

        var cfg = new LapTimerConfig
        {
            AutoStartDetection = true,
            MinimumTimeBetweenEvents = TimeSpan.Zero,
            MaxTelemetryDataRetention = 10000,
            TrackPosition = false
        };

        var timer = new LapTimerService(new NullLogger<LapTimerService>(), cfg);
        timer.SetCircuit(circuit);

        foreach (var geo in csv.TelemetryData.Cast<GeoTelemetryData>())
            timer.AddGeolocation(geo);

        var session = timer.ActiveSession;
        session.TelemetryChannels = csv.TelemetryChannels;
        Assert.NotNull(session);

        // sanity based on your earlier test
        Assert.True(session.Events.Count >= 50);

        return session;
    }

    /// <summary>
    /// Laps and sectors (type, lap, sector, time to the microsecond): what the format keeps. Crossings of zero duration
    /// (the first crossing of the line) are markers, not laps.
    /// </summary>
    private static List<(SessionEventType Type, int Lap, int Sector, long Microseconds)> Timed(DeviceSessionData session) =>
        session.Events
            .Where(x => (x.Type == SessionEventType.Lap || x.Type == SessionEventType.Sector) && x.Time > TimeSpan.Zero)
            .Select(x => (x.Type, x.LapNumber, x.Type == SessionEventType.Sector ? x.Sector : 0, (long)Math.Round(x.Time.TotalMilliseconds * 1000)))
            .OrderBy(x => x.Item2).ThenBy(x => x.Item1).ThenBy(x => x.Item3)
            .ToList();

    private static void AssertEqualUnixMs(DateTimeOffset a, DateTimeOffset b)
        => Assert.Equal(a.ToUnixTimeMilliseconds(), b.ToUnixTimeMilliseconds());

    public sealed class OneShotRoundTrip
    {
        [Fact]
        public void WriteAll_Compressed_Then_Read_Back_Preserves_Events_And_Data()
        {
            var sessionWithEvents = BuildSessionWithEventsFromCsv();

            var temp = Path.GetTempFileName();
            try
            {
                // Write compressed .lvz container
                new LapVizDataWriter().WriteAll(sessionWithEvents, temp, overwrite: true);

                // Read back
                var reader = new LapVizDataReader();
                reader.Load(temp);
                var roundTripped = reader.GetSessionData().First();

                // Telemetry channels preserved
                Assert.Equal(
                    sessionWithEvents.TelemetryChannels ?? new List<string>(),
                    roundTripped.TelemetryChannels ?? new List<string>());

                // Telemetry samples count
                Assert.Equal(sessionWithEvents.TelemetryData.Count, roundTripped.TelemetryData.Count);

                // First and last sample timestamps identical to the ms
                AssertEqualUnixMs(sessionWithEvents.TelemetryData.First().Timestamp, roundTripped.TelemetryData.First().Timestamp);
                AssertEqualUnixMs(sessionWithEvents.TelemetryData.Last().Timestamp, roundTripped.TelemetryData.Last().Timestamp);

                // If Latitude/Longitude present, spot check a mid-sample maps correctly
                var mid = sessionWithEvents.TelemetryData[sessionWithEvents.TelemetryData.Count / 2] as GeoTelemetryData;
                var mid2 = roundTripped.TelemetryData[roundTripped.TelemetryData.Count / 2] as GeoTelemetryData;
                if (mid?.Latitude != null && mid2?.Latitude != null)
                {
                    Assert.InRange(Math.Abs(mid.Latitude - mid2.Latitude), 0, 1e-9);
                    Assert.InRange(Math.Abs(mid.Longitude - mid2.Longitude), 0, 1e-9);
                }

                // Laps and sectors preserved, including the sectors of the last, incomplete lap
                Assert.Equal(Timed(sessionWithEvents), Timed(roundTripped));

                // Known assertion from your integration test: lap 3 time 00:00:57.286 +/- 1 ms
                var expected = TimeSpan.FromMilliseconds(57287);
                var actual = roundTripped.Events
                    .Where(x => x.LapNumber == 3 && x.Type == SessionEventType.Lap)
                    .Select(x => x.Time)
                    .First();

                var tolerance = TimeSpan.FromMilliseconds(1);
                Assert.True((actual - expected).Duration() <= tolerance,
                    $"Expected {expected}, got {actual} (tolerance {tolerance}).");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void WriteAll_ToMemory_CompressedEntry_RoundTrip_Works()
        {
            var session = BuildSessionWithEventsFromCsv();

            using var ms = new LapVizDataWriter().WriteAll(session);
            // Save to disk then read via reader, since reader expects a path
            var temp = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(temp, ms.ToArray());

                var reader = new LapVizDataReader();
                reader.Load(temp);
                var rt = reader.GetSessionData().First();

                Assert.Equal(session.TelemetryData.Count, rt.TelemetryData.Count);
                Assert.Equal(Timed(session), Timed(rt));
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        private static GeoTelemetryData GetData(
            DateTimeOffset dateTimeOffset,
            double latitude,
            double longitude,
            double speed,
            double accuracy)
        {
            return new GeoTelemetryData
            {
                Timestamp = dateTimeOffset,
                Latitude = latitude,
                Longitude = longitude,
                Accuracy = accuracy,
                Speed = speed,
                Data = new double?[4] { latitude, longitude, speed, accuracy }
            };
        }
    }

    public sealed class Compatibility
    {
        [Fact]
        public void IsDataCompatible_True_For_Compressed_Output()
        {
            var session = BuildSessionWithEventsFromCsv();
            var temp = Path.GetTempFileName();
            try
            {
                new LapVizDataWriter().WriteAll(session, temp, overwrite: true);

                var reader = new LapVizDataReader();
                Assert.True(reader.IsDataCompatible(temp));
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void IsDataCompatible_True_For_Plain_Text_Output()
        {
            var session = BuildSessionWithEventsFromCsv();
            var temp = Path.GetTempFileName();
            try
            {
                // Write plain text
                new LapVizDataWriter().WriteAll(session, temp, overwrite: true, compressed: false);

                var reader = new LapVizDataReader();
                Assert.True(reader.IsDataCompatible(temp));
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
