using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly NetworkClient _network;
    private string _status = string.Empty;
    private bool _connected;

    public ObservableCollection<string> OnlineUsers { get; } = new();
    public string CurrentUsername => _network.Username ?? string.Empty;
    public bool IsConnected { get => _connected; private set => SetField(ref _connected, value); }
    public string Status { get => _status; set => SetField(ref _status, value); }
    public string ConnectionStatus => IsConnected ? "متصل بالخادم" : "غير متصل بالخادم";

    public ICommand CallUserCommand { get; }
    public ICommand OpenRoomsCommand { get; }
    public event Action<string>? PrivateCallRequested;
    public event Action<CallRequestPayload>? IncomingCall;
    public event Action<CallAcceptedPayload>? PrivateCallAccepted;
    public event Action<CallRejectedPayload>? PrivateCallRejected;
    public event Action<CallEndedPayload>? PrivateCallEnded;
    public event Action? OpenRoomsRequested;

    public MainViewModel(NetworkClient network)
    {
        _network = network;
        IsConnected = network.IsConnected;
        _network.OnlineUsersUpdated += OnUsersUpdated;
        _network.Disconnected += OnDisconnected;
        _network.IncomingCall += payload => OnUi(() => IncomingCall?.Invoke(payload));
        _network.CallAccepted += payload => OnUi(() => PrivateCallAccepted?.Invoke(payload));
        _network.CallRejected += payload => OnUi(() => PrivateCallRejected?.Invoke(payload));
        _network.CallEnded += payload => OnUi(() => PrivateCallEnded?.Invoke(payload));
        _network.ErrorReceived += OnError;

        CallUserCommand = new RelayCommand(() => { });
        OpenRoomsCommand = new RelayCommand(() => OpenRoomsRequested?.Invoke());
    }

    public void RequestPrivateCall(string username)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(username)) return;
        PrivateCallRequested?.Invoke(username);
    }

    public async Task RejectIncomingCallAsync(CallRequestPayload request) =>
        await _network.RejectCallAsync(request.CallId, request.Caller);

    public async Task AcceptIncomingCallAsync(CallRequestPayload request) =>
        await _network.AcceptCallAsync(request.CallId, request.Caller);

    private void OnUsersUpdated(OnlineUsersUpdatePayload payload)
    {
        OnUi(() =>
        {
            OnlineUsers.Clear();
            foreach (var user in payload.Usernames.Where(x => !x.Equals(CurrentUsername, StringComparison.OrdinalIgnoreCase)))
                OnlineUsers.Add(user);
            IsConnected = true;
            Raise(nameof(CurrentUsername));
            Raise(nameof(ConnectionStatus));
        });
    }

    private void OnDisconnected() => OnUi(() =>
    {
        IsConnected = false;
        Raise(nameof(ConnectionStatus));
        Status = "انقطع الاتصال بالخادم.";
    });

    private void OnError(ErrorPayload error) => OnUi(() => Status = error.Message);

    public void Dispose()
    {
        _network.OnlineUsersUpdated -= OnUsersUpdated;
        _network.Disconnected -= OnDisconnected;
        _network.ErrorReceived -= OnError;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
