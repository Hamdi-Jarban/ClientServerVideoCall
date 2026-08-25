namespace VideoCall.Shared.Models;

/// <summary>
/// Server-side representation of a group room.
/// Rooms are independent of one another: membership changes in one
/// room must never affect any other room (each Room instance owns
/// its own Members list, and RoomManager keys rooms by RoomId).
/// </summary>
public class Room
{
    public string RoomId { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public HashSet<string> Members { get; init; } = new();
    public DateTime CreationTimeUtc { get; init; } = DateTime.UtcNow;
}
