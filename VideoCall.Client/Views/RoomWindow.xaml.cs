using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Client.ViewModels;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.Views;

public partial class RoomWindow : Window
{
    private readonly NetworkClient _network;
    private readonly RoomViewModel _viewModel;
    private GroupCallWindow? _groupCallWindow;

    public RoomWindow(NetworkClient network)
    {
        InitializeComponent();
        _network = network;
        _viewModel = new RoomViewModel(network);
        _viewModel.GroupMediaStarted += OnGroupMediaStarted;
        DataContext = _viewModel;
    }

    private void OnGroupMediaStarted(RoomMediaPayload payload)
    {
        if (_groupCallWindow is not null)
        {
            _groupCallWindow.Activate();
            return;
        }

        var callViewModel = new GroupCallViewModel(
            _network,
            _network.ServerHost ?? "127.0.0.1",
            payload.RoomId,
            payload.MediaId,
            _viewModel.Members.ToArray());

        _groupCallWindow = new GroupCallWindow(callViewModel) { Owner = this };
        callViewModel.Closed += () =>
        {
            _groupCallWindow?.Close();
            _groupCallWindow = null;
        };
        _groupCallWindow.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.GroupMediaStarted -= OnGroupMediaStarted;
        _groupCallWindow?.Close();
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
