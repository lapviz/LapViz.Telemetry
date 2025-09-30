using System.ComponentModel;
using System.Diagnostics;

namespace LapViz.LiveTiming.Models.Views;

/// <summary>Hold convenient historical object model of live timing data.</summary>
public class LiveTimingDataView : INotifyPropertyChanged
{
    public IList<LiveTimingDataDeviceView> Devices { get; set; } = new List<LiveTimingDataDeviceView>();

    public string SessionId { get; set; }

    private DateTime _lastEventOfSessionTimeStamp;
    private readonly object _addEventLock = new object();

    // Ensures at least one complete rebuild after the first fill, useful when joining during a session
    private bool _everRebuilt;

    /// <summary>Adds the device events.</summary>
    /// <param name="sessionDataDeviceDto">The session data device dto.</param>
    /// <param name="skipStateCalculation">if set to <c>true</c> [skip state calculation].</param>
    /// <returns><c>true</c> if statistics has been rebuilt, <c>false</c> otherwise.</returns>
    public LiveTimingDataViewAddEventsResults AddDeviceEvents(SessionDataDeviceDto sessionDataDeviceDto, bool skipStateCalculation)
    {
        var stopWatch = Stopwatch.StartNew();

        lock (_addEventLock)
        {
            // Create device on first encounter
            var deviceView = Devices.FirstOrDefault(x => x.Id == sessionDataDeviceDto.DeviceId);
            if (deviceView == null)
            {
                deviceView = new LiveTimingDataDeviceView
                {
                    Id = sessionDataDeviceDto.DeviceId,
                    Type = sessionDataDeviceDto.Type.ToString(),
                    LiveTimingData = this,
                    Info = new LiveTimingDataDeviceInfoView()
                };
                deviceView.Info.DisplayName = sessionDataDeviceDto.DisplayName;
                deviceView.Info.Category = sessionDataDeviceDto.Category;
                Devices.Add(deviceView);
            }
            else
            {
                // keep Info up to date if names/categories change during the session
                if (!string.IsNullOrWhiteSpace(sessionDataDeviceDto.DisplayName))
                    deviceView.Info.DisplayName = sessionDataDeviceDto.DisplayName;
                if (!string.IsNullOrWhiteSpace(sessionDataDeviceDto.Category))
                    deviceView.Info.Category = sessionDataDeviceDto.Category;
            }

            bool shouldRebuild = false;

            foreach (var deviceEventDto in sessionDataDeviceDto.Events)
            {
                LiveTimingDataDeviceEventView deviceEventView;

                // Soft delete of an existing event
                if (deviceEventDto.Deleted.HasValue && deviceView.Events.Any(x => x.Id == deviceEventDto.Id))
                {
                    deviceEventView = deviceView.Events.First(e => e.Id == deviceEventDto.Id);
                    if (deviceEventView.Deleted == null)
                    {
                        deviceEventView.Deleted = deviceEventDto.Deleted;
                        shouldRebuild = true; // removal of bests and meters
                    }
                    continue;
                }

                // Nieuw evenement
                deviceEventView = new LiveTimingDataDeviceEventView
                {
                    Id = deviceEventDto.Id,
                    Device = deviceView,
                    LiveTimingData = this,
                    Type = deviceEventDto.Type == SessionEventTypeDto.Lap ? LiveTimingDataDeviceEventType.Lap : LiveTimingDataDeviceEventType.Sector,
                    Time = deviceEventDto.Time,
                    Timestamp = deviceEventDto.Timestamp,
                    Lap = deviceEventDto.LapNumber,
                    Sector = deviceEventDto.SectorNumber
                };

                deviceView.Events.Add(deviceEventView);

                // Live updates on request
                if (!skipStateCalculation && deviceEventView.Time != TimeSpan.Zero)
                    UpdateStatistics(deviceEventView);

                // If we receive an event with a timestamp earlier than the last known, our derivatives are potentially false.
                if (deviceEventView.Timestamp.DateTime < _lastEventOfSessionTimeStamp)
                    shouldRebuild = true;

                if (deviceView.LastEvent == null || deviceView.LastEvent.Timestamp < deviceEventView.Timestamp)
                    deviceView.LastEvent = deviceEventView;

                if (deviceEventView.Type == LiveTimingDataDeviceEventType.Lap)
                {
                    if (deviceView.LastLap == null || deviceView.LastLap.Timestamp < deviceEventView.Timestamp)
                        deviceView.LastLap = deviceEventView;
                }

                // Màj borne max
                if (deviceEventView.Timestamp.DateTime > _lastEventOfSessionTimeStamp)
                    _lastEventOfSessionTimeStamp = deviceEventView.Timestamp.DateTime;
            }

            // Case 1: if a rebuild has been signaled by the
            // Case 2: if you've never rebuilt and already have valid events, you force a rebuild.
            if (shouldRebuild || !_everRebuilt)
            {
                // Force a complete rebuild to ensure consistency when rejoining in progress
                RebuildStatistics();
                stopWatch.Stop();
                Updated = DateTime.UtcNow;
                return new LiveTimingDataViewAddEventsResults { Duration = stopWatch.Elapsed, StatisticsRebuilt = true };
            }

            Updated = DateTime.UtcNow;
            stopWatch.Stop();
            return new LiveTimingDataViewAddEventsResults { Duration = stopWatch.Elapsed, StatisticsRebuilt = false };
        }
    }

