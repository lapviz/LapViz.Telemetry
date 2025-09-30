using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Tests.Domain;

public class TelemetryDomainAggregateTests
{
    private static GeoCoordinates G(double lat, double lon, double alt = 0) => new GeoCoordinates(lat, lon, alt);

    public sealed class GeoCoordinatesTests
    {
        [Fact]
        public void Default_Ctor_Sets_Zeroes()
        {
            var g = new GeoCoordinates();
            Assert.Equal(0, g.Latitude);
            Assert.Equal(0, g.Longitude);
            Assert.Equal(0, g.Altitude);
        }

        [Fact]
        public void Param_Ctor_Assigns_Values()
        {
            var g = new GeoCoordinates(50.1, 5.2, 123.4);
            Assert.Equal(50.1, g.Latitude, 12);
            Assert.Equal(5.2, g.Longitude, 12);
            Assert.Equal(123.4, g.Altitude, 12);
        }

        [Fact]
        public void ToString_Is_Invariant_And_Contains_Space()
        {
            var g = new GeoCoordinates(0, 0);
            Assert.Equal("0, 0", g.ToString());
        }

        [Fact]
        public void DistanceTo_Units_Are_Consistent()
        {
            var a = G(0, 0);
            var b = G(0, 1);

            var dM = a.DistanceTo(b, 'M'); // miles (base)
            var dK = a.DistanceTo(b, 'K'); // kilometers
            var dN = a.DistanceTo(b, 'N'); // nautical miles

            Assert.Equal(dK, dM * 1.609344, 6);
            Assert.Equal(dN, dM * 0.8684, 6);
            Assert.InRange(dK, 110.0, 112.5);
        }

        [Fact]
        public void Clone_Creates_Independent_Copy()
        {
            var g1 = new GeoCoordinates(10, 20, 30);
            var g2 = (GeoCoordinates)g1.Clone();

            Assert.NotSame(g1, g2);
            Assert.Equal(g1.Latitude, g2.Latitude);
            Assert.Equal(g1.Longitude, g2.Longitude);
            Assert.Equal(g1.Altitude, g2.Altitude);

            g2.Latitude = 11;
            Assert.NotEqual(g1.Latitude, g2.Latitude);
        }
    }

    public sealed class GeoTelemetryDataTests
    {
        [Fact]
        public void Default_Ctor_Initializes_Data_List()
        {
            var d = new GeoTelemetryData();
            Assert.NotNull(d.Data);
            Assert.Empty(d.Data);
        }

        [Fact]
        public void LatLon_Ctor_Assigns_Values()
        {
            var d = new GeoTelemetryData(50.123, 5.456);
            Assert.Equal(50.123, d.Latitude, 12);
            Assert.Equal(5.456, d.Longitude, 12);
        }

        [Fact]
        public void ToString_Contains_Comma_Without_Space()
        {
            var d = new GeoTelemetryData(1.5, 2.5);
            var s = d.ToString();
            Assert.Contains(",", s);
            Assert.DoesNotContain(", ", s);
        }
    }

    public sealed class SessionDataEventTests
    {
        [Fact]
        public void Data_Vector_Maps_Properties_In_Documented_Order()
        {
            var evt = new SessionDataEvent
            {
                Timestamp = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero),
                Type = SessionEventType.Sector,
                LapNumber = 3,
                Sector = 2,
                Time = TimeSpan.FromMilliseconds(1234),
                FirstGeoCoordinates = G(50.1, 5.2),
                SecondGeoCoordinates = G(50.2, 5.3),
                Factor = 0.42
            };

            var v = evt.Data; // IList<double?>

            // [ Type, LapNumber, Sector, Time(ms), First.lat, First.lon, Second.lat, Second.lon, Factor ]

            // Use GetValueOrDefault() to unwrap nullable before precision overloads
            Assert.Equal((double)SessionEventType.Sector, v[0].GetValueOrDefault(), 12);

            // Lap/Sector are stored as doubles in Data; assert as ints after unwrapping
            Assert.Equal(3, (int)v[1].GetValueOrDefault());
            Assert.Equal(2, (int)v[2].GetValueOrDefault());

