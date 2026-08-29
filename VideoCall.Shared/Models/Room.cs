namespace VideoCall.Shared.Models;


public class Room
{
    public string RoomId { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public HashSet<string> Members { get; init; } = new();
    public DateTime CreationTimeUtc { get; init; } = DateTime.UtcNow;
}
