using VideoCall.Client.ViewModels;

namespace VideoCall.Client.Views;

public partial class RoomWindow : System.Windows.Window
{
    public RoomWindow(RoomViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
    {

    }
}
