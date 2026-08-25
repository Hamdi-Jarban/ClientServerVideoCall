using System.Net.Sockets;
using System.Windows;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Client.Services;


public class NetworkClient : IDisposable
{
    private TcpClient? _tcpClient;
    private TcpMessageReaderWriter? _framing;
    private CancellationTokenSource? _cts;

    public string? Username { get; private set; }
    public Guid? SessionToken { get; private set; }
    public string? ServerHost { get; private set; }
    public bool IsConnected => _tcpClient?.Connected ?? false;

    public event Action? Disconnected;
    public event Action<LoginResponsePayload>? LoginResponseReceived;
    public event Action<OnlineUsersUpdatePayload>? OnlineUsersUpdated;
    public event Action<CallRequestPayload>? IncomingCall;
    public event Action<CallAcceptedPayload>? CallAccepted;
    public event Action<CallRejectedPayload>? CallRejected;
    public event Action<CallEndedPayload>? CallEnded;
    public event Action<CallTimedOutPayload>? CallTimedOut;
    public event Action<ErrorPayload>? CallError;
    public event Action<RoomUpdatePayload>? RoomUpdated;
    public event Action<RoomErrorPayload>? RoomError;

    public async Task<bool> ConnectAsync(string host)
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, NetworkConfig.TcpControlPort);
            ServerHost = host;
            _framing = new TcpMessageReaderWriter(_tcpClient.GetStream());
            _cts = new CancellationTokenSource();
            _ = ReadLoopAsync(_cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task LoginAsync(string username, string password) =>
        SendAsync(Message.Create(MessageType.LoginRequest, new LoginRequestPayload(username, password)));

    public Task RequestCallAsync(string callee) =>
        SendAsync(Message.Create(MessageType.CallRequest, new CallRequestPayload(Guid.Empty, Username ?? "", callee)));

    public Task AcceptCallAsync(Guid callId, string caller) =>
        SendAsync(Message.Create(MessageType.CallAccepted, new CallAcceptedPayload(callId, caller, Username ?? "")));

    public Task RejectCallAsync(Guid callId, string caller) =>
        SendAsync(Message.Create(MessageType.CallRejected, new CallRejectedPayload(callId, caller, Username ?? "")));

    public Task EndCallAsync(Guid callId) =>
        SendAsync(Message.Create(MessageType.CallEnded, new CallEndedPayload(callId, Username ?? "")));

    public Task CreateRoomAsync(string roomId) =>
        SendAsync(Message.Create(MessageType.CreateRoomRequest, new CreateRoomRequestPayload(roomId)));

    public Task AddUserToRoomAsync(string roomId, string username) =>
        SendAsync(Message.Create(MessageType.AddUserToRoomRequest, new AddUserToRoomRequestPayload(roomId, username)));

    public Task JoinRoomAsync(string roomId) =>
        SendAsync(Message.Create(MessageType.JoinRoomRequest, new JoinRoomRequestPayload(roomId)));

    public Task LeaveRoomAsync(string roomId) =>
        SendAsync(Message.Create(MessageType.LeaveRoomRequest, new LeaveRoomRequestPayload(roomId)));

    private async Task SendAsync(Message message)
    {
        if (_framing is null || _cts is null)
        {
            return;
        }

        try
        {
            await _framing.WriteMessageAsync(message, _cts.Token);
        }
        catch (Exception)
        {
            RaiseDisconnected();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Message? message;
                try
                {
                    message = await _framing!.ReadMessageAsync(ct);
                }
                catch (Exception)
                {
                    break;
                }

                if (message is null)
                {
                    break;
                }

                HandleMessage(message);
            }
        }
        finally
        {
            RaiseDisconnected();
        }
    }

    private void HandleMessage(Message message)
    {
        switch (message.Type)
        {
            case MessageType.LoginResponse:
                var login = message.ReadPayload<LoginResponsePayload>()!;
                if (login.Success)
                {
                    Username = login.Username;
                    SessionToken = login.SessionToken;
                }
                Raise(() => LoginResponseReceived?.Invoke(login));
                break;

            case MessageType.OnlineUsersUpdate:
                Raise(() => OnlineUsersUpdated?.Invoke(message.ReadPayload<OnlineUsersUpdatePayload>()!));
                break;

            case MessageType.CallRequest:
                Raise(() => IncomingCall?.Invoke(message.ReadPayload<CallRequestPayload>()!));
                break;

            case MessageType.CallAccepted:
                Raise(() => CallAccepted?.Invoke(message.ReadPayload<CallAcceptedPayload>()!));
                break;

            case MessageType.CallRejected:
                Raise(() => CallRejected?.Invoke(message.ReadPayload<CallRejectedPayload>()!));
                break;

            case MessageType.CallEnded:
                Raise(() => CallEnded?.Invoke(message.ReadPayload<CallEndedPayload>()!));
                break;

            case MessageType.CallTimedOut:
                Raise(() => CallTimedOut?.Invoke(message.ReadPayload<CallTimedOutPayload>()!));
                break;

            case MessageType.CallError:
                Raise(() => CallError?.Invoke(message.ReadPayload<ErrorPayload>()!));
                break;

            case MessageType.RoomUpdate:
                Raise(() => RoomUpdated?.Invoke(message.ReadPayload<RoomUpdatePayload>()!));
                break;

            case MessageType.RoomError:
                Raise(() => RoomError?.Invoke(message.ReadPayload<RoomErrorPayload>()!));
                break;
        }
    }

    private static void Raise(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private bool _disconnectedRaised;

    private void RaiseDisconnected()
    {
        if (_disconnectedRaised) return;
        _disconnectedRaised = true;
        Raise(() => Disconnected?.Invoke());
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _tcpClient?.Close();
    }
}
