namespace LapViz.LiveTiming.Models.Views;
public class LiveTimingDataRankingTableView
{
    public IList<LiveTimingDataRankingRowView> Rows { get; private set; } = new List<LiveTimingDataRankingRowView>();
    public long Duration { get; set; }
    public int? Sectors { get; internal set; }
}
