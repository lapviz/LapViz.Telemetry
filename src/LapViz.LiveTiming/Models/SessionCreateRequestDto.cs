namespace LapViz.LiveTiming.Models;

public class SessionCreateRequestDto
{
    public bool IsPrivate { get; set; }
    public string Description { get; set; }
    public string CircuitCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Account { get; set; }
    public string Password { get; set; }
}
