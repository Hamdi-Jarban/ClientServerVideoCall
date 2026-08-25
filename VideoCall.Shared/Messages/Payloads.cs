namespace VideoCall.Shared.Messages;

// ===== Auth =====

public record LoginRequestPayload(string Username, string Password);

public record LoginResponsePayload(bool Success, string? ErrorCode, string? Username, Guid? SessionToken);

// ===== Presence =====

public record OnlineUsersUpdatePayload(List<string> Usernames);

// ===== Call signaling =====

public record CallRequestPayload(Guid CallId, string Caller, string Callee);

public record CallAcceptedPayload(Guid CallId, string Caller, string Callee);

public record CallRejectedPayload(Guid CallId, string Caller, string Callee);

public record CallEndedPayload(Guid CallId, string EndedBy);

public record CallTimedOutPayload(Guid CallId);

public record CallErrorPayload(string ErrorCode, string Message);

// ===== Rooms =====

public record CreateRoomRequestPayload(string RoomId);

public record AddUserToRoomRequestPayload(string RoomId, string Username);

public record JoinRoomRequestPayload(string RoomId);

public record LeaveRoomRequestPayload(string RoomId);

public record RoomUpdatePayload(string RoomId, string Host, List<string> Members);

public record RoomErrorPayload(string ErrorCode, string Message);

// ===== Generic =====

public record ErrorPayload(string ErrorCode, string Message);

/// <summary>
/// Standard error codes shared between Server and Client so the Client's
/// Arabic UI can map a code to the correct Arabic message without the
/// Server ever needing to know about Arabic strings.
/// </summary>
public static class ErrorCodes
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AlreadyLoggedIn = "ALREADY_LOGGED_IN";
    public const string TargetOffline = "TARGET_OFFLINE";
    public const string TargetBusy = "TARGET_BUSY";
    public const string CallNotFound = "CALL_NOT_FOUND";
    public const string NotYourCall = "NOT_YOUR_CALL";
    public const string InvalidCallState = "INVALID_CALL_STATE";
    public const string RoomAlreadyExists = "ROOM_ALREADY_EXISTS";
    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string NotRoomMember = "NOT_ROOM_MEMBER";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string ServerUnavailable = "SERVER_UNAVAILABLE";
    public const string UnexpectedError = "UNEXPECTED_ERROR";
}