            // Time(ms) is a double
            Assert.Equal(1234d, v[3].GetValueOrDefault(), 12);

            // Coordinates + factor
            Assert.Equal(50.1, v[4].GetValueOrDefault(), 12);
            Assert.Equal(5.2, v[5].GetValueOrDefault(), 12);
            Assert.Equal(50.2, v[6].GetValueOrDefault(), 12);
            Assert.Equal(5.3, v[7].GetValueOrDefault(), 12);
            Assert.Equal(0.42, v[8].GetValueOrDefault(), 12);
        }

        [Fact]
        public void ToString_Includes_Type_And_Info()
        {
            var evt = new SessionDataEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Type = SessionEventType.Lap,
                LapNumber = 7,
                Time = TimeSpan.FromSeconds(65),
                DeviceId = "dev",
                SessionId = "sess"
            };

            var s = evt.ToString();
            Assert.Contains("Lap", s);
            Assert.Contains("7", s);
            Assert.Contains("dev", s);
            Assert.Contains("sess", s);
        }

        [Fact]
        public void Clone_Performs_Deep_Copy_For_Coordinates_And_Arrays()
        {
            var evt = new SessionDataEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Type = SessionEventType.Sector,
                LapNumber = 1,
                Sector = 2,
                Time = TimeSpan.FromMilliseconds(500),
                FirstGeoCoordinates = G(1, 2),
                SecondGeoCoordinates = G(3, 4),
                UserId = "u",
                DeviceId = "d",
                Factor = 0.5,
                CircuitCode = "c",
                SessionId = "s",
                IsBestOverall = true,
                IsPersonnalBest = true,
                DataMin = new List<double?> { 1, 2 },
                DataMax = new List<double?> { 10, 20 },
                Deleted = DateTime.UtcNow
            };

            var copy = (SessionDataEvent)evt.Clone();

            Assert.NotSame(evt, copy);
            Assert.NotSame(evt.FirstGeoCoordinates, copy.FirstGeoCoordinates);
            Assert.NotSame(evt.SecondGeoCoordinates, copy.SecondGeoCoordinates);
            Assert.Equal(evt.FirstGeoCoordinates.Latitude, copy.FirstGeoCoordinates.Latitude);
            Assert.Equal(evt.SecondGeoCoordinates.Longitude, copy.SecondGeoCoordinates.Longitude);
            Assert.NotSame(evt.DataMin, copy.DataMin);
            Assert.NotSame(evt.DataMax, copy.DataMax);
            Assert.Equal(evt.DataMin, copy.DataMin);
            Assert.Equal(evt.DataMax, copy.DataMax);

            Assert.Same(evt.DriverRace, copy.DriverRace); // shallow copy by design
        }
    }

    public sealed class SessionEventsTests
    {
        private static SessionDataEvent Lap(int lap, int secCount, DateTimeOffset t0, double ms)
            => new SessionDataEvent
            {
                Type = SessionEventType.Lap,
                LapNumber = lap,
                Sector = 0,
                Time = TimeSpan.FromMilliseconds(ms),
                Timestamp = t0.AddMilliseconds(ms)
            };

        private static SessionDataEvent Sec(int lap, int sec, DateTimeOffset ts, double ms)
            => new SessionDataEvent
            {
                Type = SessionEventType.Sector,
                LapNumber = lap,
                Sector = sec,
                Time = TimeSpan.FromMilliseconds(ms),
                Timestamp = ts.AddMilliseconds(ms)
            };

        [Fact]
        public void AddEvent_LastEvent_LastLap_BestLap_Work_As_Expected()
        {
            var evts = new SessionEvents();
            var t0 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

            evts.AddEvent(Lap(1, 3, t0, 60000));
            evts.AddEvent(Lap(2, 3, t0, 58000));

            Assert.Equal(2, evts.LastLap.LapNumber);
            Assert.Equal(58000, evts.BestLap.Time.TotalMilliseconds);
            Assert.Same(evts.Events.Last(), evts.LastEvent);
        }

        [Fact]
        public void LastSector_Picks_Greatest_Lap_Then_Greatest_Sector_With_Positive_Time()
        {
            var evts = new SessionEvents();
            var t = DateTimeOffset.UtcNow;

            evts.AddEvent(Sec(1, 1, t, 1000));
            evts.AddEvent(Sec(1, 2, t, 1100));
            evts.AddEvent(Sec(2, 1, t, 900));
            evts.AddEvent(Sec(2, 2, t, 0));
            evts.AddEvent(Sec(2, 3, t, 1200));

            Assert.Equal(3, evts.LastSector.Sector);
            Assert.Equal(2, evts.LastSector.LapNumber);
        }

        [Fact]
        public void BestSectors_Theorical_Rolling_Computations_Are_Correct()
        {
            var evts = new SessionEvents();

            var base1 = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var base2 = base1.AddMinutes(1); // ensure lap 2 sectors occur after lap 1 in time
            var base3 = base2.AddMinutes(1); // ensure lap 3 sectors occur after lap 2

            SessionDataEvent SecAt(DateTimeOffset b, int lap, int sec, double ms) => new SessionDataEvent
            {
                Type = SessionEventType.Sector,
                LapNumber = lap,
                Sector = sec,
                Time = TimeSpan.FromMilliseconds(ms),
                Timestamp = b.AddMilliseconds(ms)
            };

            SessionDataEvent LapAt(DateTimeOffset b, int lap, double ms) => new SessionDataEvent
            {
                Type = SessionEventType.Lap,
                LapNumber = lap,
                Time = TimeSpan.FromMilliseconds(ms),
                Timestamp = b.AddMilliseconds(ms)
            };

            // Lap 1: 10s, 11s, 12s
            evts.AddEvent(SecAt(base1, 1, 1, 10000));
            evts.AddEvent(SecAt(base1, 1, 2, 11000));
            evts.AddEvent(SecAt(base1, 1, 3, 12000));
            evts.AddEvent(LapAt(base1, 1, 33000));

            // Lap 2: 9s, 13s, 14s
            evts.AddEvent(SecAt(base2, 2, 1, 9000));
            evts.AddEvent(SecAt(base2, 2, 2, 13000));
            evts.AddEvent(SecAt(base2, 2, 3, 14000));
            evts.AddEvent(LapAt(base2, 2, 36000));

            // Lap 3: 10s, 10s, 10s
            evts.AddEvent(SecAt(base3, 3, 1, 10000));
            evts.AddEvent(SecAt(base3, 3, 2, 10000));
            evts.AddEvent(SecAt(base3, 3, 3, 10000));
            evts.AddEvent(LapAt(base3, 3, 30000));

            var best = evts.BestSectors;
            Assert.Equal(TimeSpan.FromSeconds(9), best[1].Time);
            Assert.Equal(TimeSpan.FromSeconds(10), best[2].Time);
            Assert.Equal(TimeSpan.FromSeconds(10), best[3].Time);

            // Theoretical: 9 + 10 + 10 = 29s
            Assert.Equal(TimeSpan.FromSeconds(29), evts.Theorical);

            // Rolling (3 consecutive in chronological order): 10+10+10 = 30s
            Assert.Equal(TimeSpan.FromSeconds(30), evts.Rolling);
        }

        [Fact]
        public void IsBestSector_Returns_True_When_No_Best_Yet_Or_Improves()
        {
            var evts = new SessionEvents();

            var s1 = new SessionDataEvent
            {
                Type = SessionEventType.Sector,
                LapNumber = 1,
                Sector = 1,
                Time = TimeSpan.FromMilliseconds(1000)
            };

            Assert.True(evts.IsBestSector(s1));
            evts.AddEvent(s1);

            var s2 = new SessionDataEvent
            {
                Type = SessionEventType.Sector,
                LapNumber = 2,
                Sector = 1,
                Time = TimeSpan.FromMilliseconds(900)
            };

            Assert.True(evts.IsBestSector(s2));

            var s3 = new SessionDataEvent
            {
                Type = SessionEventType.Sector,
                LapNumber = 3,
                Sector = 1,
                Time = TimeSpan.FromMilliseconds(1100)
            };

            Assert.False(evts.IsBestSector(s3));
        }
    }

    public sealed class SessionDataTests
    {
        [Fact]
        public void AddEvent_Dispatches_To_PerDevice_And_Aggregates_BestLap()
        {
            var session = new SessionData();

            session.AddEvent(new SessionDataEvent
            {
                DeviceId = "A",
                Type = SessionEventType.Lap,
                LapNumber = 1,
                Time = TimeSpan.FromSeconds(61),
                Timestamp = DateTimeOffset.UtcNow
            });
            session.AddEvent(new SessionDataEvent
            {
                DeviceId = "A",
                Type = SessionEventType.Lap,
                LapNumber = 2,
                Time = TimeSpan.FromSeconds(59),
                Timestamp = DateTimeOffset.UtcNow.AddSeconds(1)
            });
            session.AddEvent(new SessionDataEvent
            {
                DeviceId = "B",
                Type = SessionEventType.Lap,
                LapNumber = 1,
                Time = TimeSpan.FromSeconds(58),
                Timestamp = DateTimeOffset.UtcNow.AddSeconds(2)
            });

            Assert.Equal(TimeSpan.FromSeconds(58), session.BestLap);
            Assert.True(session.Devices.ContainsKey("A"));
            Assert.True(session.Devices.ContainsKey("B"));
        }

        [Fact]
        public void BestSectors_Aggregates_Minimum_Per_Sector_Across_Devices()
        {
            var session = new SessionData();

            session.AddEvent(new SessionDataEvent { DeviceId = "A", Type = SessionEventType.Sector, LapNumber = 1, Sector = 1, Time = TimeSpan.FromMilliseconds(1000), Timestamp = DateTimeOffset.UtcNow });
            session.AddEvent(new SessionDataEvent { DeviceId = "A", Type = SessionEventType.Sector, LapNumber = 1, Sector = 2, Time = TimeSpan.FromMilliseconds(1200), Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(1) });

            session.AddEvent(new SessionDataEvent { DeviceId = "B", Type = SessionEventType.Sector, LapNumber = 1, Sector = 1, Time = TimeSpan.FromMilliseconds(900), Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(2) });
            session.AddEvent(new SessionDataEvent { DeviceId = "B", Type = SessionEventType.Sector, LapNumber = 1, Sector = 2, Time = TimeSpan.FromMilliseconds(1300), Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(3) });

            var best = session.BestSectors;
            Assert.Equal(TimeSpan.FromMilliseconds(900), best[1]);
            Assert.Equal(TimeSpan.FromMilliseconds(1200), best[2]);
        }

        [Fact]
        public void GetOrCreateDevice_Throws_On_Empty_And_WithDevice_Executes_Action()
        {
            var session = new SessionData();

            Assert.Throws<ArgumentException>(() => session.GetOrCreateDevice("  "));

            bool touched = false;
            session.WithDevice("D1", dev => touched = true);

            Assert.True(touched);
            Assert.True(session.Devices.ContainsKey("D1"));
        }
    }

    public sealed class WeatherInfoTests
    {
        [Fact]
        public void Roundtrip_Properties_Work()
        {
            var w = new WeatherInfo
            {
                Temperature = 22.5,
                Unit = TemperatureUnit.Celsius,
                Humidity = 55,
                Pressure = 1013,
                Precipitation = 0.2,
                WindSpeed = 12.3,
                CloudCover = 30,
                Conditions = "Sunny",
                Icon = "sunny.png"
            };

            Assert.Equal(22.5, w.Temperature);
            Assert.Equal(TemperatureUnit.Celsius, w.Unit);
            Assert.Equal(55, w.Humidity);
            Assert.Equal(1013, w.Pressure);
            Assert.Equal(0.2, w.Precipitation);
            Assert.Equal(12.3, w.WindSpeed);
            Assert.Equal(30, w.CloudCover);
            Assert.Equal("Sunny", w.Conditions);
            Assert.Equal("sunny.png", w.Icon);
        }
    }
}
