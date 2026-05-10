using LapViz.LiveTiming.Models;
using LapViz.LiveTiming.Models.Views;
using LapViz.Telemetry.Domain;

namespace LapViz.LiveTiming;

/// <summary>
/// Bridges <see cref="RaceSessionState"/> (OpenLiveTiming domain model) into a
/// <see cref="LiveTimingDataView"/> that the LapViz analysis tools can consume.
/// <para>
/// Call <see cref="Update"/> each time the race state changes. The adapter
/// detects new lap and sector completions by comparing the current state
/// against a snapshot of the previous state and emits the corresponding
/// <see cref="SessionDeviceEventDto"/> events into the view.
/// </para>
/// </summary>
public class RaceSessionAdapter
{
    private readonly LiveTimingDataView _view;
    private readonly Dictionary<string, EntrySnapshot> _snapshots = new();

    /// <summary>
    /// The <see cref="LiveTimingDataView"/> being populated.
    /// </summary>
    public LiveTimingDataView View => _view;

    public RaceSessionAdapter(string? sessionId = null)
    {
        _view = new LiveTimingDataView
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// Updates the view with the latest <see cref="RaceSessionState"/>.
    /// New lap/sector events are detected by comparing against the previous snapshot.
    /// </summary>
    public void Update(RaceSessionState state)
    {
        foreach (var kvp in state.Entries)
        {
            var key = kvp.Key;
            var entry = kvp.Value;
            var prev = _snapshots.GetValueOrDefault(key);

            var dto = new SessionDataDeviceDto
            {
                DeviceId = key,
                DisplayName = entry.Driver?.Name ?? key,
                Category = "",
                SessionId = _view.SessionId,
                Type = DeviceTypeDto.Other,
                Events = new List<SessionDeviceEventDto>()
            };

            var sectorCount = state.SectorCount > 0 ? state.SectorCount : 3;

            // Detect new sector completions
            for (int i = 0; i < sectorCount; i++)
            {
                if (entry.SectionTimes == null || entry.SectionTimes.Length <= i)
                    continue;

                var sectorTime = entry.SectionTimes[i];
                if (sectorTime.HasValue && sectorTime.Value > TimeSpan.Zero)
                {
                    var prevSector = prev?.SectionTimes != null && prev.SectionTimes.Length > i
                        ? prev.SectionTimes[i]
                        : null;

                    if (prevSector != sectorTime)
                    {
                        dto.Events.Add(new SessionDeviceEventDto
                        {
                            Id = $"{key}_L{entry.Laps}_S{i + 1}_{sectorTime.Value.TotalMilliseconds:F0}",
                            DeviceId = key,
                            SessionId = _view.SessionId,
                            Timestamp = DateTimeOffset.UtcNow,
                            Time = sectorTime.Value,
                            Type = SessionEventTypeDto.Sector,
                            LapNumber = entry.Laps > 0 ? entry.Laps : 1,
                            SectorNumber = i + 1
                        });
                    }
                }
            }

            // Detect new lap completion
            if (entry.LastTime.HasValue && entry.LastTime.Value > TimeSpan.Zero)
            {
                if (prev == null || prev.LastTime != entry.LastTime || prev.Laps != entry.Laps)
                {
                    var lapNumber = entry.Laps > 0 ? entry.Laps : 1;
                    dto.Events.Add(new SessionDeviceEventDto
                    {
                        Id = $"{key}_L{lapNumber}_{entry.LastTime.Value.TotalMilliseconds:F0}",
                        DeviceId = key,
                        SessionId = _view.SessionId,
                        Timestamp = DateTimeOffset.UtcNow,
                        Time = entry.LastTime.Value,
                        Type = SessionEventTypeDto.Lap,
                        LapNumber = lapNumber,
                        SectorNumber = 0
                    });
                }
            }

            // Feed events into the view
            if (dto.Events.Count > 0)
            {
                _view.AddDeviceEvents(dto, skipStateCalculation: false);
            }

            // Update snapshot
            _snapshots[key] = new EntrySnapshot
            {
                Laps = entry.Laps,
                LastTime = entry.LastTime,
                SectionTimes = entry.SectionTimes?.ToArray() ?? Array.Empty<TimeSpan?>()
            };
        }
    }

    /// <summary>
    /// Resets the adapter for a new session.
    /// </summary>
    public void Reset(string? sessionId = null)
    {
        _snapshots.Clear();
        _view.Devices.Clear();
        _view.SessionId = sessionId ?? Guid.NewGuid().ToString("N");
    }

    private class EntrySnapshot
    {
        public int Laps { get; set; }
        public TimeSpan? LastTime { get; set; }
        public TimeSpan?[] SectionTimes { get; set; } = Array.Empty<TimeSpan?>();
    }
}
