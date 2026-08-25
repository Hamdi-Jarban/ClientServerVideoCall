using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Client.ViewModels;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.Views;

public partial class MainWindow : Window
{
    private readonly NetworkClient _network;
    private readonly MainViewModel _viewModel;
    private CallWindow? _activeCallWindow;
    private IncomingCallWindow? _incomingCallWindow;

    public MainWindow(NetworkClient network)
    {
        InitializeComponent();
        _network = network;
        _viewModel = new MainViewModel(network);
        _viewModel.CallRequested += OnCallRequested;
        _viewModel.IncomingCallReceived += OnIncomingCallReceived;
        DataContext = _viewModel;

        Closed += (_, _) => _network.Dispose();
    }

    private void OnCallRequested(string callee)
    {
        if (_activeCallWindow is not null)
        {
            return; // already in a call
        }

        var callViewModel = new CallViewModel(_network, GetServerAddress(), callee);
        _activeCallWindow = new CallWindow(callViewModel);
        callViewModel.CallClosed += () =>
        {
            _activeCallWindow?.Close();
            _activeCallWindow = null;
        };
        _activeCallWindow.Show();
    }

    private void OnIncomingCallReceived(CallRequestPayload payload)
    {
        if (_activeCallWindow is not null || _incomingCallWindow is not null)
        {
            
            _ = _network.RejectCallAsync(payload.CallId, payload.Caller);
            return;
        }

        _incomingCallWindow = new IncomingCallWindow(payload.Caller);
        _incomingCallWindow.Accepted += async () =>
        {
            await _network.AcceptCallAsync(payload.CallId, payload.Caller);
            var callViewModel = new CallViewModel(_network, GetServerAddress(), payload.Caller, payload.CallId);
            _activeCallWindow = new CallWindow(callViewModel);
            callViewModel.CallClosed += () =>
            {
                _activeCallWindow?.Close();
                _activeCallWindow = null;
            };
            _activeCallWindow.Show();
            _incomingCallWindow?.Close();
            _incomingCallWindow = null;
        };
        _incomingCallWindow.Rejected += async () =>
        {
            await _network.RejectCallAsync(payload.CallId, payload.Caller);
            _incomingCallWindow?.Close();
            _incomingCallWindow = null;
        };
        _incomingCallWindow.Show();
    }

    private string GetServerAddress()
    {
        // The Login window's server textbox value isn't kept after that
        // window closes, so NetworkClient's already-open TcpClient's
        // remote endpoint is the reliable source of truth for "which
        // host is the server" when opening the UDP media channel.
        return _network.ServerHost ?? "127.0.0.1";
    }

    private void RoomsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_network is null)
        {
            MessageBox.Show("null");
        }
        else
        {
            var roomWindow = new RoomWindow(new RoomViewModel(_network));
            roomWindow.Owner = this;
            roomWindow.Show();
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
    }
}
