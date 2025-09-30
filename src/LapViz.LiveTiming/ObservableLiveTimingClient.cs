using LapViz.LiveTiming.Models;
using LapViz.LiveTiming.Models.Views;
using Microsoft.Extensions.Logging;

namespace LapViz.LiveTiming;

public class ObservableLiveTimingClient : LiveTimingClient
{
    private readonly ILogger<ObservableLiveTimingClient> _logger;

    public ObservableLiveTimingClient(ILogger<ObservableLiveTimingClient> logger) : base(logger)
    {
        _logger = logger;
    }

    protected override void OnSessionDeviceDataReceived(SessionDataDeviceDto sessionDeviceDataEvent)
    {
        _lastMessage = sessionDeviceDataEvent;

        if (!Views.ContainsKey(sessionDeviceDataEvent.SessionId))
        {
            Views[sessionDeviceDataEvent.SessionId] = new LiveTimingDataView { SessionId = sessionDeviceDataEvent.SessionId };
        }

        Views[sessionDeviceDataEvent.SessionId].AddDeviceEvents(sessionDeviceDataEvent, false);

        base.OnSessionDeviceDataReceived(sessionDeviceDataEvent);
    }

    protected override void OnDeviceInfoUpdated(DeviceInfoDto e)
    {
        if (!Views.ContainsKey(e.SessionId))
        {
            Views[e.SessionId] = new LiveTimingDataView { SessionId = e.SessionId };
        }

        var view = Views[e.SessionId];
        var device = view.Devices.SingleOrDefault(x => x.Id == e.DeviceId);

        if (device == null)
        {
            device = new LiveTimingDataDeviceView
            {
                Id = e.DeviceId
            };
            view.Devices.Add(device);
        }

        // Securise Info container
        if (device.Info == null) device.Info = new LiveTimingDataDeviceInfoView();

        device.Info.DisplayName = e.DisplayName;
        device.Info.Category = e.Category;
        device.Info.Deleted = e.Deleted;

        base.OnDeviceInfoUpdated(e);
    }

    protected override void OnBoardUpdated(SessionDataDto e)
    {
        Views[e.Id] = e.ToLiveTimingView();
        base.OnBoardUpdated(e);
    }

    private SessionDataDeviceDto _lastMessage = null;
    public SessionDataDeviceDto LastMessage => _lastMessage;

    public Dictionary<string, LiveTimingDataView> Views { get; } = new Dictionary<string, LiveTimingDataView>();

    public TimeSpan? GetBestLap(string sessionId)
    {
        if (!Views.ContainsKey(sessionId)) return null;
        return Views[sessionId].BestLap?.Time;
    }

    public TimeSpan? GetBestSector(string sessionId, int sector)
    {
        if (!Views.ContainsKey(sessionId)) return null;
        var view = Views[sessionId];
        if (view.BestSectors == null || !view.BestSectors.ContainsKey(sector)) return null;
        return view.BestSectors[sector].Time;
    }

    public override async Task JoinSession(string sessionId, string password)
    {
        if (!Views.ContainsKey(sessionId))
            Views.Add(sessionId, new LiveTimingDataView { SessionId = sessionId });

        await base.JoinSession(sessionId, password);
    }

    public override async Task LeaveSession(string sessionId)
    {
        await base.LeaveSession(sessionId);

        if (Views.ContainsKey(sessionId))
            Views.Remove(sessionId);
    }
}
