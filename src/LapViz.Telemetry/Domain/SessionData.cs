using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LapViz.Telemetry.Domain
{
    /// <summary>
    /// Aggregates telemetry session data across multiple devices. This class is thread-safe.
    /// </summary>
    public class SessionData
    {
        // Using a separate dictionary of locks avoids locking the whole collection.
        private readonly ConcurrentDictionary<string, object> _deviceLocks =
            new ConcurrentDictionary<string, object>();

        /// <summary>
        /// Unique identifier for the session.
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// All device sessions participating in this session, keyed by device ID.
        /// Thread-safe for concurrent reads and writes.
        /// </summary>
        public ConcurrentDictionary<string, DeviceSessionData> Devices { get; }

        public SessionData()
        {
            Devices = new ConcurrentDictionary<string, DeviceSessionData>();
        }

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

        /// <summary>
        /// Adds a timing event (lap or sector) to the session.
        /// Creates the per-device container on first use.
        /// </summary>
        public void AddEvent(SessionDataEvent sessionDataEvent)
        {
            if (sessionDataEvent == null) return;

            // Only sector and lap events are relevant for session aggregation
            if (sessionDataEvent.Type != SessionEventType.Sector &&
                sessionDataEvent.Type != SessionEventType.Lap)
            {
                return;
            }

            // Ensure the device is present or create it atomically
            var device = Devices.GetOrAdd(
                sessionDataEvent.DeviceId,
                _ => new DeviceSessionData()
            );

            // Get the per-device lock and synchronize the mutation of DeviceSessionData
            var devLock = _deviceLocks.GetOrAdd(sessionDataEvent.DeviceId, _ => new object());

            lock (devLock)
            {
                // Inside this lock it is safe to mutate the DeviceSessionData instance
                device.AddEvent(sessionDataEvent);
            }
        }

        /// <summary>
        /// Gets the DeviceSessionData for a device, creating it if missing, thread-safe.
        /// </summary>
        public DeviceSessionData GetOrCreateDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("deviceId must be non empty.", nameof(deviceId));

            return Devices.GetOrAdd(deviceId, _ => new DeviceSessionData());
        }

        /// <summary>
        /// Executes a thread-safe action against a specific device session.
        /// Creates the device if it does not exist yet.
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
    }
}
