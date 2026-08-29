using System.Windows;
using VideoCall.Client.ViewModels;

namespace VideoCall.Client.Views;

public partial class GroupCallWindow : Window
{
    private readonly GroupCallViewModel _viewModel;

    public GroupCallWindow(GroupCallViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
