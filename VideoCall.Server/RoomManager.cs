using System.Collections.Concurrent;
using VideoCall.Shared.Models;

namespace VideoCall.Server;

public enum RoomOperationResult
{
    Ok,
    RoomAlreadyExists,
    RoomNotFound,
    UserNotFound,
    NotRoomMember
}

/// <summary>
/// Owns every Room, keyed by RoomId. Each Room owns its own Members set,
/// so an operation on one room can never leak into another room's state -
/// there is no shared mutable collection between rooms.
/// </summary>
public class RoomManager
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);

    public RoomOperationResult CreateRoom(string roomId, string host, out Room? room)
    {
        room = new Room { RoomId = roomId, Host = host, Members = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host } };
        if (!_rooms.TryAdd(roomId, room))
        {
            room = null;
            return RoomOperationResult.RoomAlreadyExists;
        }

        return RoomOperationResult.Ok;
    }

    public RoomOperationResult AddUser(string roomId, string requestingUser, string userToAdd, UserManager users, out Room? room)
    {
        if (!_rooms.TryGetValue(roomId, out room))
        {
            return RoomOperationResult.RoomNotFound;
        }

        if (!room.Members.Contains(requestingUser))
        {
            return RoomOperationResult.NotRoomMember;
        }

        if (!users.IsOnline(userToAdd))
        {
            return RoomOperationResult.UserNotFound;
        }

        lock (room)
        {
            room.Members.Add(userToAdd);
        }

        return RoomOperationResult.Ok;
    }

    public RoomOperationResult Join(string roomId, string username, out Room? room)
    {
        if (!_rooms.TryGetValue(roomId, out room))
        {
            return RoomOperationResult.RoomNotFound;
        }

        lock (room)
        {
            room.Members.Add(username);
        }

        return RoomOperationResult.Ok;
    }

    public RoomOperationResult Leave(string roomId, string username, out Room? room)
    {
        if (!_rooms.TryGetValue(roomId, out room))
        {
            return RoomOperationResult.RoomNotFound;
        }

        lock (room)
        {
            room.Members.Remove(username);
            if (room.Members.Count == 0)
            {
                _rooms.TryRemove(roomId, out _);
            }
        }

        return RoomOperationResult.Ok;
    }

    /// <summary>Removes a disconnected user from every room they belonged to.</summary>
    public List<Room> RemoveUserFromAllRooms(string username)
    {
        var affected = new List<Room>();
        foreach (var room in _rooms.Values)
        {
            lock (room)
            {
                if (room.Members.Remove(username))
                {
                    affected.Add(room);
                }
            }
        }

        foreach (var room in affected)
        {
            if (room.Members.Count == 0)
            {
                _rooms.TryRemove(room.RoomId, out _);
            }
        }

        return affected;
    }
}
