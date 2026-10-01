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

        Assert.Equal(167, circuits.Count);
        Assert.All(circuits, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.Code));
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
