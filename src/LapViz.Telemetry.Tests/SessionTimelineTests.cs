using System.Text.Json;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.Timing;

namespace LapViz.Telemetry.Tests.Timing;

public class SessionTimelineTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero);

    /// <summary>A session whose laps start at <paramref name="start"/> (seconds after T0) with the given lap times.</summary>
    private static TimelineSession Session(string key, double start, params double[] laps)
    {
        var events = new List<SessionDataEvent>();
        var t = T0.AddSeconds(start);
        for (var i = 0; i < laps.Length; i++)
        {
            t = t.AddSeconds(laps[i]);
            events.Add(new SessionDataEvent { Type = SessionEventType.Lap, LapNumber = i + 1, Time = TimeSpan.FromSeconds(laps[i]), Timestamp = t });
        }

        return new TimelineSession { Key = key, Number = key, Name = "Driver " + key, CircuitCode = "genk", Events = events };
    }

    [Fact]
    public void Practice_RanksByBestLap_AtEachCrossing()
    {
        var a = Session("A", 0, 60, 58, 59);
        var b = Session("B", 5, 59, 57.5, 61);

        var timeline = SessionTimeline.Build(new[] { a, b });

        Assert.Equal(T0, timeline.Start);
        Assert.Equal(8, timeline.Crossings.Count); // a start and three laps each
        // After A's first lap (60 s) A leads, B's 59 s then puts it first
        var afterA1 = timeline.Crossings.First(c => c.Key == "A" && c.Lap == 1);
        Assert.Equal(1, afterA1.Position);
        var afterB1 = timeline.Crossings.First(c => c.Key == "B" && c.Lap == 1);
        Assert.Equal(1, afterB1.Position);
        Assert.Equal(TimeSpan.FromSeconds(59), afterB1.LapTime);
        var last = timeline.Crossings[timeline.Crossings.Count - 1].Standings;
        Assert.Equal(new[] { "B", "A" }, last.Select(s => s.Key));
        Assert.Equal(TimeSpan.FromSeconds(57.5), last[0].BestLap);
        Assert.Equal(TimeSpan.FromSeconds(0.5), last[1].Gap);
    }

    [Fact]
    public void Race_RanksByLaps()
    {
        var rules = TimingRules.Race(startMode: TimingStartMode.FirstCrossing);
        var fast = Session("F", 0, 55, 55, 55);
        var slow = Session("S", 0.5, 60, 60);

        var timeline = SessionTimeline.Build(new[] { slow, fast }, new TimelineOptions { Rules = rules });

        var last = timeline.Crossings[timeline.Crossings.Count - 1].Standings;
        Assert.Equal("F", last[0].Key);
        Assert.Equal(3, last[0].Laps);
    }

    [Fact]
    public void DeviceClockInLocalTime_IsAligned_ByWholeHours()
    {
        var a = Session("A", 0, 60, 60, 60, 60);
        var b = Session("B", 10, 61, 61, 61, 61);
        var local = Session("L", 20, 62, 62, 62, 62);
        // The device wrote Brussels time (UTC+1) as UTC: one hour late
        local.Events = local.Events.Select(e => new SessionDataEvent { Type = e.Type, LapNumber = e.LapNumber, Time = e.Time, Timestamp = e.Timestamp.AddHours(1) }).ToList();

        var timeline = SessionTimeline.Build(new[] { a, b, local });

        Assert.Equal(TimeSpan.FromHours(-1), timeline.Offsets["L"]);
        Assert.Equal(TimeSpan.Zero, timeline.Offsets["A"]);
        Assert.Equal(T0, timeline.Start);
        Assert.True(timeline.End < T0.AddMinutes(5));
    }

    [Fact]
    public void DeviceWithoutTheRightDate_IsAlignedByADay_AndAManualOffsetIsKept()
    {
        var a = Session("A", 0, 60, 60, 60);
        var late = Session("D", 0, 60, 60, 60);
        late.Events = late.Events.Select(e => new SessionDataEvent { Type = e.Type, LapNumber = e.LapNumber, Time = e.Time, Timestamp = e.Timestamp.AddDays(1) }).ToList();
        var manual = Session("M", 0, 60, 60, 60);
        manual.Offset = TimeSpan.FromSeconds(-1.5);

        var timeline = SessionTimeline.Build(new[] { a, late, manual });

        Assert.Equal(TimeSpan.FromDays(-1), timeline.Offsets["D"]);
        Assert.Equal(TimeSpan.FromSeconds(-1.5), timeline.Offsets["M"]);
    }

    [Fact]
    public void DetectOffset_LeavesOtherRunsAlone()
    {
        var span = (T0, T0.AddMinutes(10));
        Assert.Equal(TimeSpan.Zero, SessionTimeline.DetectOffset((T0.AddMinutes(2), T0.AddMinutes(11)), span));
        Assert.Equal(TimeSpan.FromMinutes(-30), SessionTimeline.DetectOffset((T0.AddMinutes(30), T0.AddMinutes(40)), span));
        Assert.Null(SessionTimeline.DetectOffset((T0.AddMinutes(20), T0.AddMinutes(24)), (T0, T0.AddMinutes(4)))); // 20 min: another run
    }

    [Fact]
    public void GroupRuns_SplitsByCircuitAndTime()
    {
        var a = Session("A", 0, 60, 60);
        var b = Session("B", 30, 60, 60);
        var later = Session("C", 3600, 60, 60);
        var elsewhere = Session("E", 0, 60, 60);
        elsewhere.CircuitCode = "zolder";
        var empty = new TimelineSession { Key = "X", CircuitCode = "genk" };

        var runs = SessionTimeline.GroupRuns(new[] { later, a, empty, elsewhere, b }, TimeSpan.FromMinutes(10));

        Assert.Equal(3, runs.Count);
        Assert.Contains(runs, r => r.Select(s => s.Key).OrderBy(k => k).SequenceEqual(new[] { "A", "B" }));
        Assert.Contains(runs, r => r.Count == 1 && r[0].Key == "C");
        Assert.Contains(runs, r => r.Count == 1 && r[0].Key == "E");
    }

    [Fact]
    public void RaceTimelineJson_HasTheDriversTheirTracesAndTheLaps()
    {
        var a = Session("A", 0, 60, 58);
        var b = Session("B", 5, 59, 57.5);
        b.Offset = TimeSpan.FromSeconds(-2);
        var timeline = SessionTimeline.Build(new[] { a, b }, new TimelineOptions { AlignClocks = false });

        var json = timeline.ToRaceTimelineJson("Club day", "Senior", "Practice 1", "Genk", "genk",
            new Dictionary<string, string> { ["B"] = "sessions/b.lvz" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("lapviz-race", root.GetProperty("format").GetString());
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal("genk", root.GetProperty("circuit").GetString());
        Assert.Equal("P", root.GetProperty("type").GetString());
        Assert.Equal("2026-11-15T14:00:00.000Z", root.GetProperty("start").GetString());
        var drivers = root.GetProperty("drivers").EnumerateArray().ToList();
        Assert.Equal("B", drivers[0].GetProperty("n").GetString()); // best lap first
        Assert.Equal("sessions/b.lvz", drivers[0].GetProperty("trace").GetString());
        Assert.Equal(-2, drivers[0].GetProperty("traceOffset").GetDouble());
        Assert.False(drivers[1].TryGetProperty("trace", out _));
        var events = root.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(4, events.Count); // laps only, not the starts
        Assert.Equal(60, events[0].GetProperty("t").GetDouble());
        Assert.Equal("l", events[0].GetProperty("k").GetString());
    }
}
