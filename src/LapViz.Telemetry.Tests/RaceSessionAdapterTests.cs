using LapViz.LiveTiming;
using LapViz.LiveTiming.Models.Views;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Tests.Domain;

public class RaceSessionAdapterTests
{
    private static RaceSessionState CreateState(int sectorCount = 3)
    {
        return new RaceSessionState { SectorCount = sectorCount };
    }

    [Fact]
    public void Update_NewLap_CreatesLapEvent()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState();
        var entry = state.GetOrCreateEntry("42");
        entry.Driver = new Driver { Number = "42", Name = "Test Driver" };
        entry.Laps = 1;
        entry.LastTime = TimeSpan.FromSeconds(65.123);

        adapter.Update(state);

        var device = adapter.View.Devices.FirstOrDefault(d => d.Id == "42");
        Assert.NotNull(device);
        Assert.Contains(device.Events, e =>
            e.Type == LiveTimingDataDeviceEventType.Lap &&
            e.Lap == 1);
    }

    [Fact]
    public void Update_SameLap_DoesNotDuplicate()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState();
        var entry = state.GetOrCreateEntry("42");
        entry.Driver = new Driver { Number = "42", Name = "Test" };
        entry.Laps = 1;
        entry.LastTime = TimeSpan.FromSeconds(65);

        adapter.Update(state);
        adapter.Update(state); // same state again

        var device = adapter.View.Devices.First(d => d.Id == "42");
        var lapEvents = device.Events.Where(e =>
            e.Type == LiveTimingDataDeviceEventType.Lap).ToList();
        Assert.Single(lapEvents);
    }

    [Fact]
    public void Update_NewSector_CreatesSectorEvent()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState();
        var entry = state.GetOrCreateEntry("42");
        entry.Driver = new Driver { Number = "42", Name = "Test" };
        entry.Laps = 1;
        entry.SectionTimes[0] = TimeSpan.FromSeconds(20.5);

        adapter.Update(state);

        var device = adapter.View.Devices.First(d => d.Id == "42");
        Assert.Contains(device.Events, e =>
            e.Type == LiveTimingDataDeviceEventType.Sector &&
            e.Sector == 1);
    }

    [Fact]
    public void Update_MultipleSectors_CreatesAllEvents()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState(3);
        var entry = state.GetOrCreateEntry("42");
        entry.Driver = new Driver { Number = "42", Name = "Test" };
        entry.Laps = 1;
        entry.SectionTimes[0] = TimeSpan.FromSeconds(20);
        entry.SectionTimes[1] = TimeSpan.FromSeconds(22);
        entry.SectionTimes[2] = TimeSpan.FromSeconds(23);
        entry.LastTime = TimeSpan.FromSeconds(65);

        adapter.Update(state);

        var device = adapter.View.Devices.First(d => d.Id == "42");
        var sectorEvents = device.Events.Where(e =>
            e.Type == LiveTimingDataDeviceEventType.Sector).ToList();
        Assert.Equal(3, sectorEvents.Count);
    }

    [Fact]
    public void Update_MultipleDrivers_CreatesDevicesForEach()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState();

        var e1 = state.GetOrCreateEntry("1");
        e1.Driver = new Driver { Number = "1", Name = "Driver A" };
        e1.Laps = 1;
        e1.LastTime = TimeSpan.FromSeconds(60);

        var e2 = state.GetOrCreateEntry("2");
        e2.Driver = new Driver { Number = "2", Name = "Driver B" };
        e2.Laps = 1;
        e2.LastTime = TimeSpan.FromSeconds(61);

        adapter.Update(state);

        Assert.Equal(2, adapter.View.Devices.Count);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState();
        var entry = state.GetOrCreateEntry("42");
        entry.Driver = new Driver { Number = "42", Name = "Test" };
        entry.Laps = 1;
        entry.LastTime = TimeSpan.FromSeconds(60);

        adapter.Update(state);
        Assert.Single(adapter.View.Devices);

        adapter.Reset("new-session");
        Assert.Empty(adapter.View.Devices);
        Assert.Equal("new-session", adapter.View.SessionId);
    }

    [Fact]
    public void Update_NewLapAfterPrevious_DetectsChange()
    {
        var adapter = new RaceSessionAdapter("test");
        var state = CreateState();
        var entry = state.GetOrCreateEntry("42");
        entry.Driver = new Driver { Number = "42", Name = "Test" };
        entry.Laps = 1;
        entry.LastTime = TimeSpan.FromSeconds(65);

        adapter.Update(state);

        // Lap 2
        entry.Laps = 2;
        entry.LastTime = TimeSpan.FromSeconds(63);
        adapter.Update(state);

        var device = adapter.View.Devices.First(d => d.Id == "42");
        var lapEvents = device.Events.Where(e =>
            e.Type == LiveTimingDataDeviceEventType.Lap).ToList();
        Assert.Equal(2, lapEvents.Count);
    }
}
