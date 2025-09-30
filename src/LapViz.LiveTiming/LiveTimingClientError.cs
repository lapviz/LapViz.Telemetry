namespace LapViz.LiveTiming;

public class LiveTimingClientError
{
    public string Code { get; }
    public string Message { get; }
    public LiveTimingClientState State { get; }

    public LiveTimingClientError(string code, string message, LiveTimingClientState state)
    {
        Code = code;
        Message = message;
        State = state;
    }

    public override string ToString() => $"[{Code}] {Message} (State: {State})";
}