    /// <summary>Marks a device as deleted and rebuilds statistics.</summary>
    public void MarkDeviceDeleted(LiveTimingDataDeviceView device)
    {
        lock (_addEventLock)
        {
            if (device == null || device.Info == null) return;
            if (device.Info.Deleted == null)
            {
                device.Info.Deleted = DateTime.UtcNow;

                // If all devices are deleted, complete purge of derived state
                if (!Devices.Any(d => d.Info != null && d.Info.Deleted == null))
                {
                    ResetDerivedState();
                    Updated = DateTime.UtcNow;
                    return;
                }

                RebuildStatistics();
                Updated = DateTime.UtcNow;
            }
        }
    }

    public void UpdateStatistics(LiveTimingDataDeviceEventView deviceEvent)
    {
        if (deviceEvent == null) return;

        // ignorer soft-deleted, zero-time, device supprimé
        if (deviceEvent.Deleted != null) return;
        if (deviceEvent.Device != null && deviceEvent.Device.Info != null && deviceEvent.Device.Info.Deleted != null) return;
        if (deviceEvent.Time == TimeSpan.Zero) return;

        if (deviceEvent.Type == LiveTimingDataDeviceEventType.Lap)
        {
            // Personal best
            if (deviceEvent.Device.BestLap == null
                || deviceEvent.Device.BestLap.Deleted != null
                || deviceEvent.Device.BestLap.Time >= deviceEvent.Time)
            {
                deviceEvent.Device.BestLap = deviceEvent;
                deviceEvent.WasPersonalBest = true;
            }
            else
            {
                deviceEvent.WasPersonalBest = false;
            }

            // Best overall
            if (this.BestLap == null
                || this.BestLap.Deleted != null
                || (this.BestLap.Device != null && this.BestLap.Device.Info != null && this.BestLap.Device.Info.Deleted != null)
                || this.BestLap.Time >= deviceEvent.Time)
            {
                this.BestLap = deviceEvent;
                deviceEvent.WasBestOverall = true;
            }
            else
            {
                deviceEvent.WasBestOverall = false;
            }
        }
        else if (deviceEvent.Type == LiveTimingDataDeviceEventType.Sector)
        {
            // PB sector
            LiveTimingDataDeviceEventView prev;
            if (!deviceEvent.Device.BestSectors.TryGetValue(deviceEvent.Sector, out prev)
                || prev == null
                || prev.Deleted != null
                || prev.Time >= deviceEvent.Time)
            {
                deviceEvent.Device.BestSectors[deviceEvent.Sector] = deviceEvent;
                deviceEvent.WasPersonalBest = true;
            }
            else
            {
                deviceEvent.WasPersonalBest = false;
            }

            // Best overall sector
            LiveTimingDataDeviceEventView prevOverall;
            if (!this.BestSectors.TryGetValue(deviceEvent.Sector, out prevOverall)
                || prevOverall == null
                || prevOverall.Deleted != null
                || (prevOverall.Device != null && prevOverall.Device.Info != null && prevOverall.Device.Info.Deleted != null)
                || prevOverall.Time >= deviceEvent.Time)
            {
                this.BestSectors[deviceEvent.Sector] = deviceEvent;
                deviceEvent.WasBestOverall = true;
            }
            else
            {
                deviceEvent.WasBestOverall = false;
            }
        }
    }

