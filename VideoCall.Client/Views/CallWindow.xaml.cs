using VideoCall.Client.ViewModels;

namespace VideoCall.Client.Views;

public partial class CallWindow : System.Windows.Window
{
    private readonly CallViewModel _viewModel;

    public CallWindow(CallViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Title = $"المكالمة - {viewModel.OtherParty}";

        Closed += (_, _) => _viewModel.Dispose();
    }
}
