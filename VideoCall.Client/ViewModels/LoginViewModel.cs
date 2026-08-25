using System.Windows;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly NetworkClient _network;

    private string _serverAddress =string.Empty ;
    private string _username = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

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

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public event Action<NetworkClient>? LoginSucceeded;

    public LoginViewModel(NetworkClient network)
    {
        _network = network;
        _network.LoginResponseReceived += OnLoginResponse;
    }

    
    public async Task LoginAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            StatusMessage = "الرجاء إدخال اسم المستخدم وكلمة المرور";
            return;
        }

        IsBusy = true;
        StatusMessage = "جاري تسجيل الدخول...";

        if (!_network.IsConnected)
        {
            bool connected = await _network.ConnectAsync(ServerAddress);
            if (!connected)
            {
                StatusMessage = "تعذر الاتصال بالخادم";
                IsBusy = false;
                return;
            }

        }


        await _network.LoginAsync(Username, password);
    }

    private void OnLoginResponse(LoginResponsePayload response)
    {
        IsBusy = false;

        if (response.Success)
        {
            StatusMessage = string.Empty;
            LoginSucceeded?.Invoke(_network);
            return;
        }

        StatusMessage = response.ErrorCode switch
        {
            ErrorCodes.InvalidCredentials => "بيانات الدخول غير صحيحة",
            ErrorCodes.AlreadyLoggedIn => "المستخدم موجود بالفعل",
            _ => "حدث خطأ غير متوقع"
        };
    }
}
