using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.Services;

namespace LapViz.Telemetry.Tests.Services;

public class StaticCircuitServiceTests
{
    private static CircuitConfiguration MakeCircuit(
        string code,
        double minLat, double minLon,
        double maxLat, double maxLon,
        string name = "Test",
        bool testFlag = false)
    {
        return new CircuitConfiguration
        {
            Name = name ?? code,
            Code = code,
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(maxLat, minLon), new GeoCoordinates(minLat, maxLon)),
            UseDirection = false,
            Center = new GeoCoordinates((minLat + maxLat) / 2.0, (minLon + maxLon) / 2.0),
            Zoom = 16,
            Test = testFlag
        };
    }

    private static readonly DateTimeOffset FixedTs = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static GeoTelemetryData Gps(double lat, double lon)
        => new GeoTelemetryData { Latitude = lat, Longitude = lon, Timestamp = FixedTs };


    public sealed class Ctor
    {
        [Fact]
        public void Default_BuildsBuiltInDataset_And_Index()
        {
            var svc = new StaticCircuitService();
            Assert.NotNull(svc);
            Assert.Equal(new DateTimeOffset(2023, 6, 21, 10, 30, 25, TimeSpan.Zero), svc.Updated);

            // Sanity: some well-known circuits must exist
            Assert.NotNull(svc.GetByCode("mettet"));
            Assert.NotNull(svc.GetByCode("zolder"));
        }

        [Fact]
        public void SingleCircuit_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new StaticCircuitService((CircuitConfiguration)null));
        }

        [Fact]
        public void Enumerable_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new StaticCircuitService((IEnumerable<CircuitConfiguration>)null));
        }

        [Fact]
        public void Enumerable_BuildsIndex_IgnoresNullsAndEmptyCodes_LastOneWins()
        {
            var a = MakeCircuit("abc", 50.0, 5.0, 50.01, 5.01);
            var b = MakeCircuit("", 51.0, 6.0, 51.01, 6.01);          // ignored
            CircuitConfiguration c = null;                             // ignored
            var aPrime = MakeCircuit("ABC", 52.0, 7.0, 52.01, 7.01);   // overwrites a because comparer is case-insensitive

            var svc = new StaticCircuitService(new[] { a, b, c, aPrime });

            // empty or null codes are ignored
            Assert.Null(svc.GetByCode("").Result);

            // last one wins behavior
            var found = svc.GetByCode("abc").Result; // should be aPrime
            Assert.NotNull(found);
            Assert.Equal(aPrime.Center.Latitude, found.Center.Latitude);
            Assert.Equal(aPrime.Center.Longitude, found.Center.Longitude);
        }
    }
    public sealed class GetByCode
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NullOrWhitespace_ReturnsNull(string code)
        {
            var svc = new StaticCircuitService(new List<CircuitConfiguration>());
            Assert.Null(svc.GetByCode(code).Result);
        }

        [Fact]
        public void IsCaseInsensitive()
        {
            var c = MakeCircuit("Genk", 50.985, 5.561, 50.990, 5.568);
            var svc = new StaticCircuitService(new[] { c });

            Assert.Same(c, svc.GetByCode("genk").Result);
            Assert.Same(c, svc.GetByCode("GENK").Result);
            Assert.Same(c, svc.GetByCode("GeNk").Result);
            Assert.Equal("Genk", svc.GetByCode("GENK").Result.Code);
        }

        [Fact]
        public void ReturnsNull_WhenNotFound()
        {
            var svc = new StaticCircuitService(new List<CircuitConfiguration>());
            Assert.Null(svc.GetByCode("does-not-exist").Result);
        }

    }

    public sealed class Detect
    {
        [Fact]
        public void ReturnsNull_WhenGeoNull()
        {
            var svc = new StaticCircuitService(new List<CircuitConfiguration>());
            Assert.Null(svc.Detect(null).Result);
        }

        [Fact]
        public void ReturnsNull_WhenNoCircuits()
        {
            var svc = new StaticCircuitService(new List<CircuitConfiguration>());
            Assert.Null(svc.Detect(Gps(50.0, 5.0)).Result);
        }

        [Fact]
        public void ReturnsCircuit_WhenPointInsideBoundingBox()
        {
            var c = MakeCircuit("box", 50.000, 5.000, 50.010, 5.010);
            var svc = new StaticCircuitService(new[] { c });

            var inside = Gps(50.005, 5.005);
            var found = svc.Detect(inside).Result;

            Assert.Same(c, found);
        }

        [Fact]
        public void ReturnsNull_WhenPointOutsideAllBoundingBoxes()
        {
            var c = MakeCircuit("box", 50.000, 5.000, 50.010, 5.010);
            var svc = new StaticCircuitService(new[] { c });

            var outside = Gps(49.999, 4.999);
            var found = svc.Detect(outside).Result;

            Assert.Null(found);
        }

        [Fact]
        public void RespectsOrder_FirstMatchWins_WhenBoxesOverlap()
        {
            // Overlapping boxes, different codes, order matters
            var first = MakeCircuit("first", 50.000, 5.000, 50.010, 5.010);
            var second = MakeCircuit("second", 50.005, 5.005, 50.015, 5.015);
            var svc1 = new StaticCircuitService(new[] { first, second });
            var svc2 = new StaticCircuitService(new[] { second, first });

            var pointInOverlap = Gps(50.007, 5.007);

            Assert.Same(first, svc1.Detect(pointInOverlap).Result);
            Assert.Same(second, svc2.Detect(pointInOverlap).Result);
        }

        [Fact]
        public void GivenCoordinateInsideNandrinCircuit_ReturnsNandrinTest5()
        {
            ICircuitService circuitService = new StaticCircuitService();
            CircuitConfiguration circuit = circuitService.Detect(new GeoTelemetryData(50.5018664494456, 5.42528468242706)).Result;

            Assert.Equal("nandrintest5", circuit.Code);
        }

        [Fact]
        public void ReturnsNull_WhenOnlyCircuitsWithoutBoundingBox()
        {
            var withoutBox = new CircuitConfiguration { Code = "nobox", BoundingBox = null };
            var svc = new StaticCircuitService(new[] { withoutBox });

            Assert.Null(svc.Detect(Gps(50.0, 5.0)).Result);
        }

    }

    public sealed class Sync
    {
        [Fact]
        public void RaisesProgressEvent_WithProgressEquals1()
        {
            var c = MakeCircuit("any", 50.0, 5.0, 50.01, 5.01);
            var svc = new StaticCircuitService(new[] { c });

            CircuitSyncProgress received = null;
            svc.SyncProgress += (s, e) => received = e;

            svc.Sync(0, 0);

            Assert.NotNull(received);
            Assert.Equal(1, received.Progress);
        }
    }

    public sealed class Updated
    {
        [Fact]
        public void IsFixedTimestamp_ForCompatibility()
        {
            var svc = new StaticCircuitService(new List<CircuitConfiguration>());
            var expected = new DateTimeOffset(2023, 6, 21, 10, 30, 25, TimeSpan.Zero);
            Assert.Equal(expected, svc.Updated);
        }
    }
}
