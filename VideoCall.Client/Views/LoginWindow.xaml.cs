using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Client.ViewModels;

namespace VideoCall.Client.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        var network = new NetworkClient();
        _viewModel = new LoginViewModel(network);
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        DataContext = _viewModel;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoginAsync(PasswordBox.Password);
    }
    private void OnLoginSucceeded(NetworkClient network)
    {
        var mainWindow = new MainWindow(network);
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
        Close();
    }

    private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {

    }
}
