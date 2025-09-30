using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Tests.Domain;

public class CircuitGeoLineTests
{
    private static GeoCoordinates G(double lat, double lon) => new GeoCoordinates(lat, lon);

    public sealed class Ctor
    {
        [Fact]
        public void Throws_When_Start_Is_Null()
        {
            Assert.Throws<ArgumentNullException>(() => new CircuitGeoLine(null, G(0, 0)));
        }

        [Fact]
        public void Throws_When_End_Is_Null()
        {
            Assert.Throws<ArgumentNullException>(() => new CircuitGeoLine(G(0, 0), null));
        }

        [Fact]
        public void Stores_Start_And_End()
        {
            var a = G(1, 2);
            var b = G(3, 4);
            var l = new CircuitGeoLine(a, b);

            Assert.Same(a, l.Start);
            Assert.Same(b, l.End);
        }
    }

    public sealed class Center_And_Length
    {
        [Fact]
        public void Center_Is_Average_Of_Endpoints()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 4));
            var c = l.Center;

            Assert.Equal(1.0, c.Latitude, 12);
            Assert.Equal(2.0, c.Longitude, 12);
        }

        [Fact]
        public void LengthMeters_Is_Consistent_With_Geodesic_Distance()
        {
            // ~0.001 degree of latitude ~ 111.2 meters on Earth
            var l = new CircuitGeoLine(G(0.000, 0.0), G(0.001, 0.0));
            var len = l.LengthMeters;

            Assert.InRange(len, 100.0, 120.0);
        }
    }

    public sealed class BoundingBox
    {
        [Fact]
        public void IsWithinBox_Returns_True_For_Point_Inside()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 3));
            var p = new GeoTelemetryData { Latitude = 1, Longitude = 2 };

            Assert.True(l.IsWithinBox(p));
        }

        [Fact]
        public void IsWithinBox_Is_Inclusive_On_Boundaries()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 3));

            // Exactly at Start
            Assert.True(l.IsWithinBox(new GeoTelemetryData { Latitude = 0, Longitude = 0 }));
            // Exactly at End
            Assert.True(l.IsWithinBox(new GeoTelemetryData { Latitude = 2, Longitude = 3 }));
            // Edge on min/max lines
            Assert.True(l.IsWithinBox(new GeoTelemetryData { Latitude = 0, Longitude = 2 }));
            Assert.True(l.IsWithinBox(new GeoTelemetryData { Latitude = 2, Longitude = 1 }));
        }

        [Fact]
        public void IsWithinBox_Returns_False_Outside()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 3));
            Assert.False(l.IsWithinBox(new GeoTelemetryData { Latitude = -0.1, Longitude = 1 }));
            Assert.False(l.IsWithinBox(new GeoTelemetryData { Latitude = 1, Longitude = 3.1 }));
        }
    }

    public sealed class Projection_And_Parameter
    {
        [Fact]
        public void ProjectionFactor_Along_Segment_Ranges_0_To_1()
        {
            var l = new CircuitGeoLine(G(0, 0), G(1, 0)); // along latitude axis
            Assert.Equal(0.0, l.ProjectionFactor(G(0, 0)), 12);
            Assert.Equal(1.0, l.ProjectionFactor(G(1, 0)), 12);
            Assert.Equal(0.5, l.ProjectionFactor(G(0.5, 0)), 12);
        }

        [Fact]
        public void ProjectionFactor_Clamps_Outside_Segment()
        {
            var l = new CircuitGeoLine(G(0, 0), G(1, 0));
            Assert.Equal(0.0, l.ProjectionFactor(G(-1, 0)), 12);
            Assert.Equal(1.0, l.ProjectionFactor(G(2, 0)), 12);
        }

        [Fact]
        public void ProjectionFactor_Degenerate_StartEqualsEnd_Returns_0()
        {
            var l = new CircuitGeoLine(G(1, 1), G(1, 1));
            Assert.Equal(0.0, l.ProjectionFactor(G(2, 2)), 12);
        }

        [Fact]
        public void ParameterOf_Matches_ProjectionFactor()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 2)); // diagonal
            var pts = new[] { G(0, 0), G(1, 1), G(2, 2), G(3, 3), G(-1, -1) };

            foreach (var p in pts)
            {
                var t1 = l.ProjectionFactor(p);
                var t2 = l.ParameterOf(p);
                Assert.Equal(t1, t2, 12);
                Assert.InRange(t2, 0.0, 1.0);
            }
        }
    }

    public sealed class CenterFactorTests
    {
        [Fact]
        public void CenterFactor_Is_1_At_Midpoint()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 0));
            var mid = G(1, 0);

            Assert.Equal(1.0, l.CenterFactor(mid), 9);
        }

        [Fact]
        public void CenterFactor_Is_0_At_Endpoints()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 0));
            Assert.Equal(0.0, l.CenterFactor(G(0, 0)), 12);
            Assert.Equal(0.0, l.CenterFactor(G(2, 0)), 12);
        }

        [Fact]
        public void CenterFactor_Is_Between_0_And_1_Otherwise_And_Clamped()
        {
            var l = new CircuitGeoLine(G(0, 0), G(2, 0));
            var nearStart = G(0.2, 0);
            var nearEnd = G(1.8, 0);

            var f1 = l.CenterFactor(nearStart);
            var f2 = l.CenterFactor(nearEnd);

            Assert.InRange(f1, 0.0, 1.0);
            Assert.InRange(f2, 0.0, 1.0);
        }

        [Fact]
        public void CenterFactor_Degenerate_When_P_Equals_Both_Endpoints_Returns_0()
        {
            var a = G(1, 1);
            var l = new CircuitGeoLine(a, a);
            // p coincides with both endpoints -> sum == 0 -> returns 0.0
            Assert.Equal(0.0, l.CenterFactor(a), 12);
        }
    }

    public sealed class Intersection
    {
        [Fact]
        public void Intersect_Returns_Point_For_Proper_Crossing()
        {
            // Horizontal: (0,0) -> (1,0), Vertical: (0.5,-1) -> (0.5,1)
            var a = new CircuitGeoLine(G(0, 0), G(1, 0));
            var b = new CircuitGeoLine(G(0.5, -1), G(0.5, 1));

            var p = a.Intersect(b, CrossingFilter.Any);
            Assert.NotNull(p);
            Assert.Equal(0.5, p.Latitude, 12);
            Assert.Equal(0.0, p.Longitude, 12);
        }

        [Fact]
        public void Intersect_ClosedInterval_Check_Rejects_OutOfRange()
        {
            // a vertical from (0,0) to (0,1)
            var a = new CircuitGeoLine(G(0, 0), G(0, 1));
            // b horizontal at y=2 from x=-1 to x=1; would intersect at (0,2) if unbounded
            var b = new CircuitGeoLine(G(-1, 2), G(1, 2));

            var p = a.Intersect(b, CrossingFilter.Any);
            Assert.Null(p);
        }

        [Fact]
        public void Intersect_Parallel_Or_Collinear_Returns_Null()
        {
            // Parallel horizontals
            var a = new CircuitGeoLine(G(0, 0), G(1, 0));
            var b = new CircuitGeoLine(G(0, 1), G(1, 1));
            Assert.Null(a.Intersect(b, CrossingFilter.Any));

            // Collinear overlapping segments (legacy behavior returns null)
            var c = new CircuitGeoLine(G(0, 0), G(1, 0));
            var d = new CircuitGeoLine(G(0.5, 0), G(2, 0));
            Assert.Null(c.Intersect(d, CrossingFilter.Any));
        }

        [Fact]
        public void Intersect_Directional_Gating_TowardApex_Rejects_When_Denominator_Negative()
        {
            // a: (0,0)->(1,0); b: (0.5,-1)->(0.5,1)
            // denominator = dy12*dx34 - dx12*dy34 = 0*0 - 1*2 = -2 < 0
            var a = new CircuitGeoLine(G(0, 0), G(1, 0));
            var b = new CircuitGeoLine(G(0.5, -1), G(0.5, 1));

            Assert.Null(a.Intersect(b, CrossingFilter.TowardApex)); // gated out
            Assert.NotNull(a.Intersect(b, CrossingFilter.AwayFromApex)); // allowed
        }

        [Fact]
        public void Intersect_Directional_Gating_AwayFromApex_Rejects_When_Denominator_Positive()
        {
            // Flip one segment to get positive denominator:
            // a: (1,0)->(0,0); b: (0.5,-1)->(0.5,1)
            // denominator = dy12*dx34 - dx12*dy34 = 0*0 - (-1)*2 = +2 > 0
            var a = new CircuitGeoLine(G(1, 0), G(0, 0));
            var b = new CircuitGeoLine(G(0.5, -1), G(0.5, 1));

            Assert.Null(a.Intersect(b, CrossingFilter.AwayFromApex)); // gated out
            Assert.NotNull(a.Intersect(b, CrossingFilter.TowardApex)); // allowed
        }
    }
}
