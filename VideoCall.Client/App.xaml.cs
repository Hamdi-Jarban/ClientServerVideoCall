using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Client.Views;

namespace VideoCall.Client;

public partial class App : Application
{
    private NetworkClient? _network;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _network = new NetworkClient();
        var login = new LoginWindow(_network);
        MainWindow = login;
        login.Show();
    }

    public void ShowMainWindow()
    {
        if (_network is null) return;
        var main = new MainWindow(_network);
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Close child windows first so camera, microphone, UDP and capture loops
        // are disposed before the TCP client is released.
        foreach (var window in Windows.OfType<Window>().ToArray())
        {
            try { window.Close(); } catch { }
        }
        _network?.Dispose();
        base.OnExit(e);
    }
}
