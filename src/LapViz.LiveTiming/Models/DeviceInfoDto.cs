namespace LapViz.LiveTiming.Models;

public class DeviceInfoDto
{
    public string SessionId { get; set; }
    public string DeviceId { get; set; }
    public string UserId { get; set; }
    public string TimekeeperId { get; set; }
    public string DisplayName { get; set; }
    public string Category { get; set; }
    public DateTime? Deleted { get; set; }
}
