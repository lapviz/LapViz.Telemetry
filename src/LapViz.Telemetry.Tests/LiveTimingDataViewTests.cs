using LapViz.LiveTiming.Models;
using LapViz.LiveTiming.Models.Views;

namespace LapViz.Telemetry.Tests.LiveTiming;

public class LiveTimingDataViewTests
{
    private static string MultipleDriverSessionDataCsvPath =>
        Path.Combine(AppContext.BaseDirectory, "runs", "MultipleDriverSessionTestData.csv");

    private static SessionDeviceEventDto Ev(
        string id,
        SessionEventTypeDto type,
        int lap,
        int sector,
        DateTimeOffset baseTs,
        double ms,
        bool deleted = false)
        => new SessionDeviceEventDto
        {
            Id = id,
            Type = type,
            LapNumber = lap,
            SectorNumber = sector,
            Time = TimeSpan.FromMilliseconds(ms),
            Timestamp = baseTs.AddMilliseconds(ms),
            Deleted = deleted ? DateTime.UtcNow : (DateTime?)null
        };

    private static SessionDataDeviceDto DeviceDto(
        string deviceId,
        string displayName,
        string category,
        IEnumerable<SessionDeviceEventDto> events)
        => new SessionDataDeviceDto
        {
            DeviceId = deviceId,
            DisplayName = displayName,
            Category = category,
            Type = DeviceTypeDto.LapTimer,
            Events = events.ToList()
        };

    private static LiveTimingDataDeviceEventType Map(SessionEventTypeDto dtoType) =>
    dtoType == SessionEventTypeDto.Lap
        ? LiveTimingDataDeviceEventType.Lap
        : LiveTimingDataDeviceEventType.Sector;

    private static LiveTimingDataView NewView() => new LiveTimingDataView();

    public sealed class AddDeviceEvents
    {
        [Fact]
        public void Creates_Device_And_Rebuilds_On_First_Fill()
        {
            var view = NewView();

            var t0 = DateTimeOffset.UtcNow;
            var dto = DeviceDto(
                "dev-1",
                "Alice",
                "Karts",
                new[]
                {
                    Ev("e1", SessionEventTypeDto.Sector, 1, 1, t0, 10_000),
                    Ev("e2", SessionEventTypeDto.Lap,    1, 0, t0, 30_000),
                });

            bool propertyChanged = false;
            view.PropertyChanged += (_, e) => { if (e.PropertyName == "Updated") propertyChanged = true; };

            var result = view.AddDeviceEvents(dto, skipStateCalculation: false);

            Assert.True(result.StatisticsRebuilt);         // first time forces rebuild
            Assert.True(propertyChanged);                  // Updated fired
            Assert.Single(view.Devices);
            var dev = view.Devices[0];
            Assert.Equal("dev-1", dev.Id);
            Assert.Equal("Alice", dev.Info.DisplayName);
            Assert.Equal("Karts", dev.Info.Category);

            Assert.NotNull(view.BestLap);
            Assert.Equal(LiveTimingDataDeviceEventType.Lap, view.BestLap.Type);
            Assert.Equal(TimeSpan.FromMilliseconds(30_000), view.BestLap.Time);

            // PB/Overall flags set by UpdateStatistics during AddDeviceEvents
            var lap = dev.Events.First(x => x.Type == LiveTimingDataDeviceEventType.Lap);
            Assert.True(lap.WasPersonalBest);
            Assert.True(lap.WasBestOverall);
        }

        [Fact]
        public void OutOfOrder_Timestamp_Forces_Rebuild()
        {
            var view = NewView();
            var baseTs = DateTimeOffset.UtcNow;

            // First pass — establishes _lastEventOfSessionTimeStamp
            var dto1 = DeviceDto("dev-1", "Alice", "Karts", new[]
            {
                Ev("e1", SessionEventTypeDto.Sector, 1, 1, baseTs, 10_000)
            });
            var r1 = view.AddDeviceEvents(dto1, skipStateCalculation: false);
            Assert.True(r1.StatisticsRebuilt);

            // Second pass — older timestamp -> shouldRebuild = true
            var dto2 = DeviceDto("dev-1", "Alice", "Karts", new[]
            {
                Ev("e2", SessionEventTypeDto.Sector, 1, 2, baseTs,  9_000) // earlier than previous
            });
            var r2 = view.AddDeviceEvents(dto2, skipStateCalculation: false);
            Assert.True(r2.StatisticsRebuilt);
        }

