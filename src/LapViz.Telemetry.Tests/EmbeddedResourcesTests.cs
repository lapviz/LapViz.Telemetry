using LapViz.Telemetry.Domain;
using LapViz.Telemetry.Sensors;
using LapViz.Telemetry.Services;

namespace LapViz.Telemetry.Tests;

public class EmbeddedResourcesTests
{
    [Fact]
    public void Builtin_Circuits_Are_Loaded_From_Embedded_Json()
    {
        var circuits = new StaticCircuitService().InitializeCircuits();

        Assert.Equal(189, circuits.Count);
        Assert.All(circuits, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.Code));
            Assert.False(string.IsNullOrEmpty(c.CountryCode));
            Assert.False(string.IsNullOrEmpty(c.Location));
            Assert.NotNull(c.BoundingBox);
            Assert.NotEmpty(c.Segments);
        });

        var mettet = circuits.Single(c => c.Code == "mettet");
        Assert.Equal("Mettet", mettet.Name);
        Assert.Equal(CircuitType.Closed, mettet.Type);
        Assert.True(mettet.UseDirection);
        Assert.Equal(17, mettet.Zoom);
        Assert.Equal(3, mettet.Segments.Count);
        Assert.Equal(50.300443, mettet.Segments[2].Boundary.Start.Latitude);
        Assert.Equal(4.654117, mettet.Segments[2].Boundary.End.Longitude);
        Assert.Equal("circuits/mettet", mettet.Id);
        Assert.Equal("be", mettet.CountryCode);
    }

    /// <summary>
    /// Known errors of the dataset, to fix with real coordinates: 120 (Hockenheim) has both
    /// box corners equal, 209 (Brands Hatch industrial) has the start line of 208 (Parc Blyton).
    /// </summary>
    private static readonly HashSet<string> KnownBadGeometry = new HashSet<string> { "120", "209" };

    [Fact]
    public void Builtin_Circuits_Have_A_Consistent_Geometry()
    {
        const double margin = 0.002; // degrees: lines may sit on the edge of the box

        var problems = new List<string>();
        foreach (var circuit in new StaticCircuitService().InitializeCircuits())
        {
            if (KnownBadGeometry.Contains(circuit.Code))
                continue;

            var box = circuit.BoundingBox;
            double minLat = Math.Min(box.Start.Latitude, box.End.Latitude), maxLat = Math.Max(box.Start.Latitude, box.End.Latitude);
            double minLon = Math.Min(box.Start.Longitude, box.End.Longitude), maxLon = Math.Max(box.Start.Longitude, box.End.Longitude);

            if (maxLat - minLat < 1e-5 || maxLon - minLon < 1e-5)
                problems.Add($"{circuit.Code} {circuit.Name}: empty box");

            var numbers = circuit.Segments.Select(s => s.Number).OrderBy(n => n).ToList();
            if (!numbers.SequenceEqual(Enumerable.Range(1, numbers.Count)))
                problems.Add($"{circuit.Code} {circuit.Name}: segments not numbered 1..N");

            foreach (var segment in circuit.Segments)
            {
                foreach (var point in new[] { segment.Boundary.Start, segment.Boundary.End })
                {
                    if (point.Latitude < minLat - margin || point.Latitude > maxLat + margin ||
                        point.Longitude < minLon - margin || point.Longitude > maxLon + margin)
                    {
                        problems.Add($"{circuit.Code} {circuit.Name}: line {segment.Number} outside the box");
                        break;
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void InitializeCircuits_Returns_New_Instances()
    {
        var service = new StaticCircuitService();
        Assert.NotSame(service.InitializeCircuits()[0], service.InitializeCircuits()[0]);
    }

    [Fact]
    public async Task SimulatorGps_Replays_The_Compressed_Embedded_Session()
    {
        using var simulator = new SimulatorGps();
        var received = new TaskCompletionSource<GeoDataReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        simulator.DataReceived += (_, e) => received.TrySetResult(e);
        simulator.Error += (_, e) => received.TrySetException(e);

        simulator.Start();
        var first = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.InRange(first.Message.Latitude, 50.9, 51.1);   // Genk, Belgium
        Assert.InRange(first.Message.Longitude, 5.4, 5.6);
        Assert.Equal(TelemetryState.Receiving, simulator.State);
    }
}
