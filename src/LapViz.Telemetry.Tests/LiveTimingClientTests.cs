using LapViz.LiveTiming;
using LapViz.LiveTiming.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.Tests;

public class LiveTimingClientTests
{
    [Fact]
    public async Task DisposeAsync_Stops_Worker_And_Is_Idempotent()
    {
        var client = new LiveTimingClient(NullLogger<LiveTimingClient>.Instance);
        client.AddEventData(new SessionDataDeviceDto());

        var dispose = client.DisposeAsync().AsTask();
        Assert.Same(dispose, await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5))));

        await client.DisposeAsync();
        client.Dispose();
    }

    [Fact]
    public void Dispose_Does_Not_Deadlock_On_SynchronizationContext()
    {
        var client = new LiveTimingClient(NullLogger<LiveTimingClient>.Instance);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DroppingContext());
            client.Dispose();
        });
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Dispose deadlocked");
    }

    /// <summary>Context whose posted callbacks never run, like a blocked UI thread.</summary>
    private sealed class DroppingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object state)
        {
        }
    }
}
