namespace LapViz.LiveTiming.Models;

public class SessionEventDto
{
    public DateTimeOffset? Timestamp { get; set; }
    public string Type { get; set; }
    public TimeSpan Time { get; set; }
    public int Lap { get; set; }
    public int Sector { get; set; }
    public double Seconds { get; set; }
    public double StartSecond { get; set; }
}
