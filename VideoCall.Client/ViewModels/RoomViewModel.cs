using System.Collections.ObjectModel;
using System.Windows.Input;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.ViewModels;

public class RoomViewModel : ViewModelBase
{
    private readonly NetworkClient _network;

    private string _roomId = string.Empty;
    private string _host = string.Empty;
    private string _newMemberUsername = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _hasRoom;

    public ObservableCollection<string> Members { get; } = new();

    public string RoomId
    {
        get => _roomId;
        set => SetField(ref _roomId, value);
    }

    public string Host
    {
        get => _host;
        private set => SetField(ref _host, value);
    }

    public string NewMemberUsername
    {
        get => _newMemberUsername;
        set => SetField(ref _newMemberUsername, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool HasRoom
    {
        get => _hasRoom;
        private set => SetField(ref _hasRoom, value);
    }

    public ICommand CreateRoomCommand { get; }
    public ICommand AddUserCommand { get; }
    public ICommand LeaveRoomCommand { get; }

    public RoomViewModel(NetworkClient network)
    {
        _network = network;
        _network.RoomUpdated += OnRoomUpdated;
        _network.RoomError += OnRoomError;

        CreateRoomCommand = new RelayCommand(async _ =>
        {
            if (string.IsNullOrWhiteSpace(RoomId)) return;
            StatusMessage = "جاري إنشاء الغرفة...";
            await _network.CreateRoomAsync(RoomId);
        });

        AddUserCommand = new RelayCommand(async _ =>
        {
            if (string.IsNullOrWhiteSpace(NewMemberUsername) || !HasRoom) return;
            await _network.AddUserToRoomAsync(RoomId, NewMemberUsername);
            NewMemberUsername = string.Empty;
        });

        LeaveRoomCommand = new RelayCommand(async _ =>
        {
            if (!HasRoom) return;
            await _network.LeaveRoomAsync(RoomId);
            HasRoom = false;
            Members.Clear();
            StatusMessage = "تمت مغادرة الغرفة";
        });
    }

    private void OnRoomUpdated(RoomUpdatePayload payload)
    {
        if (!string.Equals(payload.RoomId, RoomId, StringComparison.OrdinalIgnoreCase))
        {
            RoomId = payload.RoomId;
        }

        Host = payload.Host;
        Members.Clear();
        foreach (var member in payload.Members)
        {
            Members.Add(member);
        }

        HasRoom = true;
        StatusMessage = string.Equals(Host, _network.Username, StringComparison.OrdinalIgnoreCase)
            ? "تم إنشاء الغرفة"
            : "تم الانضمام إلى الغرفة";
    }

    private void OnRoomError(RoomErrorPayload payload)
    {
        StatusMessage = payload.ErrorCode switch
        {
            ErrorCodes.RoomAlreadyExists => "الغرفة موجودة بالفعل",
            ErrorCodes.RoomNotFound => "الغرفة غير موجودة",
            ErrorCodes.UserNotFound => "المستخدم غير متصل",
            ErrorCodes.NotRoomMember => "أنت لست عضوًا في هذه الغرفة",
            _ => "حدث خطأ غير متوقع"
        };
    }
}
