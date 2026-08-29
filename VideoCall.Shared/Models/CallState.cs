namespace VideoCall.Shared.Models;


public enum CallState
{
    Calling,
    Ringing,
    Connected,
    Ended,
    Rejected,
    TimedOut
}

/// <summary>
/// Server-side representation of a single 1-to-1 call.
/// </summary>
public class CallSession
{
    public Guid CallId { get; init; } = Guid.NewGuid();
    public string Caller { get; init; } = string.Empty;
    public string Callee { get; init; } = string.Empty;
    public CallState State { get; set; } = CallState.Calling;
    public DateTime CreationTimeUtc { get; init; } = DateTime.UtcNow;
}
