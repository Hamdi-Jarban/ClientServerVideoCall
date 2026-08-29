using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Client.ViewModels;
using VideoCall.Shared.Messages;
using System.Windows.Controls;

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
        _viewModel.PrivateCallRequested += OnCallRequested;
        _viewModel.IncomingCall += OnIncomingCallReceived;
        _network.RoomInviteReceived += OnRoomInviteReceived;
        DataContext = _viewModel;

        Closed += MainWindow_Closed;
    }

    private void OnCallRequested(string callee)
    {
        if (_activeCallWindow is not null || string.IsNullOrWhiteSpace(callee)) return;
        _ = _network.RequestCallAsync(callee.Trim());
    }

    private void OnIncomingCallReceived(CallRequestPayload payload)
    {
        // The server echoes the request to the caller so both sides know the CallId.
        if (string.Equals(payload.Caller, _network.Username, StringComparison.OrdinalIgnoreCase))
        {
            if (_activeCallWindow is not null) return;
            var outgoingVm = new CallViewModel(_network, GetServerAddress(), payload.Callee, payload.CallId);
            _activeCallWindow = new CallWindow(outgoingVm) { Owner = this };
            outgoingVm.CallClosed += () =>
            {
                _activeCallWindow?.Close();
                _activeCallWindow = null;
            };
            _activeCallWindow.Show();
            return;
        }

        if (_activeCallWindow is not null || _incomingCallWindow is not null)
        {
            _ = _network.RejectCallAsync(payload.CallId, payload.Caller);
            return;
        }

        _incomingCallWindow = new IncomingCallWindow(payload.Caller) { Owner = this };
        _incomingCallWindow.Accepted += async () =>
        {
            // Subscribe before accepting so the media-start event cannot be missed.
            var callViewModel = new CallViewModel(_network, GetServerAddress(), payload.Caller, payload.CallId);
            _activeCallWindow = new CallWindow(callViewModel) { Owner = this };
            await _network.AcceptCallAsync(payload.CallId, payload.Caller);
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

    private void OnRoomInviteReceived(RoomInvitePayload invite)
    {
        var dialog = new RoomInviteWindow(_network, invite) { Owner = this };
        dialog.Closed += async (_, _) =>
        {
            // The server adds the member only after acceptance. Asking for the
            // room snapshot here makes the accepted room appear immediately.
            await Task.Delay(150);
            if (dialog.DialogResult is not false)
            {
                var roomWindow = new RoomWindow(_network) { Owner = this };
                roomWindow.Show();
                await _network.JoinRoomAsync(invite.RoomId);
            }
        };
        dialog.ShowDialog();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _network.RoomInviteReceived -= OnRoomInviteReceived;
        _viewModel.Dispose();
        _incomingCallWindow?.Close();
        _activeCallWindow?.Close();
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
        var roomWindow = new RoomWindow(_network) { Owner = this };
        roomWindow.Show();
    }

    private void Rooms_Click(object sender, RoutedEventArgs e) => RoomsButton_Click(sender, e);

    private void Call_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string target }) OnCallRequested(target);
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        var login = new LoginWindow(_network);
        Application.Current.MainWindow = login;
        login.Show();
    }

    private void Logout_Click(object sender, RoutedEventArgs e) => LogoutButton_Click(sender, e);
}
