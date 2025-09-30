using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;
using LapViz.Telemetry.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace LapViz.Telemetry.Tests.Services;

public class LapTimerServiceTests
{
    private static CircuitConfiguration MakeCircuit(
        string code = "test",
        int segmentCount = 1,
        bool useDirection = false,
        int sectorTimeoutSeconds = 0)
    {
        // Bounding box around (0,0) large enough to contain all test points
        var cfg = new CircuitConfiguration
        {
            Name = "TestCircuit",
            Code = code,
            Type = CircuitType.Closed,
            UseDirection = useDirection,
            BoundingBox = new CircuitGeoLine(
                new GeoCoordinates(0.01, -0.01),
                new GeoCoordinates(-0.01, 0.01)),
            Center = new GeoCoordinates(0, 0),
            Zoom = 16,
            SectorTimeout = sectorTimeoutSeconds
        };

        // Add N small sector lines around lon=0 to simulate multiple segments
        for (int i = 1; i <= segmentCount; i++)
        {
            var offset = (i - 1) * 0.0005;
            cfg.Segments.Add(new CircuitSegment
            {
                Number = i,
                Boundary = new CircuitGeoLine(
                    new GeoCoordinates(0.005, 0.0 + offset),
                    new GeoCoordinates(-0.005, 0.0 + offset))
            });
        }

        return cfg;
    }

    private static GeoTelemetryData Fix(double lat, double lon, DateTimeOffset ts)
        => new GeoTelemetryData { Latitude = lat, Longitude = lon, Timestamp = ts };

    private static LapTimerService NewService(LapTimerConfig cfg = null)
        => new LapTimerService(new NullLogger<LapTimerService>(), cfg ?? new LapTimerConfig());

    public sealed class CtorAndState
    {
        [Fact]
        public void AutoStartDetection_False_ByDefault_IsPaused()
        {
            var cfg = new LapTimerConfig { AutoStartDetection = false };
            var svc = NewService(cfg);
            Assert.False(svc.IsRunning);
        }

        [Fact]
        public void AutoStartDetection_True_StartsRunning()
        {
            var cfg = new LapTimerConfig { AutoStartDetection = true };
            var svc = NewService(cfg);
            Assert.True(svc.IsRunning);
        }

        [Fact]
        public void Start_Stop_Toggles_IsRunning_And_Raises_SessionPaused_WhenActive()
        {
            var cfg = new LapTimerConfig { AutoStartDetection = false };
            var svc = NewService(cfg);
            var circuit = MakeCircuit();
            svc.SetCircuit(circuit);

            // create a session to enable pause event
            DeviceSessionData pausedTarget = null;
            svc.SessionPaused += (_, s) => pausedTarget = s;

            svc.CreateSession();
            Assert.True(svc.IsRunning); // CreateSession sets running = true

            svc.StopDetection();
            Assert.False(svc.IsRunning);
            Assert.NotNull(pausedTarget);

            svc.StartDetection();
            Assert.True(svc.IsRunning);
        }
    }

    public sealed class CircuitSelection
    {
        [Fact]
        public void SetCircuit_Throws_OnNull()
        {
            var svc = NewService();
            Assert.Throws<ArgumentNullException>(() => svc.SetCircuit(null));
        }

        [Fact]
        public void SetCircuit_Closes_Active_Session()
        {
            var svc = NewService();
            var a = MakeCircuit("A");
            var b = MakeCircuit("B");
            svc.SetCircuit(a);

            DeviceSessionData ended = null;
            svc.SessionEnded += (_, s) => ended = s;

            svc.CreateSession();
            Assert.NotNull(svc.ActiveSession);
            Assert.Equal("A", svc.ActiveSession.CircuitCode);

            // switching circuits should close current session
            svc.SetCircuit(b);
            Assert.Null(svc.ActiveSession);
            Assert.NotNull(ended);
            Assert.Equal("A", ended.CircuitCode);
            Assert.Equal("B", svc.CircuitConfiguration.Code);
        }

