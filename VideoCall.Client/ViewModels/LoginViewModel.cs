using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.ViewModels;

public sealed class LoginViewModel : ViewModelBase, IDisposable
{
    private readonly NetworkClient _network;
    private string _serverAddress = "127.0.0.1";
    private string _username = string.Empty;
    private string _status = string.Empty;
    private bool _busy;

    public string ServerAddress
    {
        get => _serverAddress;
        set => SetField(ref _serverAddress, value);
    }
    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }
    public bool IsBusy
    {
        get => _busy;
        private set => SetField(ref _busy, value);
    }

    public event Action? LoginSucceeded;

    public LoginViewModel(NetworkClient network)
    {
        _network = network;
        _network.LoginResponseReceived += OnLoginResponse;
    }

    public async Task LoginAsync(string password)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(ServerAddress) ||
            string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(password))
        {
            Status = "أدخل عنوان الخادم واسم المستخدم وكلمة المرور.";
            return;
        }

        IsBusy = true;
        Status = "جاري الاتصال بالخادم...";
        if (!_network.IsConnected)
        {
            var connected = await _network.ConnectAsync(ServerAddress.Trim());
            if (!connected)
            {
                IsBusy = false;
                Status = "تعذر الاتصال بالخادم.";
                return;
            }
        }

        await _network.LoginAsync(Username.Trim(), password);
    }

    private void OnLoginResponse(LoginResponsePayload response)
    {
        OnUi(() =>
        {
            IsBusy = false;
            if (response.Success)
            {
                Status = string.Empty;
                LoginSucceeded?.Invoke();
                return;
            }

            Status = response.ErrorCode switch
            {
                ErrorCodes.InvalidCredentials => "بيانات الدخول غير صحيحة.",
                ErrorCodes.AlreadyLoggedIn => "المستخدم مسجل الدخول مسبقًا.",
                _ => "فشل تسجيل الدخول."
            };
        });
    }

    public void Dispose() => _network.LoginResponseReceived -= OnLoginResponse;

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
