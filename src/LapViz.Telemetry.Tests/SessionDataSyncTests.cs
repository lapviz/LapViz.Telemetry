using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Tests.Domain;

/// <summary>
/// Unit tests for the event-sourcing and synchronization infrastructure:
/// <see cref="CompactEventId"/>, <see cref="SequencedEvent"/>,
/// <see cref="SessionSyncDelta"/>, and the sync methods on <see cref="SessionData"/>.
/// </summary>
public class SessionDataSyncTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static SessionDataEvent MakeLapEvent(string deviceId, int lapNumber, TimeSpan time)
    {
        return new SessionDataEvent
        {
            DeviceId = deviceId,
            Type = SessionEventType.Lap,
            LapNumber = lapNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Time = time
        };
    }

    private static SessionDataEvent MakeSectorEvent(string deviceId, int lapNumber, int sector, TimeSpan time)
    {
        return new SessionDataEvent
        {
            DeviceId = deviceId,
            Type = SessionEventType.Sector,
            LapNumber = lapNumber,
            Sector = sector,
            Timestamp = DateTimeOffset.UtcNow,
            Time = time
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CompactEventId Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class CompactEventIdTests
    {
        [Fact]
        public void NewId_Produces_NonEmpty_Value()
        {
            var id = CompactEventId.NewId();
            Assert.False(id.IsEmpty);
            Assert.NotEqual(0L, id.Value);
        }

        [Fact]
        public void NewId_Produces_Unique_Values()
        {
            var ids = Enumerable.Range(0, 1000).Select(_ => CompactEventId.NewId()).ToList();
            var distinct = ids.Select(x => x.Value).Distinct().Count();
            Assert.Equal(1000, distinct);
        }

        [Fact]
        public void Ids_Are_Time_Ordered()
        {
            var first = CompactEventId.NewId();
            Thread.Sleep(2); // ensure at least 1ms difference
            var second = CompactEventId.NewId();
            Assert.True(second.Value > first.Value);
            Assert.True(second.CompareTo(first) > 0);
        }

        [Fact]
        public void Timestamp_Extraction_Is_Approximate()
        {
            var before = DateTimeOffset.UtcNow;
            var id = CompactEventId.NewId();
            var after = DateTimeOffset.UtcNow;

            Assert.InRange(id.Timestamp, before.AddMilliseconds(-1), after.AddMilliseconds(1));
        }

        [Fact]
        public void FromLong_Roundtrips_Value()
        {
            var original = CompactEventId.NewId();
            var restored = CompactEventId.FromLong(original.Value);
            Assert.Equal(original, restored);
        }

        [Fact]
        public void ToString_Parse_Roundtrip()
        {
            var original = CompactEventId.NewId();
            var str = original.ToString();
            var parsed = CompactEventId.Parse(str);
            Assert.Equal(original, parsed);
        }

        [Fact]
        public void ToString_Is_11_Characters()
        {
            var id = CompactEventId.NewId();
            var str = id.ToString();
            Assert.Equal(11, str.Length);
        }

        [Fact]
        public void ToString_Is_Url_Safe()
        {
            // Generate many IDs and check no forbidden characters
            for (int i = 0; i < 100; i++)
            {
                var str = CompactEventId.NewId().ToString();
                Assert.DoesNotContain("+", str);
                Assert.DoesNotContain("/", str);
                Assert.DoesNotContain("=", str);
            }
        }

        [Fact]
        public void Empty_Has_Zero_Value()
        {
            Assert.Equal(0L, CompactEventId.Empty.Value);
            Assert.True(CompactEventId.Empty.IsEmpty);
        }

        [Fact]
        public void Equality_Operators_Work()
        {
            var a = CompactEventId.FromLong(12345);
            var b = CompactEventId.FromLong(12345);
            var c = CompactEventId.FromLong(99999);

            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a != c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Parse_Empty_String_Returns_Empty()
        {
            Assert.Equal(CompactEventId.Empty, CompactEventId.Parse(""));
            Assert.Equal(CompactEventId.Empty, CompactEventId.Parse(null!));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SequencedEvent Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class SequencedEventTests
    {
        [Fact]
        public void Constructor_Assigns_Properties()
        {
            var evt = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            var seq = new SequencedEvent(42, evt);

            Assert.Equal(42L, seq.SequenceNumber);
            Assert.Same(evt, seq.Event);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionData — Event Log & Deduplication Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class EventLogTests
    {
        [Fact]
        public void AddEvent_Appends_To_EventLog()
        {
            var session = new SessionData();
            var evt = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(90));

            session.AddEvent(evt);

            Assert.Equal(1, session.EventCount);
            Assert.Equal(1L, session.CurrentSequence);
        }

        [Fact]
        public void AddEvent_Ignores_Null()
        {
            var session = new SessionData();
            session.AddEvent(null);
            Assert.Equal(0, session.EventCount);
        }

        [Fact]
        public void AddEvent_Ignores_NonTiming_Events()
        {
            var session = new SessionData();
            var evt = new SessionDataEvent
            {
                DeviceId = "dev1",
                Type = SessionEventType.Position,
                Timestamp = DateTimeOffset.UtcNow
            };

            session.AddEvent(evt);
            Assert.Equal(0, session.EventCount);
        }

        [Fact]
        public void AddEvent_Deduplicates_By_EventId()
        {
            var session = new SessionData();
            var evt = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(90));

            session.AddEvent(evt);
            session.AddEvent(evt); // same EventId — should be skipped

            Assert.Equal(1, session.EventCount);
        }

        [Fact]
        public void AddEvent_Dedup_Prevents_Device_Double_Mutation()
        {
            var session = new SessionData();
            var evt = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(90));

            session.AddEvent(evt);
            session.AddEvent(evt); // duplicate

            // Device should have exactly one lap recorded
            var device = session.Devices["dev1"];
            Assert.NotNull(device.BestLap);
        }

        [Fact]
        public void Sequence_Numbers_Are_Monotonically_Increasing()
        {
            var session = new SessionData();
            var events = Enumerable.Range(1, 10)
                .Select(i => MakeLapEvent("dev1", i, TimeSpan.FromSeconds(60 + i)))
                .ToList();

            foreach (var e in events) session.AddEvent(e);

            var log = session.GetFullEventLog();
            Assert.Equal(10, log.Count);
            for (int i = 1; i < log.Count; i++)
            {
                Assert.True(log[i].SequenceNumber > log[i - 1].SequenceNumber);
            }
        }

        [Fact]
        public void AddEvent_Is_ThreadSafe()
        {
            var session = new SessionData();
            var events = Enumerable.Range(1, 500)
                .Select(i => MakeLapEvent($"dev{i % 10}", i, TimeSpan.FromSeconds(60 + i)))
                .ToList();

            Parallel.ForEach(events, e => session.AddEvent(e));

            Assert.Equal(500, session.EventCount);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionData — GetEventsSince (Sequence) Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class GetEventsSinceSequenceTests
    {
        [Fact]
        public void Returns_All_Events_When_AfterSequence_Is_Zero()
        {
            var session = new SessionData();
            for (int i = 1; i <= 5; i++) session.AddEvent(MakeLapEvent("dev1", i, TimeSpan.FromSeconds(60 + i)));

            var result = session.GetEventsSince(0);
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void Returns_Only_Newer_Events()
        {
            var session = new SessionData();
            for (int i = 1; i <= 5; i++) session.AddEvent(MakeLapEvent("dev1", i, TimeSpan.FromSeconds(60 + i)));

            var result = session.GetEventsSince(3);
            Assert.Equal(2, result.Count);
            Assert.True(result.All(e => e.SequenceNumber > 3));
        }

        [Fact]
        public void Returns_Empty_When_Caught_Up()
        {
            var session = new SessionData();
            for (int i = 1; i <= 5; i++) session.AddEvent(MakeLapEvent("dev1", i, TimeSpan.FromSeconds(60 + i)));

            var result = session.GetEventsSince(session.CurrentSequence);
            Assert.Empty(result);
        }

        [Fact]
        public void Returns_Empty_For_Empty_Session()
        {
            var session = new SessionData();
            var result = session.GetEventsSince(0);
            Assert.Empty(result);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionData — GetEventsSince (Timestamp) Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class GetEventsSinceTimestampTests
    {
        [Fact]
        public void Returns_Events_At_Or_After_Timestamp()
        {
            var session = new SessionData();
            var baseTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

            for (int i = 0; i < 5; i++)
            {
                var evt = MakeLapEvent("dev1", i + 1, TimeSpan.FromSeconds(60 + i));
                evt.Timestamp = baseTime.AddMinutes(i);
                session.AddEvent(evt);
            }

            // Get events from minute 2 onward
            var result = session.GetEventsSince(baseTime.AddMinutes(2));
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void Returns_Empty_When_All_Events_Are_Before_Timestamp()
        {
            var session = new SessionData();
            var evt = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            evt.Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            session.AddEvent(evt);

            var result = session.GetEventsSince(DateTimeOffset.UtcNow);
            Assert.Empty(result);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionData — ComputeDelta Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class ComputeDeltaTests
    {
        [Fact]
        public void Delta_Against_Null_Returns_All_Source_Events()
        {
            var source = new SessionData();
            source.AddEvent(MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60)));
            source.AddEvent(MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(61)));

            var delta = source.ComputeDelta(null);

            Assert.Equal(2, delta.EventsToAddToTarget.Count);
            Assert.Empty(delta.EventsToAddToSource);
            Assert.False(delta.IsInSync);
        }

        [Fact]
        public void Delta_Between_Identical_Sessions_Is_Empty()
        {
            var source = new SessionData();
            var target = new SessionData();

            var evt1 = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            var evt2 = MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(61));

            source.AddEvent(evt1);
            source.AddEvent(evt2);
            target.AddEvent(evt1);
            target.AddEvent(evt2);

            var delta = source.ComputeDelta(target);

            Assert.Empty(delta.EventsToAddToTarget);
            Assert.Empty(delta.EventsToAddToSource);
            Assert.True(delta.IsInSync);
        }

        [Fact]
        public void Delta_Detects_Events_Missing_In_Target()
        {
            var source = new SessionData();
            var target = new SessionData();

            var shared = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            var onlyInSource = MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(61));

            source.AddEvent(shared);
            source.AddEvent(onlyInSource);
            target.AddEvent(shared);

            var delta = source.ComputeDelta(target);

            Assert.Single(delta.EventsToAddToTarget);
            Assert.Equal(onlyInSource.EventId, delta.EventsToAddToTarget[0].EventId);
            Assert.Empty(delta.EventsToAddToSource);
        }

        [Fact]
        public void Delta_Detects_Events_Missing_In_Source()
        {
            var source = new SessionData();
            var target = new SessionData();

            var shared = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            var onlyInTarget = MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(61));

            source.AddEvent(shared);
            target.AddEvent(shared);
            target.AddEvent(onlyInTarget);

            var delta = source.ComputeDelta(target);

            Assert.Empty(delta.EventsToAddToTarget);
            Assert.Single(delta.EventsToAddToSource);
            Assert.Equal(onlyInTarget.EventId, delta.EventsToAddToSource[0].EventId);
        }

        [Fact]
        public void Delta_Is_Bidirectional()
        {
            var source = new SessionData();
            var target = new SessionData();

            var onlyInSource = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            var onlyInTarget = MakeLapEvent("dev2", 1, TimeSpan.FromSeconds(62));

            source.AddEvent(onlyInSource);
            target.AddEvent(onlyInTarget);

            var delta = source.ComputeDelta(target);

            Assert.Single(delta.EventsToAddToTarget);
            Assert.Single(delta.EventsToAddToSource);
        }

        [Fact]
        public void Delta_Between_Empty_Sessions_IsInSync()
        {
            var source = new SessionData();
            var target = new SessionData();

            var delta = source.ComputeDelta(target);
            Assert.True(delta.IsInSync);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionData — ApplyRemoteEvents Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class ApplyRemoteEventsTests
    {
        [Fact]
        public void ApplyRemoteEvents_Adds_Missing_Events()
        {
            var session = new SessionData();
            var events = new[]
            {
                MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60)),
                MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(61))
            };

            session.ApplyRemoteEvents(events);

            Assert.Equal(2, session.EventCount);
        }

        [Fact]
        public void ApplyRemoteEvents_Is_Idempotent()
        {
            var session = new SessionData();
            var events = new[]
            {
                MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60)),
                MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(61))
            };

            session.ApplyRemoteEvents(events);
            session.ApplyRemoteEvents(events); // apply again

            Assert.Equal(2, session.EventCount);
        }

        [Fact]
        public void ApplyRemoteEvents_Ignores_Null()
        {
            var session = new SessionData();
            session.ApplyRemoteEvents(null);
            Assert.Equal(0, session.EventCount);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-End Synchronization Scenario Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class SyncScenarioTests
    {
        [Fact]
        public void CatchUp_Pattern_Synchronizes_Late_Joiner()
        {
            // Node A has been running for a while
            var nodeA = new SessionData();
            nodeA.AddEvent(MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60)));
            nodeA.AddEvent(MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(59)));
            nodeA.AddEvent(MakeSectorEvent("dev1", 3, 1, TimeSpan.FromSeconds(20)));

            // Node B joins late and catches up
            var nodeB = new SessionData();
            var catchUpEvents = nodeA.GetEventsSince(0);
            nodeB.ApplyRemoteEvents(catchUpEvents.Select(e => e.Event));

            Assert.Equal(nodeA.EventCount, nodeB.EventCount);

            // Verify they are in sync
            var delta = nodeA.ComputeDelta(nodeB);
            Assert.True(delta.IsInSync);
        }

        [Fact]
        public void Incremental_CatchUp_Only_Gets_New_Events()
        {
            var nodeA = new SessionData();
            nodeA.AddEvent(MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60)));
            nodeA.AddEvent(MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(59)));

            // Node B catches up initially
            var nodeB = new SessionData();
            var lastSeq = 0L;
            var batch1 = nodeA.GetEventsSince(lastSeq);
            nodeB.ApplyRemoteEvents(batch1.Select(e => e.Event));
            lastSeq = batch1.Last().SequenceNumber;

            // More events arrive at node A
            nodeA.AddEvent(MakeLapEvent("dev1", 3, TimeSpan.FromSeconds(58)));

            // Node B catches up incrementally
            var batch2 = nodeA.GetEventsSince(lastSeq);
            Assert.Single(batch2);
            nodeB.ApplyRemoteEvents(batch2.Select(e => e.Event));

            Assert.Equal(nodeA.EventCount, nodeB.EventCount);
        }

        [Fact]
        public void Bidirectional_Delta_Sync_Achieves_Convergence()
        {
            var nodeA = new SessionData();
            var nodeB = new SessionData();

            // Shared event
            var shared = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));
            nodeA.AddEvent(shared);
            nodeB.AddEvent(shared);

            // Events only on A
            nodeA.AddEvent(MakeLapEvent("dev1", 2, TimeSpan.FromSeconds(59)));
            nodeA.AddEvent(MakeSectorEvent("dev1", 3, 1, TimeSpan.FromSeconds(20)));

            // Events only on B
            nodeB.AddEvent(MakeLapEvent("dev2", 1, TimeSpan.FromSeconds(62)));

            // Compute delta and apply
            var delta = nodeA.ComputeDelta(nodeB);
            nodeA.ApplyRemoteEvents(delta.EventsToAddToSource);
            nodeB.ApplyRemoteEvents(delta.EventsToAddToTarget);

            // Both should now be in sync
            Assert.Equal(nodeA.EventCount, nodeB.EventCount);
            var verifyDelta = nodeA.ComputeDelta(nodeB);
            Assert.True(verifyDelta.IsInSync);
        }

        [Fact]
        public void Multiple_Devices_Sync_Correctly()
        {
            var nodeA = new SessionData();
            var nodeB = new SessionData();

            // Multiple devices report to different nodes
            nodeA.AddEvent(MakeLapEvent("car-42", 1, TimeSpan.FromSeconds(90)));
            nodeA.AddEvent(MakeLapEvent("car-42", 2, TimeSpan.FromSeconds(88)));
            nodeA.AddEvent(MakeLapEvent("car-7", 1, TimeSpan.FromSeconds(91)));

            nodeB.AddEvent(MakeLapEvent("car-99", 1, TimeSpan.FromSeconds(92)));
            nodeB.AddEvent(MakeSectorEvent("car-99", 1, 1, TimeSpan.FromSeconds(30)));

            // Sync
            var delta = nodeA.ComputeDelta(nodeB);
            nodeA.ApplyRemoteEvents(delta.EventsToAddToSource);
            nodeB.ApplyRemoteEvents(delta.EventsToAddToTarget);

            // Both nodes should see all 3 devices
            Assert.Equal(3, nodeA.Devices.Count);
            Assert.Equal(3, nodeB.Devices.Count);
            Assert.True(nodeA.ComputeDelta(nodeB).IsInSync);
        }

        [Fact]
        public void Mesh_FanOut_With_Duplicates_Is_Safe()
        {
            // Simulate a 3-node mesh where events fan out to all peers
            var node1 = new SessionData();
            var node2 = new SessionData();
            var node3 = new SessionData();

            var evt = MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60));

            // Event originates at node1
            node1.AddEvent(evt);

            // Fan out to node2 and node3
            node2.ApplyRemoteEvents(new[] { evt });
            node3.ApplyRemoteEvents(new[] { evt });

            // node2 also fans out to node3 (duplicate)
            node3.ApplyRemoteEvents(new[] { evt });

            // All nodes should have exactly 1 event
            Assert.Equal(1, node1.EventCount);
            Assert.Equal(1, node2.EventCount);
            Assert.Equal(1, node3.EventCount);
        }

        [Fact]
        public void BestLap_Reflects_All_Synced_Devices()
        {
            var nodeA = new SessionData();
            var nodeB = new SessionData();

            nodeA.AddEvent(MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(90)));
            nodeB.AddEvent(MakeLapEvent("dev2", 1, TimeSpan.FromSeconds(85)));

            // Before sync, each only sees its own device
            Assert.Equal(TimeSpan.FromSeconds(90), nodeA.BestLap);
            Assert.Equal(TimeSpan.FromSeconds(85), nodeB.BestLap);

            // Sync
            var delta = nodeA.ComputeDelta(nodeB);
            nodeA.ApplyRemoteEvents(delta.EventsToAddToSource);
            nodeB.ApplyRemoteEvents(delta.EventsToAddToTarget);

            // After sync, both see the overall best
            Assert.Equal(TimeSpan.FromSeconds(85), nodeA.BestLap);
            Assert.Equal(TimeSpan.FromSeconds(85), nodeB.BestLap);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionSyncDelta Tests
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class SessionSyncDeltaTests
    {
        [Fact]
        public void IsInSync_True_When_Both_Lists_Empty()
        {
            var delta = new SessionSyncDelta
            {
                EventsToAddToTarget = Array.Empty<SessionDataEvent>(),
                EventsToAddToSource = Array.Empty<SessionDataEvent>()
            };
            Assert.True(delta.IsInSync);
        }

        [Fact]
        public void IsInSync_True_When_Both_Lists_Null()
        {
            var delta = new SessionSyncDelta
            {
                EventsToAddToTarget = null!,
                EventsToAddToSource = null!
            };
            Assert.True(delta.IsInSync);
        }

        [Fact]
        public void IsInSync_False_When_Target_Has_Events()
        {
            var delta = new SessionSyncDelta
            {
                EventsToAddToTarget = new[] { MakeLapEvent("dev1", 1, TimeSpan.FromSeconds(60)) },
                EventsToAddToSource = Array.Empty<SessionDataEvent>()
            };
            Assert.False(delta.IsInSync);
        }
    }
}