        [Fact]
        public void SoftDelete_Existing_Event_Marks_Deleted_And_Rebuilds()
        {
            var view = NewView();
            var t0 = DateTimeOffset.UtcNow;

            // create event
            var create = DeviceDto("dev-1", "Alice", "Karts", new[]
            {
                Ev("eL", SessionEventTypeDto.Lap, 1, 0, t0, 31_000)
            });
            var r1 = view.AddDeviceEvents(create, skipStateCalculation: false);
            Assert.True(r1.StatisticsRebuilt);

            // soft delete it
            var delete = DeviceDto("dev-1", "Alice", "Karts", new[]
            {
                Ev("eL", SessionEventTypeDto.Lap, 1, 0, t0, 31_000, deleted: true)
            });
            var r2 = view.AddDeviceEvents(delete, skipStateCalculation: false);

            Assert.True(r2.StatisticsRebuilt); // deletion triggers rebuild
            var dev = view.Devices.Single();
            var deleted = dev.Events.Single(x => x.Id == "eL");
            Assert.NotNull(deleted.Deleted);
        }

        [Fact]
        public void Updates_Device_Info_When_Provided()
        {
            var view = NewView();
            var t0 = DateTimeOffset.UtcNow;

            var create = DeviceDto("dev-1", "A", "Cat1", new[]
            {
                Ev("e1", SessionEventTypeDto.Sector, 1, 1, t0, 10_000)
            });
            view.AddDeviceEvents(create, skipStateCalculation: true);

            var update = DeviceDto("dev-1", "Alice", "Karts", new[]
            {
                Ev("e2", SessionEventTypeDto.Sector, 1, 2, t0, 11_000)
            });
            view.AddDeviceEvents(update, skipStateCalculation: true);

            var dev = view.Devices.Single();
            Assert.Equal("Alice", dev.Info.DisplayName);
            Assert.Equal("Karts", dev.Info.Category);
        }
    }

    public sealed class Rebuild_And_Bests
    {
        [Fact]
        public void Rebuild_Sorts_Chronologically_And_Computes_Bests()
        {
            var view = NewView();
            var t0 = DateTimeOffset.UtcNow;

            // Two devices, mix order & zero-time sector (ignored)
            var d1 = DeviceDto("A", "Alpha", "C1", new[]
            {
                Ev("a1", SessionEventTypeDto.Sector, 1, 1, t0, 10_000),
                Ev("aL", SessionEventTypeDto.Lap,    1, 0, t0, 30_000),
                Ev("aZ", SessionEventTypeDto.Sector, 2, 1, t0,  0),      // ignored (zero time)
            });
            var d2 = DeviceDto("B", "Bravo", "C1", new[]
            {
                Ev("b1", SessionEventTypeDto.Sector, 1, 1, t0,  9_500),
                Ev("bL", SessionEventTypeDto.Lap,    1, 0, t0, 29_500),
            });

            view.AddDeviceEvents(d1, skipStateCalculation: true);
            view.AddDeviceEvents(d2, skipStateCalculation: true);

            // Rebuild computes PBs and overall
            view.RebuildStatistics();

            Assert.Equal("B", view.BestLap.Device.Id);
            Assert.Equal(TimeSpan.FromMilliseconds(29_500), view.BestLap.Time);

            // Best sector for #1 is from device B (9.5s vs 10s)
            Assert.True(view.BestSectors.TryGetValue(1, out var bestS1));
            Assert.Equal("B", bestS1.Device.Id);

            // LastEvent/LastLap set per device
            foreach (var dev in view.Devices)
            {
                Assert.NotNull(dev.LastEvent);
                Assert.NotNull(dev.LastLap);
                Assert.True(dev.LastLap.Type == LiveTimingDataDeviceEventType.Lap);
            }
        }
    }