        [Fact]
        public void CreateSession_Throws_If_NoCircuit()
        {
            var svc = NewService();
            Assert.Throws<InvalidOperationException>(() => svc.CreateSession());
        }
    }

    public sealed class Events
    {
        [Fact]
        public void FirstCrossing_Produces_Sector_And_Lap_Events()
        {
            var cfg = new LapTimerConfig
            {
                AutoStartDetection = true,
                TrackPosition = false,
                MinimumTimeBetweenEvents = TimeSpan.Zero
            };
            var svc = NewService(cfg);
            var circuit = MakeCircuit(code: "C", segmentCount: 1);
            svc.SetCircuit(circuit);

            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var p1 = Fix(0, -0.001, t0);                // left of finish line
            var p2 = Fix(0, +0.001, t0.AddSeconds(10)); // right of finish line, crossing trajectory

            var added = new List<SessionDataEvent>();
            svc.EventAdded += (_, e) => added.Add(e);

            svc.AddGeolocation(p1, device: "devX"); // no event yet (no previous point)
            svc.AddGeolocation(p2, device: "devX"); // crossing produces events

            Assert.True(added.Count >= 2);
            var sector = added[0];
            var lap = added[1];

            Assert.Equal(SessionEventType.Sector, sector.Type);
            Assert.Equal(circuit.Segments.Count, sector.Sector);
            Assert.Equal("C", sector.CircuitCode);
            Assert.Equal("devX", sector.DeviceId);

            Assert.Equal(SessionEventType.Lap, lap.Type);
            Assert.Equal(0, lap.Sector);
            Assert.Equal("C", lap.CircuitCode);
            Assert.Equal("devX", lap.DeviceId);

            Assert.Equal(TimeSpan.Zero, lap.Time); // first crossing lap time is zero
        }

        [Fact]
        public void TrackPosition_Adds_Position_Event_Per_Fix_After_First()
        {
            var cfg = new LapTimerConfig
            {
                AutoStartDetection = true,
                TrackPosition = true,
                MinimumTimeBetweenEvents = TimeSpan.Zero
            };
            var svc = NewService(cfg);
            svc.SetCircuit(MakeCircuit(segmentCount: 1));

            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var p1 = Fix(0, -0.001, t0);
            var p2 = Fix(0, +0.001, t0.AddSeconds(1));

            var added = new List<SessionDataEvent>();
            svc.EventAdded += (_, e) => added.Add(e);

            svc.AddGeolocation(p1);
            svc.AddGeolocation(p2);

            Assert.True(added.Any(e => e.Type == SessionEventType.Position));
        }

        [Fact]
        public void DeviceId_And_UserId_Are_Stamped_From_Config_Or_Override()
        {
            var cfg = new LapTimerConfig
            {
                AutoStartDetection = true,
                UserId = "user-42",
                MinimumTimeBetweenEvents = TimeSpan.Zero
            };
            var svc = NewService(cfg);
            svc.SetCircuit(MakeCircuit(segmentCount: 1));

            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var p1 = Fix(0, -0.001, t0);
            var p2 = Fix(0, +0.001, t0.AddSeconds(2));

            var added = new List<SessionDataEvent>();
            svc.EventAdded += (_, e) => added.Add(e);

            svc.AddGeolocation(p1);               // no event yet
            svc.AddGeolocation(p2, "dev-override");

            Assert.NotEmpty(added);
            foreach (var ev in added)
            {
                Assert.Equal("user-42", ev.UserId);
                Assert.Equal("dev-override", ev.DeviceId);
            }
        }
    }

    public sealed class CustomCircuits
    {
        // Adjust this helper if your test asset lives elsewhere.
        private static string CsvPath =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "runs", "MariembourgFreeTests.csv");

