using LapViz.Telemetry.Domain;
using LapViz.Telemetry.Timing;

namespace LapViz.Telemetry.Tests.Timing;

public class PassingTimingSessionTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);

    private static int _ids;

    private static TimingPassing P(string key, double seconds, int line = 1, string? id = null) => new TimingPassing
    {
        Id = id ?? $"p{Interlocked.Increment(ref _ids)}",
        Key = key,
        Line = line,
        Timestamp = T0.AddSeconds(seconds),
    };

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static PassingTimingSession Practice(int lines = 1, TimeSpan? duration = null) =>
        new PassingTimingSession(new TimingLineLayout(lines), TimingRules.Practice(duration));

    private static PassingTimingSession Race(int laps = 0, TimeSpan? duration = null, TimingStartMode start = TimingStartMode.Signal, TimeSpan? minLapTime = null)
    {
        var rules = TimingRules.Race(laps, duration, start);
        rules.MinLapTime = minLapTime ?? TimeSpan.FromSeconds(20);
        return new PassingTimingSession(new TimingLineLayout(), rules);
    }

    private static void Feed(PassingTimingSession session, params TimingPassing[] passings)
    {
        foreach (var passing in passings)
            session.AddPassing(passing);
    }

    public sealed class Laps
    {
        [Fact]
        public void First_Passing_Starts_The_Timing_And_The_Next_Ones_Complete_Laps()
        {
            var session = Practice();

            var first = session.AddPassing(P("A", 0));
            var second = session.AddPassing(P("A", 50.5));
            session.AddPassing(P("A", 99.7));

            var lap0 = Assert.Single(first.Events);
            Assert.Equal(SessionEventType.Lap, lap0.Type);
            Assert.Equal(0, lap0.LapNumber);
            Assert.Equal(TimeSpan.Zero, lap0.Time);

            var lap1 = Assert.Single(second.Events);
            Assert.Equal(1, lap1.LapNumber);
            Assert.Equal(TimeSpan.FromSeconds(50.5), lap1.Time);

            var timing = session.GetTiming("A")!;
            Assert.Equal(2, timing.Laps);
            Assert.Equal(TimeSpan.FromSeconds(49.2), timing.LastLapTime);
            Assert.Equal(TimeSpan.FromSeconds(49.2), timing.BestLapTime);
            Assert.Equal(TimeSpan.FromSeconds(49.2), timing.Session.BestLap.Time);
            Assert.Equal(RaceEntryStatus.Running, timing.Status);
        }

        [Fact]
        public void Duplicate_Passings_And_Laps_Under_The_Minimum_Do_Not_Count()
        {
            var session = Practice();
            session.Rules.MinLapTime = TimeSpan.FromSeconds(30);

            session.AddPassing(P("A", 0));
            var duplicate = session.AddPassing(P("A", 0.8));
            var tooShort = session.AddPassing(P("A", 12));
            session.AddPassing(P("A", 50));

            Assert.Equal(PassingRejection.Duplicate, duplicate.Rejection);
            Assert.Equal(PassingRejection.MinLapTime, tooShort.Rejection);
            Assert.Empty(duplicate.Events);

            var timing = session.GetTiming("A")!;
            Assert.Equal(1, timing.Laps);
            Assert.Equal(TimeSpan.FromSeconds(50), timing.LastLapTime);
            Assert.Equal(2, timing.Rejected.Count);
        }

        [Fact]
        public void A_Passing_Received_Twice_Counts_Once()
        {
            var session = Practice();

            session.AddPassing(P("A", 0, id: "x1"));
            session.AddPassing(P("A", 50, id: "x2"));
            var again = session.AddPassing(P("A", 50, id: "x2"));

            Assert.Equal(PassingRejection.AlreadyReceived, again.Rejection);
            Assert.Equal(1, session.GetTiming("A")!.Laps);
        }

        [Fact]
        public void Unknown_Line_Is_Rejected()
        {
            var session = Practice();
            var update = session.AddPassing(P("A", 0, line: 7));
            Assert.Equal(PassingRejection.UnknownLine, update.Rejection);
        }
    }

    public sealed class Sectors
    {
        [Fact]
        public void Each_Line_Ends_A_Sector_And_The_Finish_Line_The_Last_One()
        {
            var session = Practice(lines: 3);

            Feed(session, P("A", 0), P("A", 15, line: 2), P("A", 32, line: 3));
            var finish = session.AddPassing(P("A", 50));

            Assert.Collection(finish.Events,
                e =>
                {
                    Assert.Equal(SessionEventType.Sector, e.Type);
                    Assert.Equal(3, e.Sector);
                    Assert.Equal(1, e.LapNumber);
                    Assert.Equal(TimeSpan.FromSeconds(18), e.Time);
                },
                e =>
                {
                    Assert.Equal(SessionEventType.Lap, e.Type);
                    Assert.Equal(TimeSpan.FromSeconds(50), e.Time);
                });

            var timing = session.GetTiming("A")!;
            Assert.Equal(new TimeSpan?[] { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(17), TimeSpan.FromSeconds(18) }, timing.PreviousLapSectorTimes);
            Assert.Equal(-1, timing.LastCompletedSector);
            Assert.Equal(TimeSpan.FromSeconds(17), timing.BestSectorTimes[1]);
        }

        [Fact]
        public void A_Missed_Loop_Gives_No_Sector_Time_But_The_Lap_Time_Stays_Right()
        {
            var session = Practice(lines: 3);

            // Line 2 missed on lap 1
            Feed(session, P("A", 0), P("A", 32, line: 3), P("A", 50));

            var timing = session.GetTiming("A")!;
            Assert.Equal(TimeSpan.FromSeconds(50), timing.LastLapTime);
            Assert.DoesNotContain(timing.Session.Events, e => e.Type == SessionEventType.Sector && e.Sector == 2);
            Assert.Equal(TimeSpan.FromSeconds(18), Assert.Single(timing.Session.Events, e => e.Type == SessionEventType.Sector).Time);
        }

        [Fact]
        public void A_Missed_Finish_Line_Counts_A_Lap_Without_Time()
        {
            var session = Practice(lines: 3);

            // Lap 1 normal, finish line of lap 2 missed (line 2 seen again), lap 3 has no known start, lap 4 timed
            Feed(session,
                P("A", 0), P("A", 15, line: 2), P("A", 32, line: 3), P("A", 50),
                P("A", 65, line: 2), P("A", 82, line: 3),
                P("A", 115, line: 2), P("A", 132, line: 3), P("A", 150),
                P("A", 165, line: 2), P("A", 182, line: 3), P("A", 200));

            var timing = session.GetTiming("A")!;
            Assert.Equal(4, timing.Laps);
            Assert.Equal(1, timing.EstimatedLaps);
            Assert.Equal(At(100), timing.LapCompletions[1]); // lap start + last lap time
            Assert.Equal(TimeSpan.FromSeconds(50), timing.LastLapTime);

            var laps = timing.Session.Events.Where(e => e.Type == SessionEventType.Lap && e.LapNumber > 0).ToList();
            Assert.Equal(new[] { 1, 4 }, laps.Select(e => e.LapNumber));
        }
    }

    public sealed class Starts
    {
        [Fact]
        public void With_A_Start_Signal_Passings_Before_It_Do_Not_Count_And_Lap_1_Is_Timed_From_It()
        {
            var session = Race(laps: 10);

            var formation = session.AddPassing(P("A", -60));
            Assert.Equal(PassingRejection.BeforeStart, formation.Rejection);

            session.Start(At(0));
            session.AddPassing(P("A", 52));

            var timing = session.GetTiming("A")!;
            Assert.Equal(1, timing.Laps);
            Assert.Equal(TimeSpan.FromSeconds(52), timing.LastLapTime);
            Assert.Equal(At(0), timing.StartedAt);
        }

        [Fact]
        public void A_Grid_Behind_The_Line_Only_Starts_Lap_1()
        {
            var session = Race(laps: 10);
            session.Start(At(0));

            var crossing = session.AddPassing(P("A", 3));
            session.AddPassing(P("A", 53));

            Assert.Null(crossing.Rejection);
            var timing = session.GetTiming("A")!;
            Assert.Equal(1, timing.Laps);
            Assert.Equal(TimeSpan.FromSeconds(50), timing.LastLapTime);
        }

        [Fact]
        public void Rolling_Start_Counts_From_The_First_Crossing()
        {
            var session = Race(laps: 10, start: TimingStartMode.FirstCrossing);
            session.Start(At(0));

            Feed(session, P("A", 5), P("B", 5.4), P("A", 55), P("B", 56));

            Assert.Equal(At(5), session.StartedAt);
            var rows = session.Classify(At(60));
            Assert.Equal("A", rows[0].Timing.Key);
            Assert.Equal(TimeSpan.FromSeconds(1), rows[1].GapTime);
        }
    }

    public sealed class Finish
    {
        [Fact]
        public void The_Leader_Completing_The_Laps_Shows_The_Checkered_Flag_And_The_Others_Finish_On_Their_Next_Passing()
        {
            var session = Race(laps: 2);
            session.Start(At(0));

            Feed(session, P("A", 50), P("B", 52), P("C", 60));
            var leaderFinish = session.AddPassing(P("A", 100));
            Feed(session, P("B", 103), P("C", 121));
            var coolDown = session.AddPassing(P("A", 150));

            Assert.True(leaderFinish.FlagChanged);
            Assert.Equal(At(100), session.CheckeredAt);
            Assert.Equal(RaceFlag.Checkered, session.Flag);
            Assert.Equal(PassingRejection.AfterFinish, coolDown.Rejection);

            var rows = session.Classify(At(200));
            Assert.Equal(new[] { "A", "B", "C" }, rows.Select(r => r.Timing.Key));
            Assert.All(rows, r => Assert.True(r.Timing.Finished));
            Assert.Equal(2, rows[2].Timing.Laps); // C finishes on its next passing, its lap counts
            Assert.Equal(TimeSpan.FromSeconds(3), rows[1].GapTime);
            Assert.Equal(TimeSpan.FromSeconds(21), rows[2].GapTime);
            Assert.Equal(TimeSpan.FromSeconds(18), rows[2].IntervalTime);
        }

        [Fact]
        public void A_Lapped_Competitor_Is_Laps_Down()
        {
            var session = Race(laps: 3);
            session.Start(At(0));

            Feed(session, P("A", 50), P("B", 70), P("A", 100), P("A", 150), P("B", 160));

            var rows = session.Classify(At(170));
            Assert.Equal("B", rows[1].Timing.Key);
            Assert.True(rows[1].Timing.Finished);
            Assert.Equal(1, rows[1].GapLaps);
            Assert.Null(rows[1].GapTime);
        }

        [Fact]
        public void A_Timed_Race_Ends_On_The_Leaders_First_Passing_After_The_Time()
        {
            var session = Race(duration: TimeSpan.FromSeconds(120));
            session.Start(At(0));

            Feed(session, P("A", 50), P("B", 55), P("A", 100), P("B", 108));
            // B's passing at 160 arrives before A's at 150: B looks like the leader until A's passing recomputes the race
            var early = session.AddPassing(P("B", 160));
            Assert.Equal(At(160), session.CheckeredAt);
            var leader = session.AddPassing(P("A", 150));

            Assert.Null(early.Rejection);
            Assert.True(leader.Rebuilt);
            Assert.Equal(At(150), session.CheckeredAt);
            Assert.True(session.GetTiming("A")!.Finished);
            Assert.True(session.GetTiming("B")!.Finished);
            Assert.Equal(3, session.GetTiming("B")!.Laps);
        }

        [Fact]
        public void A_Timed_Practice_Ends_At_Once_And_The_Lap_In_Progress_Counts()
        {
            var session = Practice(duration: TimeSpan.FromSeconds(100));

            Feed(session, P("A", 0), P("A", 50), P("A", 98));
            var last = session.AddPassing(P("A", 147));
            var after = session.AddPassing(P("A", 195));

            Assert.True(last.FlagChanged);
            Assert.Equal(At(100), session.CheckeredAt);
            Assert.Equal(3, session.GetTiming("A")!.Laps);
            Assert.True(session.GetTiming("A")!.Finished);
            Assert.Equal(PassingRejection.AfterFinish, after.Rejection);
        }

        [Fact]
        public void The_Checkered_Flag_Can_Be_Shown_By_Hand()
        {
            var session = Practice();
            Feed(session, P("A", 0), P("A", 50));

            var update = session.Checkered(At(60));
            session.AddPassing(P("A", 100));

            Assert.True(update.FlagChanged);
            Assert.True(session.GetTiming("A")!.Finished);
        }
    }

    public sealed class Classification
    {
        [Fact]
        public void Practice_Ranks_By_Best_Lap_With_Gaps_And_Registered_Competitors_Before_Their_First_Passing()
        {
            var session = Practice();
            session.SetCompetitor(new TimingCompetitor { Key = "1001", Number = "7", Name = "Alice", StartPosition = 1 });
            session.SetCompetitor(new TimingCompetitor { Key = "1002", Number = "12", Name = "Bob" });
            session.SetCompetitor(new TimingCompetitor { Key = "1003", Number = "3", Name = "Carol" });

            Feed(session, P("1001", 0), P("1002", 1), P("1001", 51), P("1002", 50.5), P("1002", 99.4));

            var rows = session.Classify(At(100));
            Assert.Equal(new[] { "12", "7", "3" }, rows.Select(r => r.Competitor.Number));
            Assert.Equal(TimeSpan.FromSeconds(48.9), rows[0].Timing.BestLapTime);
            Assert.Equal(TimeSpan.FromSeconds(2.1), rows[1].GapTime);
            Assert.Null(rows[2].GapTime);
            Assert.Equal(RaceEntryStatus.Unknown, rows[2].Timing.Status);
        }

        [Fact]
        public void An_Unknown_Transponder_Keeps_Its_Laps_Once_Assigned()
        {
            var session = Practice();
            Feed(session, P("9999", 0), P("9999", 50));

            var unknown = Assert.Single(session.Classify(At(60)));
            Assert.True(unknown.Competitor.IsUnknown);
            Assert.Equal("9999", unknown.Competitor.DisplayNumber);

            session.SetCompetitor(new TimingCompetitor { Key = "9999", Number = "44", Name = "Dave" });

            var row = Assert.Single(session.Classify(At(60)));
            Assert.False(row.Competitor.IsUnknown);
            Assert.Equal("44", row.Competitor.Number);
            Assert.Equal(1, row.Timing.Laps);
        }

        [Fact]
        public void Penalties_And_Statuses_Change_The_Race_Order()
        {
            var session = Race(laps: 5);
            session.Start(At(0));
            Feed(session, P("A", 50), P("B", 51), P("C", 52), P("A", 100), P("B", 101), P("C", 102));

            session.SetPenalty("A", TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "B", "C", "A" }, session.Classify(At(110)).Select(r => r.Timing.Key));

            session.SetPenalty("A", TimeSpan.Zero, laps: 1);
            Assert.Equal(new[] { "B", "C", "A" }, session.Classify(At(110)).Select(r => r.Timing.Key));
            Assert.Equal(1, session.Classify(At(110))[2].Timing.ClassifiedLaps);

            session.SetPenalty("A", TimeSpan.Zero);
            session.SetStatus("B", RaceEntryStatus.DSQ);
            var rows = session.Classify(At(110));
            Assert.Equal(new[] { "A", "C", "B" }, rows.Select(r => r.Timing.Key));
            Assert.Equal(RaceEntryStatus.DSQ, rows[2].Timing.Status);
        }

        [Fact]
        public void Before_The_First_Lap_A_Race_Is_In_Grid_Order()
        {
            var session = Race(laps: 5);
            session.SetCompetitor(new TimingCompetitor { Key = "a", Number = "5", StartPosition = 2 });
            session.SetCompetitor(new TimingCompetitor { Key = "b", Number = "9", StartPosition = 1 });
            session.SetCompetitor(new TimingCompetitor { Key = "c", Number = "1" });

            Assert.Equal(new[] { "9", "5", "1" }, session.Classify(At(0)).Select(r => r.Competitor.Number));
        }
    }

    public sealed class Determinism
    {
        private static TimingPassing[] Race1() => new[]
        {
            P("A", 50, id: "1"), P("B", 51, id: "2"), P("C", 70, id: "3"),
            P("A", 100, id: "4"), P("B", 99, id: "5"), P("C", 125, id: "6"),
            P("A", 150, id: "7"), P("B", 152, id: "8"), P("C", 180, id: "9"),
            P("A", 151, id: "dup"),
        };

        private static string Snapshot(PassingTimingSession session) =>
            string.Join(";", session.Classify(At(300)).Select(r =>
                $"{r.Position}:{r.Timing.Key}:{r.Timing.Laps}:{r.Timing.BestLapTime}:{r.GapTime}:{r.GapLaps}:{r.Timing.Finished}:" +
                string.Join(",", r.Timing.Session.Events.Select(e => e.EventId.Value))));

        [Fact]
        public void The_Same_Journal_Gives_The_Same_Result_Whatever_The_Order_Of_Arrival()
        {
            var inOrder = Race(laps: 3);
            inOrder.Start(At(0));
            Feed(inOrder, Race1().OrderBy(p => p.Timestamp).ToArray());

            var shuffled = Race(laps: 3);
            var passings = Race1();
            Feed(shuffled, passings.Reverse().Take(5).ToArray());
            shuffled.Start(At(0));
            Feed(shuffled, passings.Take(5).ToArray());

            Assert.Equal(Snapshot(inOrder), Snapshot(shuffled));
        }

        [Fact]
        public void An_Older_Passing_Recomputes_The_Session()
        {
            var session = Practice();
            Feed(session, P("A", 0), P("A", 100));

            var late = session.AddPassing(P("A", 50));

            Assert.True(late.Rebuilt);
            Assert.Empty(late.Events);
            Assert.Equal(2, session.GetTiming("A")!.Laps);
            Assert.Equal(TimeSpan.FromSeconds(50), session.GetTiming("A")!.BestLapTime);
        }

        [Fact]
        public void An_Invalidated_Passing_No_Longer_Counts_Until_Restored()
        {
            var session = Practice();
            Feed(session, P("A", 0, id: "s"), P("A", 30, id: "ghost"), P("A", 60, id: "real"));
            Assert.Equal(TimeSpan.FromSeconds(30), session.GetTiming("A")!.BestLapTime);

            Assert.True(session.InvalidatePassing("ghost"));
            var timing = session.GetTiming("A")!;
            Assert.Equal(1, timing.Laps);
            Assert.Equal(TimeSpan.FromSeconds(60), timing.BestLapTime);
            Assert.Equal(PassingRejection.Invalidated, Assert.Single(timing.Rejected).Reason);

            Assert.True(session.RestorePassing("ghost"));
            Assert.Equal(2, session.GetTiming("A")!.Laps);
        }
    }

    public sealed class Pit
    {
        [Fact]
        public void Pit_Lines_Set_In_Pit_Then_Out_Lap_Until_The_Finish_Line()
        {
            var session = new PassingTimingSession(new TimingLineLayout(1, pitInLine: 10, pitOutLine: 11), TimingRules.Practice());
            Feed(session, P("A", 0), P("A", 50), P("A", 90, line: 10));
            Assert.Equal(RaceEntryStatus.InPit, session.GetTiming("A")!.Status);

            session.AddPassing(P("A", 180, line: 11));
            Assert.Equal(RaceEntryStatus.OutLap, session.GetTiming("A")!.Status);

            session.AddPassing(P("A", 200));
            Assert.Equal(RaceEntryStatus.Running, session.GetTiming("A")!.Status);
            Assert.Equal(2, session.GetTiming("A")!.Laps);
        }
    }

    public sealed class RaceStateOutput
    {
        [Fact]
        public void Fills_The_Race_Model()
        {
            var session = new PassingTimingSession(new TimingLineLayout(2), TimingRules.Race(laps: 10));
            session.Rules.MinLapTime = TimeSpan.FromSeconds(20);
            session.SetCompetitor(new TimingCompetitor { Key = "1001", Number = "7", Name = "Alice", ClassName = "Senior", StartPosition = 2 });
            session.SetCompetitor(new TimingCompetitor { Key = "1002", Number = "12", Name = "Bob", ClassName = "Senior", StartPosition = 1 });
            session.Start(At(0));
            Feed(session,
                P("1002", 20, line: 2), P("1001", 21, line: 2),
                P("1002", 50), P("1001", 51.234),
                P("1002", 70, line: 2), P("1001", 70.5, line: 2));

            var state = new RaceSessionState();
            session.ToRaceState(state, At(80));

            Assert.Equal(RaceRunType.Race, state.RunType);
            Assert.Equal(RaceFlag.Green, state.Flag);
            Assert.Equal(2, state.SectorCount);
            Assert.Equal(10, state.TotalLaps);
            Assert.Equal(1, state.LeaderLap);
            Assert.Equal(TimeSpan.FromSeconds(80), state.RaceTime);
            Assert.Null(state.TimeToGo);
            Assert.Equal(TimeSpan.FromSeconds(50), state.BestLapTime);
            Assert.Equal("12", state.BestLapByNumber);
            Assert.Equal("Bob", state.BestLapBy);

            var bob = state.Entries["12"];
            var alice = state.Entries["7"];
            Assert.Equal(1, bob.Position);
            Assert.Equal(2, alice.Position);
            Assert.Equal("", bob.Gap);
            Assert.Equal("+1.234", alice.Gap);
            Assert.Equal("+1.234", alice.Interval);
            Assert.Equal("Senior", alice.ClassName);
            Assert.Equal(2, alice.StartPosition);
            Assert.Equal(TimeSpan.FromSeconds(51.234), alice.TotalTime);
            Assert.Equal(RaceEntryStatus.Running, alice.Status);

            // Sector 1 of lap 2 done: only it is shown
            Assert.Equal(0, bob.LastCompletedSector);
            Assert.Equal(TimeSpan.FromSeconds(20), bob.SectionTimes[0]);
            Assert.Null(bob.SectionTimes[1]);
            Assert.True(bob.IsSectorPersonalBest[0]);
            Assert.True(bob.IsLastLapOverallBest);
            Assert.Same(session.GetTiming("1002")!.Session, bob.Timing);
        }

        [Fact]
        public void Gap_Formats_Are_Those_Of_OpenLiveTiming()
        {
            Assert.Equal("+0.500", Utils.TimingFormatters.FormatGap(TimeSpan.FromMilliseconds(500)));
            Assert.Equal("+1:23.456", Utils.TimingFormatters.FormatGap(TimeSpan.FromMilliseconds(83_456)));
            Assert.Equal("+2:00.000", Utils.TimingFormatters.FormatGap(TimeSpan.FromSeconds(119.9996)));
            Assert.Equal("-0.250", Utils.TimingFormatters.FormatGap(TimeSpan.FromMilliseconds(-250)));
            Assert.Equal("+1 Lap", Utils.TimingFormatters.FormatLapsGap(1));
            Assert.Equal("+3 Laps", Utils.TimingFormatters.FormatLapsGap(3));
        }

        [Fact]
        public void A_Timed_Session_Counts_Down()
        {
            var session = Practice(duration: TimeSpan.FromMinutes(10));
            session.Start(At(0));

            var state = new RaceSessionState();
            session.ToRaceState(state, At(90));

            Assert.Equal(TimeSpan.FromSeconds(90), state.RaceTime);
            Assert.Equal(TimeSpan.FromSeconds(510), state.TimeToGo);

            session.ToRaceState(state, At(700));
            Assert.Equal(RaceFlag.Checkered, state.Flag);
            Assert.Equal(TimeSpan.Zero, state.TimeToGo);
        }
    }

    public sealed class RaceControl
    {
        [Fact]
        public void A_Red_Flag_Stops_The_Clock_And_Extends_A_Timed_Race()
        {
            var session = Race(duration: TimeSpan.FromSeconds(300));
            session.Start(At(0));
            session.SetFlag(RaceFlag.Red, At(100));

            var state = new RaceSessionState();
            session.ToRaceState(state, At(130));
            Assert.Equal(RaceFlag.Red, state.Flag);
            Assert.Equal(TimeSpan.FromSeconds(100), state.RaceTime); // stopped at the red flag
            Assert.Equal(TimeSpan.FromSeconds(200), state.TimeToGo);

            session.SetFlag(RaceFlag.Green, At(160));
            session.ToRaceState(state, At(200));
            Assert.Equal(RaceFlag.Green, state.Flag);
            Assert.Equal(TimeSpan.FromSeconds(140), state.RaceTime);

            // 300 s of racing end at 360 s: the leader passing at 330 s goes on, at 370 s takes the flag
            session.AddPassing(P("A", 330));
            Assert.Null(session.CheckeredAt);
            session.AddPassing(P("A", 370));
            Assert.Equal(At(370), session.CheckeredAt);
        }

        [Fact]
        public void Without_The_Clock_Rule_The_Red_Flag_Does_Not_Extend_The_Race()
        {
            var session = Race(duration: TimeSpan.FromSeconds(300));
            session.Rules.RedFlagStopsClock = false;
            session.Start(At(0));
            session.SetFlag(RaceFlag.Red, At(100));
            session.SetFlag(RaceFlag.Green, At(160));
            session.AddPassing(P("A", 330));
            Assert.Equal(At(330), session.CheckeredAt);
        }

        [Fact]
        public void A_Timed_Practice_Ends_Later_After_A_Suspension_And_Never_While_Suspended()
        {
            var session = Practice(duration: TimeSpan.FromSeconds(600));
            session.Start(At(0));
            session.SetFlag(RaceFlag.Red, At(500));

            var state = new RaceSessionState();
            session.ToRaceState(state, At(800));
            Assert.Equal(RaceFlag.Red, state.Flag); // the time would be up, but the clock is stopped

            session.SetFlag(RaceFlag.Green, At(900));
            session.ToRaceState(state, At(950));
            Assert.Equal(TimeSpan.FromSeconds(50), state.TimeToGo);
            session.ToRaceState(state, At(1001));
            Assert.Equal(RaceFlag.Checkered, state.Flag);
            Assert.Equal(At(1000), session.CheckeredAt);
        }

        [Fact]
        public void A_Lap_Overlapping_A_Red_Flag_Counts_Without_Time()
        {
            var session = Race(laps: 10);
            session.Start(At(0));
            session.AddPassing(P("A", 50));
            session.AddPassing(P("A", 100));
            session.SetFlag(RaceFlag.Red, At(120));
            session.SetFlag(RaceFlag.Green, At(200));
            session.AddPassing(P("A", 230));
            session.AddPassing(P("A", 278));

            var a = session.GetTiming("A")!;
            Assert.Equal(4, a.Laps);
            Assert.Equal(1, a.ExcludedLaps);
            Assert.Equal(TimeSpan.FromSeconds(48), a.BestLapTime);
            Assert.DoesNotContain(a.Session.Events, e => e.Type == SessionEventType.Lap && e.Time > TimeSpan.FromSeconds(100));
            Assert.Equal(new[] { 1, 2, 4 }, a.Session.Events.Where(e => e.Type == SessionEventType.Lap && e.LapNumber > 0).Select(e => e.LapNumber));
        }

        [Fact]
        public void A_Red_Flag_Received_Late_Gives_The_Same_Result()
        {
            PassingTimingSession Build(bool late)
            {
                var session = Race(laps: 10);
                session.Start(At(0));
                if (!late)
                    session.SetFlag(RaceFlag.Red, At(120));
                foreach (var t in new[] { 50.0, 100, 230, 278 })
                {
                    if (!late && t > 200)
                        session.SetFlag(RaceFlag.Green, At(200));
                    session.AddPassing(P("A", t));
                }
                if (late)
                {
                    session.SetFlag(RaceFlag.Red, At(120));
                    session.SetFlag(RaceFlag.Green, At(200));
                }
                return session;
            }

            var inOrder = Build(late: false).GetTiming("A")!;
            var late = Build(late: true).GetTiming("A")!;
            Assert.Equal(inOrder.ExcludedLaps, late.ExcludedLaps);
            Assert.Equal(inOrder.BestLapTime, late.BestLapTime);
            Assert.Equal(inOrder.LastLapTime, late.LastLapTime);
        }

        [Fact]
        public void A_Sector_Overlapping_A_Red_Flag_Has_No_Time()
        {
            var session = new PassingTimingSession(new TimingLineLayout(2), TimingRules.Practice());
            session.Start(At(0));
            session.AddPassing(P("A", 10));
            session.AddPassing(P("A", 30, line: 2));
            session.SetFlag(RaceFlag.Red, At(40));
            session.SetFlag(RaceFlag.Green, At(90));
            session.AddPassing(P("A", 100));
            session.AddPassing(P("A", 120, line: 2));

            var a = session.GetTiming("A")!;
            Assert.Equal(new TimeSpan?[] { TimeSpan.FromSeconds(20), null }, a.PreviousLapSectorTimes);
            Assert.Equal(TimeSpan.FromSeconds(20), a.SectorTimes[0]);
            Assert.Null(a.BestSectorTimes[1]);
        }

        [Fact]
        public void A_Lap_Longer_Than_The_Maximum_Counts_Without_Time()
        {
            var session = Practice();
            session.Rules.MaxLapTime = TimeSpan.FromSeconds(120);
            session.AddPassing(P("A", 0));
            session.AddPassing(P("A", 60));
            session.AddPassing(P("A", 300)); // stopped on track
            Assert.Null(session.GetTiming("A")!.LastLapTime);
            session.AddPassing(P("A", 355));

            var a = session.GetTiming("A")!;
            Assert.Equal(3, a.Laps);
            Assert.Equal(1, a.ExcludedLaps);
            Assert.Equal(TimeSpan.FromSeconds(55), a.BestLapTime);
            Assert.Equal(TimeSpan.FromSeconds(55), a.LastLapTime);
        }

        [Fact]
        public void A_Short_Pit_Stop_Gets_The_Missing_Time_As_A_Penalty()
        {
            var rules = TimingRules.Race(laps: 3);
            rules.MinLapTime = TimeSpan.FromSeconds(20);
            rules.MinPitStopTime = TimeSpan.FromSeconds(30);
            var session = new PassingTimingSession(new TimingLineLayout(1, pitInLine: 10, pitOutLine: 11), rules);
            session.Start(At(0));

            // A pits for 20 s (10 s short), B for 35 s; A crosses the line first, 5 s ahead
            session.AddPassing(P("A", 50));
            session.AddPassing(P("B", 51));
            session.AddPassing(P("A", 80, line: 10));
            session.AddPassing(P("A", 100, line: 11));
            session.AddPassing(P("B", 75, line: 10));
            session.AddPassing(P("B", 110, line: 11));
            session.AddPassing(P("A", 150));
            session.AddPassing(P("B", 155));
            session.AddPassing(P("A", 200));
            session.AddPassing(P("B", 205));

            var a = session.GetTiming("A")!;
            var stop = Assert.Single(a.PitStops);
            Assert.Equal(TimeSpan.FromSeconds(20), stop.Duration);
            Assert.Equal(TimeSpan.FromSeconds(10), stop.Shortfall);
            Assert.Equal(TimeSpan.FromSeconds(10), a.TotalPenaltyTime);
            Assert.Equal(TimeSpan.Zero, session.GetTiming("B")!.TotalPenaltyTime);

            var rows = session.Classify(At(300));
            Assert.Equal(new[] { "B", "A" }, rows.Select(r => r.Timing.Key));

            var state = new RaceSessionState();
            session.ToRaceState(state, At(300));
            Assert.Equal(10, state.Entries.Values.Single(e => e.Driver.Id == "A").PenaltyTime);
        }

        [Fact]
        public void The_New_Rules_Go_Through_The_Journal()
        {
            var rules = TimingRules.Race(laps: 20);
            rules.MaxLapTime = TimeSpan.FromSeconds(180);
            rules.MinPitStopTime = TimeSpan.FromSeconds(45);
            rules.RedFlagStopsClock = false;
            var line = TimingJournal.Serialize(TimingJournal.Session(new TimingLineLayout(1, 10, 11), rules));
            var copy = TimingJournal.CreateSession(TimingJournal.Parse(line)!).Rules;
            Assert.Equal(TimeSpan.FromSeconds(180), copy.MaxLapTime);
            Assert.Equal(TimeSpan.FromSeconds(45), copy.MinPitStopTime);
            Assert.False(copy.RedFlagStopsClock);

            // A journal written before these rules: none of them
            var old = TimingJournal.CreateSession(TimingJournal.Parse(TimingJournal.Serialize(TimingJournal.Session(new TimingLineLayout(), TimingRules.Race(laps: 5))))!).Rules;
            Assert.Equal(TimeSpan.Zero, old.MaxLapTime);
            Assert.Equal(TimeSpan.Zero, old.MinPitStopTime);
            Assert.True(old.RedFlagStopsClock);
            Assert.DoesNotContain("maxLap", TimingJournal.Serialize(TimingJournal.Session(new TimingLineLayout(), TimingRules.Race(laps: 5))));
        }
    }
}