    public sealed class MarkDeviceDeleted_Tests
    {
        [Fact]
        public void Marks_Device_Deleted_And_Rebuilds_Bests()
        {
            var view = NewView();
            var t0 = DateTimeOffset.UtcNow;

            // Two devices, B is globally best
            view.AddDeviceEvents(DeviceDto("A", "Alpha", "C", new[]
            {
                Ev("aL", SessionEventTypeDto.Lap, 1, 0, t0, 31_000)
            }), skipStateCalculation: false);

            view.AddDeviceEvents(DeviceDto("B", "Bravo", "C", new[]
            {
                Ev("bL", SessionEventTypeDto.Lap, 1, 0, t0, 29_000)
            }), skipStateCalculation: false);

            var devA = view.Devices.Single(d => d.Id == "A");
            var devB = view.Devices.Single(d => d.Id == "B");

            // Delete the best device (B) → best should switch to A
            view.MarkDeviceDeleted(devB);
            Assert.Equal("A", view.BestLap.Device.Id);

            // Delete remaining device → derived state reset
            view.MarkDeviceDeleted(devA);
            Assert.Null(view.BestLap);
            Assert.Empty(view.BestSectors);
            Assert.True(view.Devices.All(d => d.BestSectors.Count == 0 && d.BestLap == null));
        }
    }

    public sealed class GetRanking_Tests
    {
        [Fact]
        public void Builds_Ranking_With_Gap_Interval_And_PreviousRank()
        {
            var view = NewView();
            var t0 = DateTimeOffset.UtcNow;

            // three devices with distinct best laps
            view.AddDeviceEvents(DeviceDto("A", "Alpha", "C", new[]
            {
                Ev("a1", SessionEventTypeDto.Lap, 1, 0, t0, 30_500)
            }), false);

            view.AddDeviceEvents(DeviceDto("B", "Bravo", "C", new[]
            {
                Ev("b1", SessionEventTypeDto.Lap, 1, 0, t0, 29_900)
            }), false);

            view.AddDeviceEvents(DeviceDto("C", "Charlie", "C", new[]
            {
                Ev("c1", SessionEventTypeDto.Lap, 1, 0, t0, 31_000)
            }), false);

            // First ranking snapshot
            var r1 = view.GetRanking(LiveTimingDataRankingType.Qualifying);
            Assert.Equal(3, r1.Rows.Count);
            Assert.Collection(
                r1.Rows,
                r => Assert.Equal("B", r.DeviceId),  // best
                r => Assert.Equal("A", r.DeviceId),
                r => Assert.Equal("C", r.DeviceId)
            );

            // Gap: row 2 vs global best
            var rowA = r1.Rows[1];
            Assert.True(rowA.Gap.HasValue);
            Assert.Equal(TimeSpan.FromMilliseconds(600), rowA.Gap.Value); // 30.5 - 29.9

            // Interval vs previous row (A vs B)
            Assert.True(rowA.Interval.HasValue);
            Assert.Equal(TimeSpan.FromMilliseconds(600), rowA.Interval.Value);

            // Second snapshot with C improving to the top → ranks change and PreviousRank is tracked
            view.AddDeviceEvents(DeviceDto("C", "Charlie", "C", new[]
            {
                Ev("c2", SessionEventTypeDto.Lap, 2, 0, t0.AddMinutes(1), 29_700)
            }), false);

            var r2 = view.GetRanking(LiveTimingDataRankingType.Qualifying, r1);
            Assert.Equal(3, r2.Rows.Count);

            var r2_top = r2.Rows[0];
            Assert.Equal("C", r2_top.DeviceId);
            Assert.Equal(1, r2_top.Rank);
            Assert.Equal(3, r2_top.PreviousRank); // jumped from #3 to #1
            Assert.True(r2_top.HasChanged);
        }

        [Fact]
        public void Builds_Ranking_With_Multiple_Driver_Session_Data()
        {
            DateTime start = new DateTime(1976, 5, 29, 5, 0, 0, DateTimeKind.Utc);
            var view = NewView();

            // Collect expected rows so we can assert after the rebuild
            var expected = new List<(string deviceId, DateTimeOffset ts, LiveTimingDataDeviceEventType type, int lap, int sector, TimeSpan time, bool pb, bool ob)>();

            using var reader = new StreamReader(MultipleDriverSessionDataCsvPath);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("DeviceId"))
                    continue;