    public void RebuildStatistics()
    {
        // Herbouw compleet en idempotent
        var stopwatch = Stopwatch.StartNew();

        // Active devices only
        var validDevices = Devices
            .Where(d => d.Info == null || d.Info.Deleted == null)
            .ToList();

        // If no active device, total purging of derived state
        if (validDevices.Count == 0)
        {
            ResetDerivedState();
            _everRebuilt = true; // even if empty, "rebuilt" is considered
            stopwatch.Stop();
            return;
        }

        // Smoothing out valuable events
        var sortedEvents = validDevices
            .SelectMany(d => d.Events ?? Enumerable.Empty<LiveTimingDataDeviceEventView>())
            .Where(e => e != null && e.Deleted == null && e.Time != TimeSpan.Zero)
            .OrderBy(e => e.Timestamp)
            .ToList();

        // Reset of the global best
        this.BestSectors = new Dictionary<int, LiveTimingDataDeviceEventView>();
        this.BestLap = null;

        // Reset by device
        foreach (var device in Devices)
        {
            device.BestLap = null;
            device.BestSectors.Clear();
            device.Events = device.Events ?? new List<LiveTimingDataDeviceEventView>();
            device.LastEvent = null;
            device.LastLap = null;
        }

        // Chronological proofreading
        foreach (var deviceEvent in sortedEvents)
        {
            UpdateStatistics(deviceEvent);

            var dev = deviceEvent.Device;
            if (dev != null)
            {
                if (dev.LastEvent == null || dev.LastEvent.Timestamp < deviceEvent.Timestamp)
                    dev.LastEvent = deviceEvent;

                if (deviceEvent.Type == LiveTimingDataDeviceEventType.Lap)
                {
                    if (dev.LastLap == null || dev.LastLap.Timestamp < deviceEvent.Timestamp)
                        dev.LastLap = deviceEvent;
                }
            }
        }

        _everRebuilt = true;
        stopwatch.Stop();
    }

    private void ResetDerivedState()
    {
        this.BestLap = null;
        if (this.BestSectors == null)
            this.BestSectors = new Dictionary<int, LiveTimingDataDeviceEventView>();
        else
            this.BestSectors.Clear();

        foreach (var d in Devices)
        {
            d.BestLap = null;
            d.BestSectors.Clear();

            d.LastLap = null;
            d.LastEvent = null;
        }
    }

