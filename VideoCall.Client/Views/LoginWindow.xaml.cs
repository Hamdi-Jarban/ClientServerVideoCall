using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Client.ViewModels;

namespace VideoCall.Client.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;
    private readonly NetworkClient _network;

    public LoginWindow(NetworkClient network)
    {
        InitializeComponent();
        _network = network;
        _viewModel = new LoginViewModel(network);
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        DataContext = _viewModel;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoginAsync(PasswordBox.Password);
        PasswordBox.Clear();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoginAsync(PasswordBox.Password);
        PasswordBox.Clear();
    }

    private void OnLoginSucceeded()
    {
        var main = new MainWindow(_network);
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
