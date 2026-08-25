using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Models;
using VideoCall.Shared.Networking;

namespace VideoCall.Server;

public class Server
{
    private readonly TcpListener _listener;
    private readonly UserManager _userManager = new();
    private readonly CallManager _callManager = new();
    private readonly RoomManager _roomManager = new();
    private readonly UdpMediaRelay _udpRelay;

    private readonly ConcurrentDictionary<ClientSession, byte> _sessions = new();
    // Kept alongside _calls in CallManager so the UDP relay can resolve
    // "who are the two participants of this CallId" without needing a
    // reference back into ClientSession internals.
    private readonly ConcurrentDictionary<Guid, (string Caller, string Callee)> _callParticipants = new();

    public Server()
    {
        _listener = new TcpListener(IPAddress.Any, NetworkConfig.TcpControlPort);
        _udpRelay = new UdpMediaRelay(_callManager, ResolveCallParticipants, ResolveSessionToken);
        _callManager.OnCallTimedOut += call => _ = BroadcastCallTimedOutAsync(call.CallId, call.Caller, call.Callee);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        Logger.Info($"Server started. Listening on TCP port {NetworkConfig.TcpControlPort}.");

        _ = _udpRelay.RunAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var session = new ClientSession(tcpClient, this);
            _sessions.TryAdd(session, 0);
            Logger.Info($"Client connected from {tcpClient.Client.RemoteEndPoint}.");

            _ = session.RunAsync();
        }
    }

    public async Task DispatchAsync(ClientSession session, Message message)
    {
        switch (message.Type)
        {
            case MessageType.LoginRequest:
                await HandleLoginAsync(session, message.ReadPayload<LoginRequestPayload>()!);
                break;

            case MessageType.CallRequest:
                await HandleCallRequestAsync(session, message.ReadPayload<CallRequestPayload>()!);
                break;

            case MessageType.CallAccepted:
                await HandleCallAcceptAsync(session, message.ReadPayload<CallAcceptedPayload>()!);
                break;

            case MessageType.CallRejected:
                await HandleCallRejectAsync(session, message.ReadPayload<CallRejectedPayload>()!);
                break;

            case MessageType.CallEnded:
                await HandleCallEndAsync(session, message.ReadPayload<CallEndedPayload>()!);
                break;

            case MessageType.CreateRoomRequest:
                await HandleCreateRoomAsync(session, message.ReadPayload<CreateRoomRequestPayload>()!);
                break;

            case MessageType.AddUserToRoomRequest:
                await HandleAddUserToRoomAsync(session, message.ReadPayload<AddUserToRoomRequestPayload>()!);
                break;

            case MessageType.JoinRoomRequest:
                await HandleJoinRoomAsync(session, message.ReadPayload<JoinRoomRequestPayload>()!);
                break;

            case MessageType.LeaveRoomRequest:
                await HandleLeaveRoomAsync(session, message.ReadPayload<LeaveRoomRequestPayload>()!);
                break;

            case MessageType.Disconnect:
                session.Disconnect();
                break;

            default:
                Logger.Warn($"Received unexpected message type {message.Type} from {session.Username ?? "unauthenticated client"}.");
                break;
        }
    }

    // ===== Login =====

    private async Task HandleLoginAsync(ClientSession session, LoginRequestPayload request)
    {
        if (session.Username is not null)
        {
            return; // already logged in on this connection - ignore
        }

        if (!_userManager.ValidateCredentials(request.Username, request.Password))
        {
            await session.SendAsync(Message.Create(MessageType.LoginResponse,
                new LoginResponsePayload(false, ErrorCodes.InvalidCredentials, null, null)));
            return;
        }

        if (!_userManager.TryLogin(request.Username, session))
        {
            await session.SendAsync(Message.Create(MessageType.LoginResponse,
                new LoginResponsePayload(false, ErrorCodes.AlreadyLoggedIn, null, null)));
            return;
        }

        session.SetUsername(request.Username);
        Logger.Info($"{request.Username} logged in.");

        await session.SendAsync(Message.Create(MessageType.LoginResponse,
            new LoginResponsePayload(true, null, request.Username, session.SessionToken)));

        await BroadcastOnlineUsersAsync();
    }

    // ===== Call signaling =====

    private async Task HandleCallRequestAsync(ClientSession session, CallRequestPayload request)
    {
        if (session.Username is null) return;

        var (result, call) = _callManager.StartCall(session.Username, request.Callee, _userManager);
        if (result != CallOperationResult.Ok || call is null)
        {
            await session.SendAsync(Message.Create(MessageType.CallError, MapCallError(result)));
            return;
        }

        _callParticipants[call.CallId] = (call.Caller, call.Callee);
        Logger.Info($"Call {session.Username} -> {request.Callee} ({call.CallId}).");

        if (_userManager.TryGetSession(request.Callee, out var calleeSession))
        {
            await calleeSession.SendAsync(Message.Create(MessageType.CallRequest,
                new CallRequestPayload(call.CallId, call.Caller, call.Callee)));
        }
    }

    private async Task HandleCallAcceptAsync(ClientSession session, CallAcceptedPayload request)
    {
        if (session.Username is null) return;

        var result = _callManager.Accept(request.CallId, session.Username, out var call);
        if (result != CallOperationResult.Ok || call is null)
        {
            await session.SendAsync(Message.Create(MessageType.CallError, MapCallError(result)));
            return;
        }

        Logger.Info($"{session.Username} accepted call {call.CallId}.");

        if (_userManager.TryGetSession(call.Caller, out var callerSession))
        {
            await callerSession.SendAsync(Message.Create(MessageType.CallAccepted,
                new CallAcceptedPayload(call.CallId, call.Caller, call.Callee)));
        }
    }

    private async Task HandleCallRejectAsync(ClientSession session, CallRejectedPayload request)
    {
        if (session.Username is null) return;

        var result = _callManager.Reject(request.CallId, session.Username, out var call);
        if (result != CallOperationResult.Ok || call is null)
        {
            await session.SendAsync(Message.Create(MessageType.CallError, MapCallError(result)));
            return;
        }

        Logger.Info($"{session.Username} rejected call {call.CallId}.");
        _callParticipants.TryRemove(call.CallId, out _);
        _udpRelay.ForgetCall(call.CallId);

        if (_userManager.TryGetSession(call.Caller, out var callerSession))
        {
            await callerSession.SendAsync(Message.Create(MessageType.CallRejected,
                new CallRejectedPayload(call.CallId, call.Caller, call.Callee)));
        }
    }

    private async Task HandleCallEndAsync(ClientSession session, CallEndedPayload request)
    {
        if (session.Username is null) return;

        var result = _callManager.End(request.CallId, session.Username, out var call);
        if (result != CallOperationResult.Ok || call is null)
        {
            await session.SendAsync(Message.Create(MessageType.CallError, MapCallError(result)));
            return;
        }

        Logger.Info($"Call {call.CallId} ended by {session.Username}.");
        _callParticipants.TryRemove(call.CallId, out _);
        _udpRelay.ForgetCall(call.CallId);

        string otherParty = string.Equals(call.Caller, session.Username, StringComparison.OrdinalIgnoreCase) ? call.Callee : call.Caller;
        if (_userManager.TryGetSession(otherParty, out var otherSession))
        {
            await otherSession.SendAsync(Message.Create(MessageType.CallEnded, new CallEndedPayload(call.CallId, session.Username)));
        }
    }

    private async Task BroadcastCallTimedOutAsync(Guid callId, string caller, string callee)
    {
        _callParticipants.TryRemove(callId, out _);
        _udpRelay.ForgetCall(callId);

        foreach (var username in new[] { caller, callee })
        {
            if (_userManager.TryGetSession(username, out var s))
            {
                await s.SendAsync(Message.Create(MessageType.CallTimedOut, new CallTimedOutPayload(callId)));
            }
        }
    }

    private static ErrorPayload MapCallError(CallOperationResult result) => result switch
    {
        CallOperationResult.TargetOffline => new ErrorPayload(ErrorCodes.TargetOffline, "The user you called is offline."),
        CallOperationResult.TargetBusy => new ErrorPayload(ErrorCodes.TargetBusy, "The user you called is busy."),
        CallOperationResult.CallNotFound => new ErrorPayload(ErrorCodes.CallNotFound, "This call no longer exists."),
        CallOperationResult.NotYourCall => new ErrorPayload(ErrorCodes.NotYourCall, "This call does not belong to you."),
        CallOperationResult.InvalidState => new ErrorPayload(ErrorCodes.InvalidCallState, "This operation is not valid for the call's current state."),
        _ => new ErrorPayload(ErrorCodes.UnexpectedError, "Unexpected call error.")
    };

    // ===== Rooms =====

    private async Task HandleCreateRoomAsync(ClientSession session, CreateRoomRequestPayload request)
    {
        if (session.Username is null) return;

        var result = _roomManager.CreateRoom(request.RoomId, session.Username, out var room);
        if (result != RoomOperationResult.Ok || room is null)
        {
            await session.SendAsync(Message.Create(MessageType.RoomError, MapRoomError(result)));
            return;
        }

        Logger.Info($"{session.Username} created room {room.RoomId}.");
        await session.SendAsync(Message.Create(MessageType.RoomUpdate, ToRoomUpdate(room)));
    }

    private async Task HandleAddUserToRoomAsync(ClientSession session, AddUserToRoomRequestPayload request)
    {
        if (session.Username is null) return;

        var result = _roomManager.AddUser(request.RoomId, session.Username, request.Username, _userManager, out var room);
        if (result != RoomOperationResult.Ok || room is null)
        {
            await session.SendAsync(Message.Create(MessageType.RoomError, MapRoomError(result)));
            return;
        }

        await BroadcastRoomUpdateAsync(room);

        if (_userManager.TryGetSession(request.Username, out var addedSession))
        {
            await addedSession.SendAsync(Message.Create(MessageType.RoomUpdate, ToRoomUpdate(room)));
        }
    }

    private async Task HandleJoinRoomAsync(ClientSession session, JoinRoomRequestPayload request)
    {
        if (session.Username is null) return;

        var result = _roomManager.Join(request.RoomId, session.Username, out var room);
        if (result != RoomOperationResult.Ok || room is null)
        {
            await session.SendAsync(Message.Create(MessageType.RoomError, MapRoomError(result)));
            return;
        }

        await BroadcastRoomUpdateAsync(room);
    }

    private async Task HandleLeaveRoomAsync(ClientSession session, LeaveRoomRequestPayload request)
    {
        if (session.Username is null) return;

        var result = _roomManager.Leave(request.RoomId, session.Username, out var room);
        if (result != RoomOperationResult.Ok)
        {
            await session.SendAsync(Message.Create(MessageType.RoomError, MapRoomError(result)));
            return;
        }

        if (room is not null && room.Members.Count > 0)
        {
            await BroadcastRoomUpdateAsync(room);
        }
    }

    private static RoomErrorPayload MapRoomError(RoomOperationResult result) => result switch
    {
        RoomOperationResult.RoomAlreadyExists => new RoomErrorPayload(ErrorCodes.RoomAlreadyExists, "A room with this ID already exists."),
        RoomOperationResult.RoomNotFound => new RoomErrorPayload(ErrorCodes.RoomNotFound, "This room does not exist."),
        RoomOperationResult.UserNotFound => new RoomErrorPayload(ErrorCodes.UserNotFound, "That user is not online."),
        RoomOperationResult.NotRoomMember => new RoomErrorPayload(ErrorCodes.NotRoomMember, "You are not a member of this room."),
        _ => new RoomErrorPayload(ErrorCodes.UnexpectedError, "Unexpected room error.")
    };

    private static RoomUpdatePayload ToRoomUpdate(Room room) =>
        new(room.RoomId, room.Host, room.Members.ToList());

    private async Task BroadcastRoomUpdateAsync(Room room)
    {
        var payload = ToRoomUpdate(room);
        foreach (var member in room.Members.ToList())
        {
            if (_userManager.TryGetSession(member, out var memberSession))
            {
                await memberSession.SendAsync(Message.Create(MessageType.RoomUpdate, payload));
            }
        }
    }

    // ===== Presence / disconnect =====

    private async Task BroadcastOnlineUsersAsync()
    {
        var payload = new OnlineUsersUpdatePayload(_userManager.GetOnlineUsernames());
        var message = Message.Create(MessageType.OnlineUsersUpdate, payload);

        foreach (var session in _sessions.Keys)
        {
            if (session.Username is not null)
            {
                await session.SendAsync(message);
            }
        }
    }

    public async Task OnClientDisconnectedAsync(ClientSession session)
    {
        _sessions.TryRemove(session, out _);

        if (session.Username is null)
        {
            return;
        }

        Logger.Info($"{session.Username} disconnected.");
        _userManager.Logout(session.Username);

        var endedCall = _callManager.EndAnyActiveCallFor(session.Username);
        if (endedCall is not null)
        {
            _callParticipants.TryRemove(endedCall.CallId, out _);
            _udpRelay.ForgetCall(endedCall.CallId);

            string otherParty = string.Equals(endedCall.Caller, session.Username, StringComparison.OrdinalIgnoreCase) ? endedCall.Callee : endedCall.Caller;
            if (_userManager.TryGetSession(otherParty, out var otherSession))
            {
                await otherSession.SendAsync(Message.Create(MessageType.CallEnded, new CallEndedPayload(endedCall.CallId, session.Username)));
            }
        }

        var affectedRooms = _roomManager.RemoveUserFromAllRooms(session.Username);
        foreach (var room in affectedRooms)
        {
            await BroadcastRoomUpdateAsync(room);
        }

        await BroadcastOnlineUsersAsync();
    }

    // ===== Helpers for UdpMediaRelay =====

    private (string? UserA, string? UserB) ResolveCallParticipants(Guid callId) =>
        _callParticipants.TryGetValue(callId, out var pair) ? (pair.Caller, pair.Callee) : (null, null);

    private Guid? ResolveSessionToken(string username) =>
        _userManager.TryGetSession(username, out var session) ? session.SessionToken : null;
}
