namespace VideoCall.Shared.Messages;

/// <summary>
/// Every kind of message that can travel over the TCP control channel.
/// The Message envelope carries one of these plus a JSON payload whose
/// shape depends on the type (see the matching *Payload record below).
/// </summary>
public enum MessageType
{
    // Auth
    LoginRequest,
    LoginResponse,

    // Presence
    OnlineUsersUpdate,

    // Call signaling
    CallRequest,
    CallAccepted,
    CallRejected,
    CallEnded,
    CallTimedOut,
    CallError,

    // Rooms
    CreateRoomRequest,
    AddUserToRoomRequest,
    JoinRoomRequest,
    LeaveRoomRequest,
    RoomUpdate,
    RoomError,
    StartRoomMedia,
    StopRoomMedia,
    RoomMediaStarted,
    RoomMediaStopped,
    RoomInvite,
    RoomInviteAccepted,
    RoomInviteRejected,

    // Generic
    Error,
    Disconnect,
    StopConversationMedia,
    StartConversationMedia
}
