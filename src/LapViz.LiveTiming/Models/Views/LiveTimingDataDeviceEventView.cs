using System.Text.Json.Serialization;

namespace LapViz.LiveTiming.Models.Views;

public class LiveTimingDataDeviceEventView
{
    [JsonIgnore]
    public LiveTimingDataDeviceView Device { get; internal set; }
    [JsonIgnore]
    public LiveTimingDataView LiveTimingData { get; internal set; }
    public LiveTimingDataDeviceEventType Type { get; internal set; }
    public TimeSpan Time { get; internal set; }
    public DateTimeOffset Timestamp { get; internal set; }
    public int Lap { get; internal set; }
    public int Sector { get; internal set; }
    public bool WasPersonalBest { get; set; }
    public bool WasBestOverall { get; set; }
    public bool IsPersonalBest
    {
        get
        {
            if (Time == TimeSpan.Zero)
            {
                return false;
            }

            if (Type == LiveTimingDataDeviceEventType.Lap)
            {
                return Time <= Device.BestLap.Time;
            }

            return Time <= Device.BestSectors[Sector].Time;
        }
    }

    public bool IsBestOverall
    {
        get
        {
            if (Time == TimeSpan.Zero)
                return false;

            if (Type == LiveTimingDataDeviceEventType.Lap)
            {
                if (LiveTimingData.BestLap == null)
                {
                    return true;
                }
                else
                {
                    return Time <= LiveTimingData.BestLap.Time;
                }
            }

            if (!LiveTimingData.BestSectors.ContainsKey(Sector))
                return true;

            return Time <= LiveTimingData.BestSectors[Sector].Time;
        }
    }

    public string Id { get; internal set; }
    public DateTime? Deleted { get; internal set; }
}
