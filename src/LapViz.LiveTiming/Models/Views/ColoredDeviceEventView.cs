using System.Drawing;

namespace LapViz.LiveTiming.Models.Views;

public class ColoredDeviceEventView : IEquatable<ColoredDeviceEventView>
{
    public ColoredDeviceEventView(LiveTimingDataDeviceEventView deviceEvent)
    {
        Event = deviceEvent;
    }

    public LiveTimingDataDeviceEventView Event { get; }
    public Color Color
    {
        get
        {
            if (Event == null)
                return Color.Black;

            if (Event.IsBestOverall)
                return Color.Purple;

            if (Event.WasPersonalBest)
                return Color.Green;

            return Color.Goldenrod;
        }
    }

    public override string ToString()
    {
        return Event != null && Event.Time > TimeSpan.Zero
            ? Event.Time.ToString("m\\:ss\\.fff")
            : string.Empty;
    }

    public bool Equals(ColoredDeviceEventView other)
    {
        if (other == null)
            return false;

        return EventEquals(this.Event, other.Event);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as ColoredDeviceEventView);
    }

    public override int GetHashCode()
    {
        return Event?.GetHashCode() ?? 0;
    }

    private bool EventEquals(LiveTimingDataDeviceEventView a, LiveTimingDataDeviceEventView b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;

        return a.Time == b.Time &&
               a.Lap == b.Lap &&
               a.Sector == b.Sector &&
               a.IsBestOverall == b.IsBestOverall &&
               a.IsPersonalBest == b.IsPersonalBest &&
               a.Type == b.Type &&
               a.Timestamp == b.Timestamp;
    }
}