                var parts = line.Split(';');
                if (parts.Length != 9)
                    continue;

                var deviceId = parts[0];

                // CSV Timestamp is milliseconds offset from "start" (can be negative for early sector rows)
                var ts = start.AddMilliseconds(Convert.ToInt32(parts[1]));

                var dtoType = parts[2] == "Lap" ? SessionEventTypeDto.Lap : SessionEventTypeDto.Sector;
                var type = Map(dtoType);

                var lapNumber = Convert.ToInt32(parts[3]);
                var sectorNumber = Convert.ToInt32(parts[4]);

                // CSV stores TimeFull as "ticks/10000" (i.e., milliseconds * 1000 * 10). Convert back to ms.
                // e.g., 647745000 / 10000 = 64774.5 ms
                var timeFull = TimeSpan.FromMilliseconds(Convert.ToInt32(parts[5]) / 10000d);

                var isPersonnalBest = parts[7] != "False";
                var isBestOverall = parts[8] != "False";

                expected.Add((deviceId, ts, type, lapNumber, sectorNumber, timeFull, isPersonnalBest, isBestOverall));

                // Create one-event DTO for streaming into the view.
                // IMPORTANT: Live helper Ev(...) adds the duration (ms) to the base timestamp to produce the final Timestamp.
                // We want the final Timestamp to be exactly 'ts' from CSV, so we pass a base of (ts - time).
                var baseForEvent = ts.AddMilliseconds(-timeFull.TotalMilliseconds);

                var dto = DeviceDto(
                    deviceId,
                    deviceId,
                    string.Empty,
                    new[]
                    {
                    Ev(Guid.NewGuid().ToString(), dtoType, lapNumber, sectorNumber, baseForEvent, timeFull.TotalMilliseconds),
                    });

                // Skip per-event live updates; we will rebuild once at the end
                view.AddDeviceEvents(dto, skipStateCalculation: true);
            }

            // Now compute all derived stats & flags
            view.RebuildStatistics();

            // For each CSV row, there must be a matching event with the exact flags
            foreach (var row in expected)
            {
                var dev = view.Devices.FirstOrDefault(d => d.Id == row.deviceId);
                Assert.NotNull(dev);

                // Find the event by Type/Lap/Sector/Timestamp and (tolerant) duration match
                var match = dev.Events.FirstOrDefault(e =>
                    e.Type == row.type &&
                    e.Lap == row.lap &&
                    e.Sector == row.sector &&
                    e.Timestamp == row.ts &&
                    Math.Abs((e.Time - row.time).TotalMilliseconds) <= 0.5);

                Assert.NotNull(match);

                Assert.Equal(row.pb, match.WasPersonalBest);
                Assert.Equal(row.ob, match.WasBestOverall);
            }

            // Sanity check: final global best lap should be Device2 with 54.93s
            var best = view.BestLap;
            Assert.NotNull(best);
            Assert.Equal("Device2", best.Device.Id);
            Assert.InRange(best.Time.TotalMilliseconds, 54932 - 0.5, 54932 + 0.5);
        }
    }

    public sealed class PropertyChanged_Tests
    {
        [Fact]
        public void Updated_Raises_PropertyChanged_On_Add_And_Delete()
        {
            var view = NewView();
            var t0 = DateTimeOffset.UtcNow;

            int fired = 0;
            view.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Updated") Interlocked.Increment(ref fired);
            };

            var r1 = view.AddDeviceEvents(DeviceDto("D", "Delta", "C", new[]
            {
                Ev("d1", SessionEventTypeDto.Lap, 1, 0, t0, 30_000)
            }), false);
            Assert.True(r1.StatisticsRebuilt);

            var device = view.Devices.Single();
            view.MarkDeviceDeleted(device);

            Assert.True(fired >= 2); // at least once on Add & once on Delete
        }
    }
}
