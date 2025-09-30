namespace LapViz.LiveTiming.Models;

public class GetSessionDataRequestDto
{
    public string SessionId { get; set; }
    public DateTimeOffset Timestamp { get; set; }

}

public class SessionDataDto
{
    public string Id { get; set; } // Id of the session
    public DateTimeOffset LastUpdated { get; set; }
    public IList<SessionDataDeviceDto> Devices { get; set; }
    public string Description { get; set; }
    public string CircuitCode { get; set; }
    public int? Sectors { get; set; }
    public bool IsPrivate { get; set; }
}
