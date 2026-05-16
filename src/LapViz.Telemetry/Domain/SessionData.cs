using System;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LapViz.Telemetry.Domain
{
    /// <summary>
    /// Aggregates telemetry session data across multiple devices. This class is thread-safe.
    /// 
    /// <para><b>Synchronization model (inspired by multiplayer game netcode):</b></para>
    /// <list type="bullet">
    ///   <item><b>Event log with sequence numbers</b> — every ingested event is assigned a
    ///   monotonically increasing sequence number, enabling reliable ordered replay (similar to
    ///   a reliable ordered channel in game networking).</item>
    ///   <item><b>Catch-up streaming</b> — consumers can request all events since a given sequence
    ///   number or timestamp, enabling late-joiners to synchronize (snapshot + delta pattern).</item>
    ///   <item><b>Bidirectional delta sync</b> — two instances can be compared to produce a
    ///   <see cref="SessionSyncDelta"/> describing what each side is missing (server reconciliation).</item>
    /// </list>
    /// 
    /// <para><b>Thread-safety guarantees:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="Devices"/> is a <see cref="ConcurrentDictionary{TKey, TValue}"/> — safe
    ///   for concurrent reads/writes of different devices.</item>
    ///   <item>Per-device mutations are serialized via fine-grained locks (one lock object per device)
    ///   to avoid contention across unrelated devices.</item>
    ///   <item>The event log is protected by a single lock (<c>_eventLogLock</c>); this is acceptable
    ///   because appends are fast O(1) amortized and reads take a snapshot.</item>
    /// </list>
    /// 
    /// <para><b>Developer notes:</b></para>
    /// <list type="bullet">
    ///   <item>Deduplication is performed FIRST (via <see cref="CompactEventId"/>) before mutating
    ///   device state, ensuring duplicate remote events never corrupt aggregated data.</item>
    ///   <item>Sequence numbers are instance-local (not globally unique). For cross-network identity,
    ///   always use <see cref="SessionDataEvent.EventId"/>.</item>
    ///   <item>The event log is append-only and never trimmed in this implementation. For long-running
    ///   sessions, consider adding a compaction/snapshotting mechanism.</item>
    /// </list>
    /// </summary>
    public class SessionData
    {
        // ─── Locking infrastructure ──────────────────────────────────────────
        // Fine-grained: one lock per device avoids contention between unrelated devices.
        private readonly ConcurrentDictionary<string, object> _deviceLocks =
            new ConcurrentDictionary<string, object>();

        // ─── Event log (append-only, for replay and synchronization) ─────────
        private readonly List<SequencedEvent> _eventLog = new List<SequencedEvent>();

        // O(1) membership test for deduplication. All access guarded by _eventLogLock.
        private readonly HashSet<CompactEventId> _knownEventIds = new HashSet<CompactEventId>();

        private readonly object _eventLogLock = new object();

        // Instance-local sequence counter. Incremented atomically inside _eventLogLock.
        private long _sequenceCounter;

        // ─── Public properties ───────────────────────────────────────────────

        /// <summary>
        /// Unique identifier for the session (e.g., race weekend + session type).
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// All device sessions participating in this session, keyed by device ID.
        /// Thread-safe for concurrent reads and writes via <see cref="ConcurrentDictionary{TKey, TValue}"/>.
        /// </summary>
        public ConcurrentDictionary<string, DeviceSessionData> Devices { get; }

        /// <summary>
        /// The current highest sequence number that has been assigned in this instance.
        /// Consumers can store this value and later call <see cref="GetEventsSince(long)"/>
        /// to retrieve only newer events (catch-up pattern).
        /// </summary>
        public long CurrentSequence => Interlocked.Read(ref _sequenceCounter);

        /// <summary>
        /// Total number of unique events ingested into this instance.
        /// </summary>
        public int EventCount
        {
            get { lock (_eventLogLock) { return _eventLog.Count; } }
        }

        public SessionData()
        {
            Devices = new ConcurrentDictionary<string, DeviceSessionData>();
        }

        // ─── Aggregated properties ───────────────────────────────────────────

        /// <summary>
        /// Gets the best lap time across all devices.
        /// Returns null if no valid laps are found.
        /// </summary>
        public TimeSpan? BestLap
        {
            get
            {
                var snapshot = Devices.Values.ToArray(); // snapshot for thread safety
                return snapshot
                    .Select(x => x.BestLap)
                    .Where(lap => lap != null && lap.Time > TimeSpan.Zero)
                    .OrderBy(lap => lap.Time)
                    .Select(lap => (TimeSpan?)lap.Time)
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// Gets the best sector times across all devices.
        /// Returns a dictionary where the key is the sector index and the value is the best time.
        /// </summary>
        public IDictionary<int, TimeSpan?> BestSectors
        {
            get
            {
                var snapshot = Devices.Values.ToArray(); // snapshot for thread safety

                return snapshot
                    .SelectMany(d => d.BestSectors ?? new Dictionary<int, SessionDataEvent>())
                    .Where(kvp => kvp.Value != null && kvp.Value.Time > TimeSpan.Zero)
                    .GroupBy(kvp => kvp.Key)
                    .ToDictionary(
                        g => g.Key,
                        g => (TimeSpan?)g.Min(kvp => kvp.Value.Time)
                    );
            }
        }

        // ─── Event ingestion ─────────────────────────────────────────────────

        /// <summary>
        /// Adds a timing event (lap or sector) to the session.
        /// 
        /// <para><b>Processing order:</b></para>
        /// <list type="number">
        ///   <item>Null/type guard — only Sector and Lap events are accepted.</item>
        ///   <item>Deduplication — if <see cref="SessionDataEvent.EventId"/> is already known,
        ///   the event is silently dropped (idempotent).</item>
        ///   <item>Device aggregation — the event is forwarded to the per-device container.</item>
        ///   <item>Event log append — a sequence number is assigned and the event is stored.</item>
        /// </list>
        /// 
        /// <para><b>Idempotency:</b> Safe to call multiple times with the same event
        /// (e.g., during retry or mesh fan-out). Duplicates are detected via
        /// <see cref="CompactEventId"/> in O(1).</para>
        /// </summary>
        public void AddEvent(SessionDataEvent sessionDataEvent)
        {
            if (sessionDataEvent == null) return;

            // Gate: only timing events are relevant for session aggregation
            if (sessionDataEvent.Type != SessionEventType.Sector &&
                sessionDataEvent.Type != SessionEventType.Lap)
            {
                return;
            }

            // ┌─────────────────────────────────────────────────────────────────┐
            // │ IMPORTANT: Deduplication MUST happen BEFORE device mutation.     │
            // │ Otherwise, a duplicate remote event would be applied twice to    │
            // │ DeviceSessionData, corrupting lap/sector aggregations.           │
            // └─────────────────────────────────────────────────────────────────┘
            lock (_eventLogLock)
            {
                if (!_knownEventIds.Add(sessionDataEvent.EventId))
                    return; // Already ingested — idempotent skip

                var seq = Interlocked.Increment(ref _sequenceCounter);
                _eventLog.Add(new SequencedEvent(seq, sessionDataEvent));
            }

            // Ensure the device container exists (atomic via ConcurrentDictionary)
            var device = Devices.GetOrAdd(
                sessionDataEvent.DeviceId,
                _ => new DeviceSessionData()
            );

            // Serialize mutations to this specific device (fine-grained lock)
            var devLock = _deviceLocks.GetOrAdd(sessionDataEvent.DeviceId, _ => new object());

            lock (devLock)
            {
                device.AddEvent(sessionDataEvent);
            }
        }

        // ─── Device access helpers ───────────────────────────────────────────

        /// <summary>
        /// Gets the <see cref="DeviceSessionData"/> for a device, creating it if missing. Thread-safe.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if <paramref name="deviceId"/> is null or whitespace.</exception>
        public DeviceSessionData GetOrCreateDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("deviceId must be non empty.", nameof(deviceId));

            return Devices.GetOrAdd(deviceId, _ => new DeviceSessionData());
        }

        /// <summary>
        /// Executes a thread-safe action against a specific device session.
        /// Creates the device if it does not exist yet.
        /// The action runs inside the per-device lock, so it is safe to mutate
        /// <see cref="DeviceSessionData"/> properties within it.
        /// </summary>
        public void WithDevice(string deviceId, Action<DeviceSessionData> action)
        {
            if (action == null) return;

            var device = Devices.GetOrAdd(deviceId, _ => new DeviceSessionData());
            var devLock = _deviceLocks.GetOrAdd(deviceId, _ => new object());

            lock (devLock)
            {
                action(device);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Streaming & Synchronization (Game Netcode patterns)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all events with a sequence number strictly greater than <paramref name="afterSequence"/>.
        /// 
        /// <para><b>Catch-up pattern:</b> A consumer stores <see cref="CurrentSequence"/> after
        /// processing a batch, then later calls this method with that stored value to get only
        /// events it hasn't seen. This is analogous to a game client reconnecting mid-match and
        /// requesting "everything since tick N".</para>
        /// 
        /// <para>Pass 0 to get the full history.</para>
        /// 
        /// <para><b>Complexity:</b> O(n) linear scan from the end (could be optimized to binary
        /// search since sequences are monotonic, but event counts per session are typically small
        /// enough that this is negligible).</para>
        /// </summary>
        public IReadOnlyList<SequencedEvent> GetEventsSince(long afterSequence)
        {
            lock (_eventLogLock)
            {
                if (_eventLog.Count == 0 || afterSequence >= _sequenceCounter)
                    return Array.Empty<SequencedEvent>();

                // Reverse scan to find the boundary (sequences are monotonically increasing).
                // For typical racing sessions (<10k events), this is faster than binary search
                // due to branch prediction and cache locality on the tail.
                var startIndex = 0;
                for (int i = _eventLog.Count - 1; i >= 0; i--)
                {
                    if (_eventLog[i].SequenceNumber <= afterSequence)
                    {
                        startIndex = i + 1;
                        break;
                    }
                }

                if (startIndex >= _eventLog.Count)
                    return Array.Empty<SequencedEvent>();

                return _eventLog.GetRange(startIndex, _eventLog.Count - startIndex).ToArray();
            }
        }

        /// <summary>
        /// Returns all events with a timestamp at or after <paramref name="since"/>.
        /// 
        /// <para><b>Use case:</b> Time-based replay or streaming from a wall-clock reference.
        /// Note that timestamps depend on device clocks and may not be perfectly ordered.
        /// For reliable ordering, prefer <see cref="GetEventsSince(long)"/>.</para>
        /// </summary>
        public IReadOnlyList<SequencedEvent> GetEventsSince(DateTimeOffset since)
        {
            lock (_eventLogLock)
            {
                return _eventLog
                    .Where(e => e.Event.Timestamp >= since)
                    .ToArray();
            }
        }

        /// <summary>
        /// Returns the full ordered event log as a defensive copy (snapshot).
        /// Safe to enumerate without holding locks.
        /// </summary>
        public IReadOnlyList<SequencedEvent> GetFullEventLog()
        {
            lock (_eventLogLock)
            {
                return _eventLog.ToArray();
            }
        }

        // ─── Delta synchronization ───────────────────────────────────────────

        /// <summary>
        /// Computes the bidirectional delta between this instance (source) and <paramref name="other"/> (target).
        /// Returns which events need to be added to each side to achieve convergence.
        /// 
        /// <para><b>Game netcode analogy — Server Reconciliation:</b> Both peers exchange their
        /// known event ID sets and each applies the operations they are missing. Because
        /// <see cref="AddEvent"/> is idempotent, applying the delta multiple times is safe.</para>
        /// 
        /// <para><b>Complexity:</b> O(|source| + |target|) — two HashSet constructions + two
        /// linear scans with O(1) membership tests.</para>
        /// 
        /// <para><b>Event identity:</b> Determined solely by <see cref="CompactEventId"/>
        /// (globally unique, 8-byte, time-ordered).</para>
        /// </summary>
        public SessionSyncDelta ComputeDelta(SessionData other)
        {
            if (other == null)
            {
                lock (_eventLogLock)
                {
                    return new SessionSyncDelta
                    {
                        EventsToAddToTarget = _eventLog.Select(e => e.Event).ToArray(),
                        EventsToAddToSource = Array.Empty<SessionDataEvent>()
                    };
                }
            }

            // Snapshot both sides under their respective locks (no deadlock risk:
            // we always lock source first, then target — consistent ordering).
            HashSet<CompactEventId> sourceIds;
            List<SessionDataEvent> sourceEvents;
            lock (_eventLogLock)
            {
                sourceEvents = _eventLog.Select(e => e.Event).ToList();
                sourceIds = new HashSet<CompactEventId>(_knownEventIds);
            }

            HashSet<CompactEventId> targetIds;
            List<SessionDataEvent> targetEvents;
            lock (other._eventLogLock)
            {
                targetEvents = other._eventLog.Select(e => e.Event).ToList();
                targetIds = new HashSet<CompactEventId>(other._knownEventIds);
            }

            // Set difference in each direction
            var toAddToTarget = sourceEvents.Where(e => !targetIds.Contains(e.EventId)).ToArray();
            var toAddToSource = targetEvents.Where(e => !sourceIds.Contains(e.EventId)).ToArray();

            return new SessionSyncDelta
            {
                EventsToAddToTarget = toAddToTarget,
                EventsToAddToSource = toAddToSource
            };
        }

        /// <summary>
        /// Applies a collection of events from a remote source (e.g., from a <see cref="SessionSyncDelta"/>).
        /// Each event is processed through <see cref="AddEvent"/>, going through the full pipeline:
        /// deduplication → device aggregation → event log append.
        /// 
        /// <para><b>Idempotent:</b> Safe to call with overlapping event sets — duplicates are
        /// silently skipped.</para>
        /// </summary>
        public void ApplyRemoteEvents(IEnumerable<SessionDataEvent> events)
        {
            if (events == null) return;
            foreach (var e in events)
            {
                AddEvent(e);
            }
        }
    }
}
