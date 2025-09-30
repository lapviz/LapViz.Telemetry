using System.Diagnostics;
using LapViz.LiveTiming.Models.Views;

namespace LapViz.LiveTiming.Models;

public static class Extensions
{
    public static LiveTimingDataView ToLiveTimingView(this SessionDataDto sessionData)
    {
        var stopwatch = Stopwatch.StartNew();
        var liveTimingData = new LiveTimingDataView() { SessionId = sessionData.Id };
        liveTimingData.Sectors = sessionData.Sectors;
        liveTimingData.IsPrivate = sessionData.IsPrivate;
        liveTimingData.Description = sessionData.Description;
        liveTimingData.CircuitCode = sessionData.CircuitCode;
        liveTimingData.Updated = sessionData.LastUpdated.DateTime;

        foreach (var deviceDto in sessionData.Devices)
        {
            liveTimingData.AddDeviceEvents(deviceDto, true);
        }

        liveTimingData.RebuildStatistics();

        liveTimingData.Duration = stopwatch.ElapsedMilliseconds;
        return liveTimingData;
    }
}
