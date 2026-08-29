using VideoCall.Shared.Messages;
using System.Collections.Concurrent;
using VideoCall.Shared.Models;
using VideoCall.Shared.Networking;
namespace VideoCall.Server;

/// <summary>
/// Converts TCP messages into domain operations and sends the resulting
/// events to the affected sessions. It is the only class that knows both the
/// wire message types and the conversation manager.
/// </summary>
public sealed class ProtocolRouter
{
    private readonly PresenceManager _presence;
    private readonly ConversationManager _conversations;
    private readonly MediaRelay _media;
    private readonly ICredentialValidator _credentials;
    private readonly Func<string, Message, CancellationToken, Task> _sendToUser;
    private readonly ConcurrentDictionary<Guid, PendingRoomInvite> _pendingInvites = new();

    private sealed record PendingRoomInvite(Guid InviteId, string RoomId, string Host, string Invitee);

    public ProtocolRouter(
        PresenceManager presence,
        ConversationManager conversations,
        MediaRelay media,
        ICredentialValidator credentials,
        Func<string, Message, CancellationToken, Task> sendToUser)
    {
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sendToUser = sendToUser ?? throw new ArgumentNullException(nameof(sendToUser));
    }

    public async Task DispatchAsync(ClientSession session, Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);

        switch (message.Type)
        {
            case MessageType.LoginRequest:
                await LoginAsync(session, message.ReadPayload<LoginRequestPayload>(), ct);
                return;

            case MessageType.CallRequest:
                await RequestPrivateCallAsync(session, message.ReadPayload<CallRequestPayload>(), ct);
                return;

            case MessageType.CallAccepted:
                await AcceptPrivateCallAsync(session, message.ReadPayload<CallAcceptedPayload>(), ct);
                return;

            case MessageType.CallRejected:
                await RejectPrivateCallAsync(session, message.ReadPayload<CallRejectedPayload>(), ct);
                return;

            case MessageType.CallEnded:
                await EndPrivateCallAsync(session, message.ReadPayload<CallEndedPayload>(), ct);
                return;

            case MessageType.CreateRoomRequest:
                await CreateRoomAsync(session, message.ReadPayload<CreateRoomRequestPayload>(), ct);
                return;

            case MessageType.AddUserToRoomRequest:
                await AddUserToRoomAsync(session, message.ReadPayload<AddUserToRoomRequestPayload>(), ct);
                return;

            case MessageType.RoomInviteAccepted:
                await AcceptRoomInviteAsync(session, message.ReadPayload<RoomInviteAcceptedPayload>(), ct);
                return;

            case MessageType.RoomInviteRejected:
                await RejectRoomInviteAsync(session, message.ReadPayload<RoomInviteRejectedPayload>(), ct);
                return;

            case MessageType.JoinRoomRequest:
                await JoinRoomAsync(session, message.ReadPayload<JoinRoomRequestPayload>(), ct);
                return;

            case MessageType.LeaveRoomRequest:
                await LeaveConversationAsync(session, message.ReadPayload<LeaveRoomRequestPayload>(), ct);
                return;

            case MessageType.StartRoomMedia:
                await StartGroupMediaAsync(session, message.ReadPayload<StartRoomMediaPayload>(), ct);
                return;

            case MessageType.StopRoomMedia:
                await StopGroupMediaAsync(session, message.ReadPayload<StopRoomMediaPayload>(), ct);
                return;

            case MessageType.Disconnect:
                await session.CloseAsync();
                return;

            default:
                await SendErrorAsync(session, ErrorCodes.UnexpectedError, "Unsupported message type.", ct);
                return;
        }
    }

    private async Task LoginAsync(ClientSession session, LoginRequestPayload? request, CancellationToken ct)
    {
        if (request is null || session.IsAuthenticated)
        {
            await SendErrorAsync(session, ErrorCodes.InvalidCredentials, "Invalid login request.", ct);
            return;
        }

        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64 ||
            !_credentials.Validate(username, request.Password ?? string.Empty))
        {
            await session.SendAsync(Message.Create(
                MessageType.LoginResponse,
                new LoginResponsePayload(false, ErrorCodes.InvalidCredentials, null, null)), ct);
            return;
        }

        if (!_presence.TryAdd(username, session))
        {
            await session.SendAsync(Message.Create(
                MessageType.LoginResponse,
                new LoginResponsePayload(false, ErrorCodes.AlreadyLoggedIn, null, null)), ct);
            return;
        }

        session.SetAuthenticatedUsername(username);
        await session.SendAsync(Message.Create(
            MessageType.LoginResponse,
            new LoginResponsePayload(true, null, username, session.SessionToken)), ct);

        await BroadcastPresenceAsync(ct);
    }

    private async Task RequestPrivateCallAsync(ClientSession session, CallRequestPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var caller) || request is null) return;
        var callee = request.Callee?.Trim();
        if (string.IsNullOrWhiteSpace(callee) || caller.Equals(callee, StringComparison.OrdinalIgnoreCase)) return;
        if (!_presence.IsOnline(callee))
        {
            await SendErrorAsync(session, ErrorCodes.TargetOffline, "The target user is offline.", ct);
            return;
        }

        var callId = Guid.NewGuid();
        var result = _conversations.CreatePrivate(callId.ToString("N"), caller, callee, out _);
        if (result != ConversationOperation.Success)
        {
            await SendErrorAsync(session, ErrorCodes.TargetBusy, "The private conversation could not be created.", ct);
            return;
        }

        var callRequestMessage = Message.Create(
            MessageType.CallRequest,
            new CallRequestPayload(callId, caller, callee));
        await _sendToUser(callee, callRequestMessage, ct);
        // Echo the server-generated CallId to the caller; the client initially sent Guid.Empty.
        await _sendToUser(caller, callRequestMessage, ct);
    }

    private async Task AcceptPrivateCallAsync(ClientSession session, CallAcceptedPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var id = request.CallId.ToString("N");
        var result = _conversations.ActivateMedia(id, username, out _);
        if (result != ConversationOperation.Success)
        {
            await SendErrorAsync(session, ErrorCodes.InvalidCallState, "The call cannot be accepted.", ct);
            return;
        }

        await _sendToUser(request.Caller, Message.Create(
            MessageType.CallAccepted,
            new CallAcceptedPayload(request.CallId, request.Caller, username)), ct);
        await StartMediaForConversationAsync(id, ct);
    }

    private async Task RejectPrivateCallAsync(ClientSession session, CallRejectedPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var id = request.CallId.ToString("N");
        var result = _conversations.EndPrivate(id, username, out var members);
        if (result != ConversationOperation.Success) return;

        _media.ForgetConversation(id);
        var message = Message.Create(
            MessageType.CallRejected,
            new CallRejectedPayload(request.CallId, request.Caller, username));
        foreach (var member in members.Where(x => !x.Equals(username, StringComparison.OrdinalIgnoreCase)))
            await _sendToUser(member, message, ct);
    }

    private async Task EndPrivateCallAsync(ClientSession session, CallEndedPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var id = request.CallId.ToString("N");
        var result = _conversations.EndPrivate(id, username, out var members);
        if (result != ConversationOperation.Success) return;

        _media.ForgetConversation(id);
        var message = Message.Create(MessageType.CallEnded, new CallEndedPayload(request.CallId, username));
        foreach (var member in members.Where(x => !x.Equals(username, StringComparison.OrdinalIgnoreCase)))
            await _sendToUser(member, message, ct);
    }

    private async Task CreateRoomAsync(ClientSession session, CreateRoomRequestPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var id = request.RoomId?.Trim();
        var result = _conversations.CreateGroup(id ?? string.Empty, username, out var conversation);
        if (result != ConversationOperation.Success || conversation is null)
        {
            await SendRoomErrorAsync(session, result, ct);
            return;
        }

        await BroadcastConversationUpdateAsync(conversation, ct);
    }

    private async Task AddUserToRoomAsync(ClientSession session, AddUserToRoomRequestPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var host) || request is null) return;
        var invitee = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(invitee) || !_presence.IsOnline(invitee))
        {
            await SendErrorAsync(session, ErrorCodes.UserNotFound, "The invited user is offline.", ct);
            return;
        }
        if (!_conversations.TryGet(request.RoomId, out var room) || room is null ||
            !room.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
        {
            await SendRoomErrorAsync(session, ConversationOperation.NotHost, ct);
            return;
        }
        var inviteId = Guid.NewGuid();
        var pending = new PendingRoomInvite(inviteId, room.Id, host, invitee);
        _pendingInvites[inviteId] = pending;
        await _sendToUser(invitee, Message.Create(MessageType.RoomInvite,
            new RoomInvitePayload(inviteId, room.Id, host, invitee)), ct);
    }

    private async Task AcceptRoomInviteAsync(ClientSession session, RoomInviteAcceptedPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var invitee) || request is null ||
            !_pendingInvites.TryRemove(request.InviteId, out var pending) ||
            !pending.Invitee.Equals(invitee, StringComparison.OrdinalIgnoreCase)) return;

        var result = _conversations.AddMember(pending.RoomId, pending.Host, invitee, out var conversation);
        if (result != ConversationOperation.Success && result != ConversationOperation.AlreadyMember)
        {
            await SendRoomErrorAsync(session, result, ct);
            return;
        }
        if (conversation is not null)
        {
            await BroadcastConversationUpdateAsync(conversation, ct);
            await _sendToUser(pending.Host, Message.Create(MessageType.RoomInviteAccepted,
                new RoomInviteAcceptedPayload(pending.InviteId, pending.RoomId, invitee)), ct);
        }
    }

    private async Task RejectRoomInviteAsync(ClientSession session, RoomInviteRejectedPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var invitee) || request is null ||
            !_pendingInvites.TryRemove(request.InviteId, out var pending) ||
            !pending.Invitee.Equals(invitee, StringComparison.OrdinalIgnoreCase)) return;
        await _sendToUser(pending.Host, Message.Create(MessageType.RoomInviteRejected,
            new RoomInviteRejectedPayload(pending.InviteId, pending.RoomId, invitee)), ct);
    }

    private async Task JoinRoomAsync(ClientSession session, JoinRoomRequestPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var result = _conversations.Join(request.RoomId, username, out var conversation);
        if (result is not (ConversationOperation.Success or ConversationOperation.AlreadyMember) || conversation is null)
        {
            await SendRoomErrorAsync(session, result, ct);
            return;
        }

        await BroadcastConversationUpdateAsync(conversation, ct);
    }

    private async Task LeaveConversationAsync(ClientSession session, LeaveRoomRequestPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var result = _conversations.Leave(request.RoomId, username, out var conversation, out var removed);
        if (result != ConversationOperation.Success)
        {
            await SendRoomErrorAsync(session, result, ct);
            return;
        }

        _media.RemoveEndpoint(request.RoomId, username);
        if (removed) _media.ForgetConversation(request.RoomId);
        else if (conversation is not null) await BroadcastConversationUpdateAsync(conversation, ct);
    }

    private async Task StartGroupMediaAsync(ClientSession session, StartRoomMediaPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        var result = _conversations.StartMedia(request.RoomId, username, out var conversation);
        if (result != ConversationOperation.Success || conversation is null)
        {
            await SendErrorAsync(session, ErrorCodes.NotRoomMember, "Only the host can start this conversation.", ct);
            return;
        }
        await StartMediaForConversationAsync(conversation.Id, ct);
    }

    private async Task StopGroupMediaAsync(ClientSession session, StopRoomMediaPayload? request, CancellationToken ct)
    {
        if (!RequireAuthenticated(session, out var username) || request is null) return;
        _conversations.TryGet(request.RoomId, out var beforeStop);
        var result = _conversations.StopMedia(request.RoomId, username, out var conversation);
        if (result != ConversationOperation.Success || conversation is null) return;

        _media.ForgetConversation(conversation.Id);
        var message = Message.Create(MessageType.RoomMediaStopped,
            new RoomMediaPayload(conversation.Id, beforeStop?.MediaId ?? Guid.Empty));
        await SendToConversationAsync(conversation.Id, message, ct);
    }

    private async Task StartMediaForConversationAsync(string conversationId, CancellationToken ct)
    {
        if (!_conversations.TryGet(conversationId, out var activeConversation) || activeConversation.MediaId is not Guid mediaId)
            return;
        var message = Message.Create(MessageType.RoomMediaStarted,
            new RoomMediaPayload(conversationId, mediaId));
        await SendToConversationAsync(conversationId, message, ct);
    }

    private async Task BroadcastConversationUpdateAsync(Conversation conversation, CancellationToken ct)
    {
        await SendToConversationAsync(conversation.Id,
            Message.Create(MessageType.RoomUpdate,
                new RoomUpdatePayload(conversation.Id, conversation.Host, conversation.Members.ToList())), ct);
    }

    private async Task SendToConversationAsync(string conversationId, Message message, CancellationToken ct)
    {
        foreach (var member in _conversations.GetMembersSnapshot(conversationId))
            await _sendToUser(member, message, ct);
    }

    public async Task HandleDisconnectAsync(ClientSession session, CancellationToken ct)
    {
        if (session.Username is null) return;
        _presence.Remove(session.Username, session);

        foreach (var conversation in _conversations.RemoveUserFromAll(session.Username))
        {
            _media.RemoveEndpoint(conversation.Id, session.Username);
            await BroadcastConversationUpdateAsync(conversation, ct);
        }

        await BroadcastPresenceAsync(ct);
    }

    private async Task BroadcastPresenceAsync(CancellationToken ct)
    {
        var message = Message.Create(
            MessageType.OnlineUsersUpdate,
            new OnlineUsersUpdatePayload(_presence.GetUsernames().ToList()));
        foreach (var session in _presence.GetSessions())
        {
            try { await session.SendAsync(message, ct); }
            catch { /* session cleanup is handled by ClientSession */ }
        }
    }

    private async Task SendRoomErrorAsync(ClientSession session, ConversationOperation result, CancellationToken ct)
    {
        var (code, text) = result switch
        {
            ConversationOperation.AlreadyExists => (ErrorCodes.RoomAlreadyExists, "Room already exists."),
            ConversationOperation.NotFound => (ErrorCodes.RoomNotFound, "Room not found."),
            ConversationOperation.NotMember => (ErrorCodes.NotRoomMember, "You are not a room member."),
            ConversationOperation.Full => (ErrorCodes.RoomFull, "Room is full."),
            _ => (ErrorCodes.UnexpectedError, "Unexpected room error.")
        };
        await session.SendAsync(Message.Create(MessageType.RoomError,
            new RoomErrorPayload(code, text)), ct);
    }

    private static async Task SendErrorAsync(ClientSession session, string code, string text, CancellationToken ct)
    {
        await session.SendAsync(Message.Create(MessageType.Error, new ErrorPayload(code, text)), ct);
    }

    private static bool RequireAuthenticated(ClientSession session, out string username)
    {
        username = session.Username ?? string.Empty;
        return !string.IsNullOrWhiteSpace(username);
    }
}
