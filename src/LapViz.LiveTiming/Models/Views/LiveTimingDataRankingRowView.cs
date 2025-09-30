namespace LapViz.LiveTiming.Models.Views;

public class LiveTimingDataRankingRowView : IEquatable<LiveTimingDataRankingRowView>
{
    public int Rank { get; internal set; }
    public int? PreviousRank { get; internal set; }
    public string DeviceId { get; internal set; }
    public string DeviceShortId { get; internal set; }
    public string DisplayName { get; internal set; }
    public string Laps { get; internal set; }
    public ColoredDeviceEventView LastLap { get; internal set; }
    public ColoredDeviceEventView BestLap { get; internal set; }
    public IDictionary<int, ColoredDeviceEventView> Sectors { get; internal set; } = new Dictionary<int, ColoredDeviceEventView>();
    public bool HasChanged { get; internal set; }
    public int? RankChange
    {
        get
        {
            if (!PreviousRank.HasValue)
                return null;

            return PreviousRank.Value - Rank;
        }
    }

    public TimeSpan? Gap { get; internal set; }
    public TimeSpan? Interval { get; internal set; }
    public LiveTimingDataRankingTableView Table { get; internal set; }

    public bool Equals(LiveTimingDataRankingRowView other)
    {
        if (other == null) return false;

        if (DeviceId != other.DeviceId ||
            DisplayName != other.DisplayName ||
            Laps != other.Laps ||
            !Equals(LastLap, other.LastLap) ||
            !Equals(BestLap, other.BestLap))
            return false;

        if ((Sectors == null) != (other.Sectors == null))
            return false;
        if (Sectors != null)
        {
            if (Sectors.Count != other.Sectors.Count)
                return false;

            foreach (var kvp in Sectors)
            {
                ColoredDeviceEventView otherSector;
                if (!other.Sectors.TryGetValue(kvp.Key, out otherSector))
                    return false;
                if (!Equals(kvp.Value, otherSector))
                    return false;
            }
        }

        return true;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as LiveTimingDataRankingRowView);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (DeviceId?.GetHashCode() ?? 0);
            hash = hash * 23 + (DisplayName?.GetHashCode() ?? 0);
            hash = hash * 23 + (Laps?.GetHashCode() ?? 0);
            hash = hash * 23 + (LastLap?.GetHashCode() ?? 0);
            hash = hash * 23 + (BestLap?.GetHashCode() ?? 0);

            if (Sectors != null)
            {
                foreach (var kvp in Sectors.OrderBy(k => k.Key))
                {
                    hash = hash * 23 + kvp.Key.GetHashCode();
                    hash = hash * 23 + (kvp.Value?.GetHashCode() ?? 0);
                }
            }

            return hash;
        }
    }

}