        [Fact]
        public void MariembourgSix_FromCsv_Produces_Expected_Events_And_Lap_Time()
        {
            // Build the custom 6-segment circuit
            var mariembourgSix = new CircuitConfiguration
            {
                Name = "MariembourgSix",
                Code = "MariembourgSix",
                Type = CircuitType.Closed,
                UseDirection = true,
                BoundingBox = new CircuitGeoLine(
                    new GeoCoordinates(50.095075, 4.496925),
                    new GeoCoordinates(50.092157, 4.503523))
            };
            mariembourgSix.Segments.Add(new CircuitSegment { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.094002, 4.501658), new GeoCoordinates(50.093756, 4.501677)) });
            mariembourgSix.Segments.Add(new CircuitSegment { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093608, 4.501969), new GeoCoordinates(50.093629, 4.502334)) });
            mariembourgSix.Segments.Add(new CircuitSegment { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.092359, 4.499440), new GeoCoordinates(50.092603, 4.499566)) });
            mariembourgSix.Segments.Add(new CircuitSegment { Number = 4, Boundary = new CircuitGeoLine(new GeoCoordinates(50.092875, 4.500046), new GeoCoordinates(50.092680, 4.500279)) });
            mariembourgSix.Segments.Add(new CircuitSegment { Number = 5, Boundary = new CircuitGeoLine(new GeoCoordinates(50.092868, 4.499389), new GeoCoordinates(50.093078, 4.499239)) });
            mariembourgSix.Segments.Add(new CircuitSegment { Number = 6, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093789, 4.499239), new GeoCoordinates(50.093636, 4.499043)) });

            // Optional: dump to JSON for debugging
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "mariembourgsix.json"),
                JsonConvert.SerializeObject(mariembourgSix, Formatting.Indented));

            // Parse CSV with your existing delimited reader
            var parser = new DelimitedDataReader(new DelimitedDataReaderConfiguration
            {
                ChannelsSignature = "\"Time",
                Delimiter = ',',
                TelemetryDevice = "Test"
            });
            parser.Load(CsvPath);

            var driverRace = parser.GetSessionData().First();

            // Run LapTimerService with the custom circuit
            var cfg = new LapTimerConfig { AutoStartDetection = true };
            var svc = new LapTimerService(new NullLogger<LapTimerService>(), cfg);
            svc.SetCircuit(mariembourgSix);

            foreach (var geo in driverRace.TelemetryData.Cast<GeoTelemetryData>())
                svc.AddGeolocation(geo);

            var events = svc.ActiveSession.Events;

            // Expectations from your original sample
            Assert.Equal(90, events.Count);

            var expected = TimeSpan.FromMilliseconds(56357); // 00:00:56.357
            var actual = events
                .Where(x => x.LapNumber == 7 && x.Type == SessionEventType.Lap)
                .Select(x => x.Time)
                .First();

            var tolerance = TimeSpan.FromMilliseconds(1);
            Assert.True((actual - expected).Duration() <= tolerance,
                $"Expected {expected}, got {actual} (tolerance {tolerance}).");
        }

        [Fact]
        public void MariembourgTurns_FromCsv_Produces_Expected_Events_And_Lap_Time()
        {
            var mariembourgTurns = new CircuitConfiguration
            {
                Name = "MariembourgTurns",
                Code = "MariembourgTurns",
                Type = CircuitType.Closed,
                UseDirection = true,
                SectorTimeout = 1,
                BoundingBox = new CircuitGeoLine(
                    new GeoCoordinates(50.095075, 4.496925),
                    new GeoCoordinates(50.092157, 4.503523))
            };
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093302, 4.502491), new GeoCoordinates(50.093497, 4.502215)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093437, 4.501158), new GeoCoordinates(50.093185, 4.501241)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093593, 4.501783), new GeoCoordinates(50.093829, 4.501783)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 4, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093481, 4.500607), new GeoCoordinates(50.093486, 4.500028)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 5, Boundary = new CircuitGeoLine(new GeoCoordinates(50.092559, 4.500583), new GeoCoordinates(50.092798, 4.500382)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 6, Boundary = new CircuitGeoLine(new GeoCoordinates(50.092820, 4.499880), new GeoCoordinates(50.092578, 4.500027)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 7, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093325, 4.499743), new GeoCoordinates(50.093551, 4.499703)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 8, Boundary = new CircuitGeoLine(new GeoCoordinates(50.092849, 4.499429), new GeoCoordinates(50.093091, 4.499252)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 9, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093027, 4.498665), new GeoCoordinates(50.093137, 4.499046)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 10, Boundary = new CircuitGeoLine(new GeoCoordinates(50.094012, 4.498636), new GeoCoordinates(50.093691, 4.498677)) });
            mariembourgTurns.Segments.Add(new CircuitSegment { Number = 11, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093760, 4.499548), new GeoCoordinates(50.093512, 4.499564)) });

            // Optional: dump to JSON for debugging
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "mariembourgturns.json"),
                JsonConvert.SerializeObject(mariembourgTurns, Formatting.Indented));

            var parser = new DelimitedDataReader(new DelimitedDataReaderConfiguration
            {
                ChannelsSignature = "\"Time",
                Delimiter = ',',
                TelemetryDevice = "Test"
            });
            parser.Load(CsvPath);

            var driverRace = parser.GetSessionData().First();

            var cfg = new LapTimerConfig { AutoStartDetection = true };
            var svc = new LapTimerService(new NullLogger<LapTimerService>(), cfg);
            svc.SetCircuit(mariembourgTurns);

            foreach (var geo in driverRace.TelemetryData.Cast<GeoTelemetryData>())
                svc.AddGeolocation(geo);

            var events = svc.ActiveSession.Events;

            Assert.Equal(155, events.Count);

            var expected = TimeSpan.FromMilliseconds(56381); // 00:00:56.381
            var actual = events
                .Where(x => x.LapNumber == 7 && x.Type == SessionEventType.Lap)
                .Select(x => x.Time)
                .First();

            var tolerance = TimeSpan.FromMilliseconds(1);
            Assert.True((actual - expected).Duration() <= tolerance,
                $"Expected {expected}, got {actual} (tolerance {tolerance}).");
        }
    }

    public sealed class Cooldown
    {
        [Fact]
        public void Global_Cooldown_Blocks_Second_Crossing_When_Too_Close()
        {
            var cfg = new LapTimerConfig
            {
                AutoStartDetection = true,
                MinimumTimeBetweenEvents = TimeSpan.FromSeconds(5)
            };
            var svc = NewService(cfg);
            svc.SetCircuit(MakeCircuit(segmentCount: 1));

            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var added = new List<SessionDataEvent>();
            svc.EventAdded += (_, e) => added.Add(e);

            // first passage produces Sector+Lap
            svc.AddGeolocation(Fix(0, -0.001, t0));
            svc.AddGeolocation(Fix(0, +0.001, t0.AddSeconds(1)));

            var countAfterFirst = added.Count;
            Assert.True(countAfterFirst >= 2);

            // second passage too soon, blocked by global cooldown
            svc.AddGeolocation(Fix(0, -0.001, t0.AddSeconds(2)));
            svc.AddGeolocation(Fix(0, +0.001, t0.AddSeconds(3)));

            Assert.Equal(countAfterFirst, added.Count);
        }

        [Fact]
        public void Circuit_SectorTimeout_Overrides_Global_Cooldown()
        {
            var cfg = new LapTimerConfig
            {
                AutoStartDetection = true,
                MinimumTimeBetweenEvents = TimeSpan.FromSeconds(5)
            };
            var svc = NewService(cfg);
            // SectorTimeout = 1s should allow another event after 2s
            svc.SetCircuit(MakeCircuit(segmentCount: 1, sectorTimeoutSeconds: 1));

            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var added = new List<SessionDataEvent>();
            svc.EventAdded += (_, e) => added.Add(e);

            svc.AddGeolocation(Fix(0, -0.001, t0));
            svc.AddGeolocation(Fix(0, +0.001, t0.AddSeconds(1)));

            var countAfterFirst = added.Count;

            // another crossing after 2s should be accepted
            svc.AddGeolocation(Fix(0, -0.001, t0.AddSeconds(3)));
            svc.AddGeolocation(Fix(0, +0.001, t0.AddSeconds(4)));

            Assert.True(added.Count > countAfterFirst);
        }
    }

    public sealed class SessionLifecycle
    {
        [Fact]
        public void CreateSession_Raises_SessionStarted_And_Sets_ActiveSession()
        {
            var svc = NewService();
            var circuit = MakeCircuit();
            svc.SetCircuit(circuit);

            DeviceSessionData started = null;
            svc.SessionStarted += (_, s) => started = s;

            var session = svc.CreateSession();

            Assert.Same(session, svc.ActiveSession);
            Assert.NotNull(started);
            Assert.Equal(circuit.Code, session.CircuitCode);
            Assert.True(svc.IsRunning);
        }

        [Fact]
        public void CloseSession_Raises_SessionEnded_And_Clears_ActiveSession()
        {
            var svc = NewService();
            svc.SetCircuit(MakeCircuit());

            DeviceSessionData ended = null;
            svc.SessionEnded += (_, s) => ended = s;

            var session = svc.CreateSession();
            var closed = svc.CloseSession();

            Assert.Same(session, closed);
            Assert.Same(session, ended);
            Assert.Null(svc.ActiveSession);
        }

        [Fact]
        public void AutoClose_On_Idle_SessionTimeout()
        {
            var cfg = new LapTimerConfig
            {
                AutoStartDetection = true,
                MinimumTimeBetweenEvents = TimeSpan.Zero,
                SessionTimeout = TimeSpan.FromSeconds(5)
            };
            var svc = NewService(cfg);
            svc.SetCircuit(MakeCircuit(segmentCount: 1));

            DeviceSessionData ended = null;
            svc.SessionEnded += (_, s) => ended = s;

            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

            // first crossing to create events and initialize LastEvent
            svc.AddGeolocation(Fix(0, -0.001, t0));
            svc.AddGeolocation(Fix(0, +0.001, t0.AddSeconds(1)));

            Assert.NotNull(svc.ActiveSession);

            // new fix far in the future should trigger auto close
            svc.AddGeolocation(Fix(0, +0.002, t0.AddSeconds(20)));

            Assert.Null(svc.ActiveSession);
            Assert.NotNull(ended);
        }
    }

    public sealed class RealDataIntegration
    {
        private static string AssetCsvPath =>
                Path.Combine(AppContext.BaseDirectory, "runs", "MariembourgFreeTests.csv");

        [Fact]
        public void LapTimerService_Produces_Expected_Events_And_Lap_Time_On_Mariembourg_FreeTests_CSV()
        {
            Assert.True(File.Exists(AssetCsvPath), $"CSV not found at {AssetCsvPath}");

            var parser = new DelimitedDataReader(new DelimitedDataReaderConfiguration
            {
                ChannelsSignature = "\"Time",
                Delimiter = ',',
                TelemetryDevice = "Test"
            });
            parser.Load(AssetCsvPath);

            var driverSessionData = parser.GetSessionData();
            var driverRace = driverSessionData.First();

            var config = new LapTimerConfig(); // defaults are fine
            var logger = new NullLogger<LapTimerService>();

            var circuitService = new StaticCircuitService();
            var circuit = circuitService.Detect(driverRace.TelemetryData.First() as GeoTelemetryData).Result;
            Assert.NotNull(circuit);

            var lapTimer = new LapTimerService(logger, config);
            lapTimer.SetCircuit(circuit);
            lapTimer.StartDetection();

            foreach (var geo in driverRace.TelemetryData.Cast<GeoTelemetryData>())
            {
                lapTimer.AddGeolocation(geo);
            }

            var events = lapTimer.ActiveSession.Events;

            // total events expected from this dataset
            Assert.Equal(52, events.Count);

            // lap 3 time expected to be 00:00:57.286 with tolerance 1 ms
            var expected = TimeSpan.FromMilliseconds(57286);
            var actual = events
                .Where(x => x.LapNumber == 3 && x.Type == SessionEventType.Lap)
                .Select(x => x.Time)
                .First();

            var tolerance = TimeSpan.FromMilliseconds(1);
            var diff = (actual - expected).Duration();

            Assert.True(diff <= tolerance, $"Expected {expected}, got {actual} (tolerance {tolerance}).");
        }
    }
}
