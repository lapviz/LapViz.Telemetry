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

        if (!Views.TryGetValue(sessionDeviceDataEvent.SessionId, out var view))
        {
            view = new LiveTimingDataView { SessionId = sessionDeviceDataEvent.SessionId };
            Views[sessionDeviceDataEvent.SessionId] = view;
        }

        view.AddDeviceEvents(sessionDeviceDataEvent, false);

        base.OnSessionDeviceDataReceived(sessionDeviceDataEvent);
    }

    protected override void OnDeviceInfoUpdated(DeviceInfoDto e)
    {
        if (!Views.TryGetValue(e.SessionId, out var view))
        {
            view = new LiveTimingDataView { SessionId = e.SessionId };
            Views[e.SessionId] = view;
        }

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

    private SessionDataDeviceDto _lastMessage;
    public SessionDataDeviceDto LastMessage => _lastMessage;

    public Dictionary<string, LiveTimingDataView> Views { get; } = new Dictionary<string, LiveTimingDataView>();

    public TimeSpan? GetBestLap(string sessionId)
    {
        return Views.TryGetValue(sessionId, out var view) ? view.BestLap?.Time : null;
    }

    public TimeSpan? GetBestSector(string sessionId, int sector)
    {
        if (!Views.TryGetValue(sessionId, out var view)) return null;
        if (view.BestSectors == null || !view.BestSectors.TryGetValue(sector, out var best)) return null;
        return best.Time;
    }

    public override async Task JoinSession(string sessionId, string? password)
    {
        Views.TryAdd(sessionId, new LiveTimingDataView { SessionId = sessionId });

        await base.JoinSession(sessionId, password);
    }

    public override async Task LeaveSession(string sessionId)
    {
        await base.LeaveSession(sessionId);

        Views.Remove(sessionId);
    }
}
