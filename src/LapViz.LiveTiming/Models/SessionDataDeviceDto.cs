namespace LapViz.LiveTiming.Models;

public class SessionDataDeviceDto
{
    public string DeviceId { get; set; } // Device ID
    public string UserId { get; set; }
    public string DisplayName { get; set; }
    public string Category { get; set; }
    public string SessionId { get; set; }
    public string ConnectionId { get; set; }
    public string CircuitCode { get; set; }
    public DeviceTypeDto Type { get; set; }
    public string TimekeeperId { get; set; }
    public string IpAddress { get; set; }
    public string Token { get; set; }
    public List<SessionDeviceEventDto> Events { get; set; } = new List<SessionDeviceEventDto>();
}

public class SessionDeviceEventDto
{
    public string Id { get; set; }
    public string DeviceId { get; set; }
    public string SessionId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public TimeSpan Time { get; set; }
    public SessionEventTypeDto Type { get; set; }
    public int LapNumber { get; set; }
    public int SectorNumber { get; set; }
    public double Factor { get; set; }
    public DateTime? Deleted { get; set; }
    public override string ToString()
    {
        return $"{Type} > Lap {LapNumber} / Sector {SectorNumber} : {Time.ToString("m\\:ss\\.fff")}";
    }
}

public enum SessionEventTypeDto
{
    Lap,
    Sector
}

public enum DeviceTypeDto
{
    LapTimer,
    Stopwatch,
    Other
}