    public LiveTimingDataRankingTableView GetRanking(LiveTimingDataRankingType type, LiveTimingDataRankingTableView previousRanking = null)
    {
        lock (_addEventLock)
        {
            var stopwatch = Stopwatch.StartNew();

            var newRankingTable = new LiveTimingDataRankingTableView();
            newRankingTable.Sectors = this.Sectors.HasValue ? this.Sectors : (this.BestSectors.Keys.Any() ? this.BestSectors.Keys.Max() : 3);

            // dict with FULL id key, to compare with previous ranking
            IDictionary<string, LiveTimingDataRankingRowView> dict = previousRanking != null
                ? previousRanking.Rows.ToDictionary(x => x.DeviceId)
                : null;

            // active devices only
            var activeDevices = Devices
                .Where(d => d.Info != null && d.Info.Deleted == null)
                .ToList();

            LiveTimingDataDeviceEventView BestNonDeletedLap(LiveTimingDataDeviceView d)
            {
                return d.Events
                    .Where(e => e.Type == LiveTimingDataDeviceEventType.Lap
                                && e.Deleted == null
                                && e.Time > TimeSpan.Zero)
                    .OrderBy(e => e.Time)
                    .FirstOrDefault();
            }

            var globalBestLap = activeDevices
                .SelectMany(d => d.Events)
                .Where(e => e.Type == LiveTimingDataDeviceEventType.Lap
                            && e.Deleted == null
                            && e.Time > TimeSpan.Zero)
                .OrderBy(e => e.Time)
                .FirstOrDefault();

            var devicesOrdered = activeDevices
                .Select(d => new { Device = d, BestLap = BestNonDeletedLap(d) })
                .OrderBy(x => x.BestLap == null ? TimeSpan.MaxValue : x.BestLap.Time)
                .ToList();

            int index = 1;
            LiveTimingDataDeviceView previous = null;

            foreach (var item in devicesOrdered)
            {
                var device = item.Device;
                var bestLap = item.BestLap;

                var row = new LiveTimingDataRankingRowView
                {
                    Rank = index,
                    DisplayName = device.Info != null && !string.IsNullOrWhiteSpace(device.Info.DisplayName) ? device.Info.DisplayName : (device.Id.Length > 8 ? device.Id.Substring(0, 8) : device.Id),
                    DeviceId = device.Id,
                    DeviceShortId = device.Id.Length > 8 ? device.Id.Substring(0, 8) : device.Id
                };

                var lastLapEvent = device.Events
                    .Where(e => e.Type == LiveTimingDataDeviceEventType.Lap && e.Deleted == null)
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefault();
                row.Laps = lastLapEvent != null ? lastLapEvent.Lap.ToString() : "";

                // Total number of sectors to display for the table
                var totalSectors = newRankingTable.Sectors ?? 3;

                // Reminder of the display rule:
                // 1) If no sector of the current round has yet been passed,
                // the sectors of the previous round are displayed, complete.
                // 2) As soon as a sector of the current round has been passed,
                // this/these sector(s) is/are displayed, leaving the following sectors empty.

                var lastCompletedLap = device.GetLastCompletedLapNumber();
                var currentLapSectors = device.CurrentLapSectors; // now for the current tour
                var hasCurrent = currentLapSectors != null && currentLapSectors.Count > 0;

                // If no sectors in current round, fallback to previous round
                var previousLapSectors = hasCurrent
                    ? null
                    : device.GetLapSectors(lastCompletedLap);

                // Fill 1..N sectors
                for (int s = 1; s <= totalSectors; s++)
                {
                    LiveTimingDataDeviceEventView sectorEvent = null;

                    if (hasCurrent)
                    {
                        // display sectors already passed in the current round, otherwise empty
                        currentLapSectors.TryGetValue(s, out sectorEvent);
                    }
                    else
                    {
                        // no sectors in current tour, display entire previous tour
                        previousLapSectors?.TryGetValue(s, out sectorEvent);
                    }

                    var sectorData = new ColoredDeviceEventView(sectorEvent);
                    if (row.Sectors.ContainsKey(s))
                        row.Sectors[s] = sectorData;
                    else
                        row.Sectors.Add(s, sectorData);
                }

                var lastNonDeletedLap = device.Events
                    .Where(e => e.Type == LiveTimingDataDeviceEventType.Lap && e.Deleted == null)
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefault();

                row.LastLap = new ColoredDeviceEventView(lastNonDeletedLap);
                row.BestLap = new ColoredDeviceEventView(bestLap);

                if (globalBestLap != null && bestLap != null)
                    row.Gap = bestLap.Time - globalBestLap.Time;

                if (previous != null)
                {
                    var prevBestLap = BestNonDeletedLap(previous);
                    if (bestLap != null && prevBestLap != null)
                        row.Interval = bestLap.Time - prevBestLap.Time;
                }

                if (dict != null)
                {
                    LiveTimingDataRankingRowView prevRow;
                    if (dict.TryGetValue(device.Id, out prevRow))
                    {
                        row.PreviousRank = prevRow.Rank;
                        row.HasChanged = prevRow.Rank != row.Rank;
                    }
                    else
                    {
                        row.PreviousRank = row.Rank;
                    }
                }
                else
                {
                    row.PreviousRank = row.Rank;
                }

                newRankingTable.Rows.Add(row);

                previous = device;
                index++;
            }

            stopwatch.Stop();
            newRankingTable.Duration = stopwatch.ElapsedMilliseconds;
            return newRankingTable;
        }
    }

    public LiveTimingDataDeviceEventView BestLap { get; private set; }
    public IDictionary<int, LiveTimingDataDeviceEventView> BestSectors { get; set; } = new Dictionary<int, LiveTimingDataDeviceEventView>();

    private DateTime _updated;
    public DateTime Updated
    {
        get { return _updated; }
        set
        {
            _updated = value;
            OnPropertyChanged("Updated");
        }
    }

    public int? Sectors { get; internal set; }
    public bool IsPrivate { get; internal set; }
    public string Description { get; internal set; }
    public string CircuitCode { get; internal set; }
    public long Duration { get; internal set; }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        var handler = PropertyChanged;
        if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
    }
    public event PropertyChangedEventHandler PropertyChanged;
}
