using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Tests.Domain;

public class RaceSessionStateTests
{
    [Fact]
    public void GetOrCreateEntry_Creates_New_Entry()
    {
        var state = new RaceSessionState();
        var entry = state.GetOrCreateEntry("42");

        Assert.NotNull(entry);
        Assert.True(state.Entries.ContainsKey("42"));
    }

    [Fact]
    public void GetOrCreateEntry_Returns_Same_Instance()
    {
        var state = new RaceSessionState();
        var first = state.GetOrCreateEntry("42");
        var second = state.GetOrCreateEntry("42");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreateEntry_Throws_On_Empty_Key()
    {
        var state = new RaceSessionState();
        Assert.Throws<ArgumentException>(() => state.GetOrCreateEntry(""));
        Assert.Throws<ArgumentException>(() => state.GetOrCreateEntry("  "));
    }

    [Fact]
    public void WithEntry_Executes_Action_ThreadSafe()
    {
        var state = new RaceSessionState();

        state.WithEntry("42", entry =>
        {
            entry.Position = 1;
            entry.Driver = new Driver { Name = "Test", Number = "42" };
        });

        Assert.Equal(1, state.Entries["42"].Position);
        Assert.Equal("Test", state.Entries["42"].Driver.Name);
    }

    [Fact]
    public void WithEntry_Ignores_Null_Action()
    {
        var state = new RaceSessionState();
        state.WithEntry("42", null!);
        // Should not throw; entry may or may not be created
    }

    [Fact]
    public void BestLap_Returns_Null_When_No_Entries()
    {
        var state = new RaceSessionState();
        Assert.Null(state.BestLap);
    }

    [Fact]
    public void BestLap_Returns_Best_Across_Entries()
    {
        var state = new RaceSessionState();

        var entry1 = state.GetOrCreateEntry("1");
        entry1.Timing.AddEvent(new SessionDataEvent
        {
            Type = SessionEventType.Lap,
            LapNumber = 1,
            Time = TimeSpan.FromSeconds(65),
            Timestamp = DateTimeOffset.UtcNow
        });

        var entry2 = state.GetOrCreateEntry("2");
        entry2.Timing.AddEvent(new SessionDataEvent
        {
            Type = SessionEventType.Lap,
            LapNumber = 1,
            Time = TimeSpan.FromSeconds(63),
            Timestamp = DateTimeOffset.UtcNow
        });

        Assert.Equal(TimeSpan.FromSeconds(63), state.BestLap);
    }

    [Fact]
    public void BestSectors_Returns_Best_Across_Entries()
    {
        var state = new RaceSessionState();

        var entry1 = state.GetOrCreateEntry("1");
        entry1.Timing.AddEvent(new SessionDataEvent
        {
            Type = SessionEventType.Sector,
            Sector = 1,
            LapNumber = 1,
            Time = TimeSpan.FromSeconds(20),
            Timestamp = DateTimeOffset.UtcNow
        });

        var entry2 = state.GetOrCreateEntry("2");
        entry2.Timing.AddEvent(new SessionDataEvent
        {
            Type = SessionEventType.Sector,
            Sector = 1,
            LapNumber = 1,
            Time = TimeSpan.FromSeconds(18),
            Timestamp = DateTimeOffset.UtcNow
        });

        var bestSectors = state.BestSectors;
        Assert.Equal(TimeSpan.FromSeconds(18), bestSectors[1]);
    }

    [Fact]
    public void Announcements_Schedule_Links_Are_Initialized()
    {
        var state = new RaceSessionState();

        Assert.NotNull(state.Announcements);
        Assert.Empty(state.Announcements);
        Assert.NotNull(state.Schedule);
        Assert.Empty(state.Schedule);
        Assert.NotNull(state.Links);
        Assert.Empty(state.Links);
    }

    [Fact]
    public void Session_Properties_Are_Settable()
    {
        var state = new RaceSessionState
        {
            TrackName = "Spa",
            TrackLength = 7.004,
            EventName = "Belgian GP",
            GroupName = "F1",
            RunName = "Race",
            RunType = RaceRunType.Race,
            Flag = RaceFlag.Green,
            SectorCount = 3,
            TotalLaps = 44,
            LeaderLap = 10,
            RaceTime = TimeSpan.FromMinutes(30),
            TimeToGo = TimeSpan.FromMinutes(60),
            TrackCondition = TrackCondition.Dry
        };

        Assert.Equal("Spa", state.TrackName);
        Assert.Equal(7.004, state.TrackLength);
        Assert.Equal(RaceFlag.Green, state.Flag);
        Assert.Equal(3, state.SectorCount);
        Assert.Equal(44, state.TotalLaps);
    }
}

public class RaceEntryTests
{
    [Fact]
    public void Default_Ctor_Initializes_Timing_And_Driver()
    {
        var entry = new RaceEntry();

        Assert.NotNull(entry.Timing);
        Assert.NotNull(entry.Driver);
        Assert.Equal(RaceEntryStatus.Unknown, entry.Status);
        Assert.False(entry.HasFinished);
        Assert.Equal(0, entry.PenaltyTime);
    }

    [Fact]
    public void Ctor_With_Driver_Sets_Driver()
    {
        var driver = new Driver { Name = "Max", Number = "1" };
        var entry = new RaceEntry(driver);

        Assert.Same(driver, entry.Driver);
    }

    [Fact]
    public void Ctor_With_Null_Driver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RaceEntry(null!));
    }

    [Fact]
    public void Timing_BestLap_Is_Computed_From_Events()
    {
        var entry = new RaceEntry();

        entry.Timing.AddEvent(new SessionDataEvent
        {
            Type = SessionEventType.Lap,
            LapNumber = 1,
            Time = TimeSpan.FromSeconds(65),
            Timestamp = DateTimeOffset.UtcNow
        });
        entry.Timing.AddEvent(new SessionDataEvent
        {
            Type = SessionEventType.Lap,
            LapNumber = 2,
            Time = TimeSpan.FromSeconds(63),
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(63)
        });

        Assert.Equal(TimeSpan.FromSeconds(63), entry.Timing.BestLap?.Time);
    }

    [Fact]
    public void Race_Properties_Are_Settable()
    {
        var entry = new RaceEntry
        {
            Position = 3,
            StartPosition = 5,
            Laps = 10,
            Gap = "+12.345",
            Interval = "+1.234",
            Status = RaceEntryStatus.Running,
            HasFinished = false,
            PenaltyTime = 5,
            Marker = "PIT",
            TotalTime = TimeSpan.FromMinutes(30)
        };

        Assert.Equal(3, entry.Position);
        Assert.Equal(5, entry.StartPosition);
        Assert.Equal("+12.345", entry.Gap);
        Assert.Equal(RaceEntryStatus.Running, entry.Status);
        Assert.Equal(5, entry.PenaltyTime);
    }
}
