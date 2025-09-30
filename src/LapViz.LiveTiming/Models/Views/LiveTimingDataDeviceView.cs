using System.Text.Json.Serialization;

namespace LapViz.LiveTiming.Models.Views;

public class LiveTimingDataDeviceView
{
    public string Id { get; set; }
    public string Type { get; internal set; }
    [JsonIgnore]
    public LiveTimingDataView LiveTimingData { get; set; }
    public LiveTimingDataDeviceInfoView Info { get; set; } = new LiveTimingDataDeviceInfoView() { DisplayName = "NotSet" };
    public IList<LiveTimingDataDeviceEventView> Events { get; set; } = new List<LiveTimingDataDeviceEventView>();
    public LiveTimingDataDeviceEventView BestLap { get; set; }
    public IDictionary<int, LiveTimingDataDeviceEventView> BestSectors { get; private set; } = new Dictionary<int, LiveTimingDataDeviceEventView>();
    public LiveTimingDataDeviceEventView LastEvent { get; internal set; }
    public LiveTimingDataDeviceEventView LastLap { get; internal set; }
    public IDictionary<int, LiveTimingDataDeviceEventView> CurrentLapSectors
    {
        get
        {
            // Current round = last full round + 1
            var currentLap = GetLastCompletedLapNumber() + 1;

            var dict = new Dictionary<int, LiveTimingDataDeviceEventView>();
            foreach (var e in Events
                .Where(x => x.Type == LiveTimingDataDeviceEventType.Sector
                            && x.Lap == currentLap
                            && x.Deleted == null
                            && x.Time > TimeSpan.Zero)
                .OrderBy(x => x.Timestamp))
            {
                dict[e.Sector] = e;
            }
            return dict;
        }
    }

    public int GetLastCompletedLapNumber()
    {
        var lastLap = Events
            .Where(e => e.Type == LiveTimingDataDeviceEventType.Lap
                        && e.Deleted == null
                        && e.Time > TimeSpan.Zero)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        return lastLap?.Lap ?? 0;
    }

    public IDictionary<int, LiveTimingDataDeviceEventView> GetLapSectors(int lapNumber)
    {
        var dict = new Dictionary<int, LiveTimingDataDeviceEventView>();
        if (lapNumber <= 0) return dict;

        foreach (var e in Events
            .Where(x => x.Type == LiveTimingDataDeviceEventType.Sector
                        && x.Lap == lapNumber
                        && x.Deleted == null
                        && x.Time > TimeSpan.Zero)
            .OrderBy(x => x.Timestamp))
        {
            dict[e.Sector] = e; // dernier event pour un secteur ecrase l’ancien
        }
        return dict;
    }

}
