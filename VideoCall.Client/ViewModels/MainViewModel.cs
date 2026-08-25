using System.Collections.ObjectModel;
using System.Windows.Input;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly NetworkClient _network;

    private bool _isConnected = true;
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> OnlineUsers { get; } = new();

    public string CurrentUsername => _network.Username ?? string.Empty;

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    public string ConnectionStatusText => IsConnected ? "متصل بالخادم" : "غير متصل بالخادم";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    /// <summary>Raised with the callee's name when the user clicks "اتصال" for that user.</summary>
    public event Action<string>? CallRequested;

    public event Action<CallRequestPayload>? IncomingCallReceived;

    public ICommand CallUserCommand { get; }
    public ICommand LogoutCommand { get; }

    public MainViewModel(NetworkClient network)
    {
        _network = network;
        _network.OnlineUsersUpdated += OnOnlineUsersUpdated;
        _network.Disconnected += OnDisconnected;
        _network.IncomingCall += payload => IncomingCallReceived?.Invoke(payload);
        _network.CallError += OnCallError;

        CallUserCommand = new RelayCommand(param =>
        {
            if (param is string username)
            {
                CallRequested?.Invoke(username);
            }
        });

        LogoutCommand = new RelayCommand(_ => _network.Dispose());
    }

    private void OnOnlineUsersUpdated(OnlineUsersUpdatePayload payload)
    {
        OnlineUsers.Clear();
        foreach (var username in payload.Usernames.Where(u => u != CurrentUsername))
        {
            OnlineUsers.Add(username);
        }

        OnPropertyChanged(nameof(CurrentUsername));
    }

    private void OnDisconnected()
    {
        IsConnected = false;
        OnPropertyChanged(nameof(ConnectionStatusText));
        StatusMessage = "تعذر الاتصال بالخادم";
    }

    private void OnCallError(ErrorPayload error)
    {
        StatusMessage = error.ErrorCode switch
        {
            ErrorCodes.TargetOffline => "المستخدم غير متصل",
            ErrorCodes.TargetBusy => "المستخدم مشغول بمكالمة أخرى",
            _ => "حدث خطأ غير متوقع"
        };
    }
}
