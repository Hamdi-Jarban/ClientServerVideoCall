using System.Collections.ObjectModel;
using System.Windows.Input;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;

namespace VideoCall.Client.ViewModels;

public sealed class RoomViewModel : ViewModelBase, IDisposable
{
    private readonly NetworkClient _network;
    private string _roomId = string.Empty;
    private string _host = string.Empty;
    private string _newMemberUsername = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _hasRoom;
    private bool _isMediaActive;
    private Guid _mediaId;

    public ObservableCollection<string> Members { get; } = new();

    public string RoomId { get => _roomId; set => SetField(ref _roomId, value); }
    public string Host { get => _host; private set => SetField(ref _host, value); }
    public string NewMemberUsername { get => _newMemberUsername; set => SetField(ref _newMemberUsername, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public bool HasRoom { get => _hasRoom; private set => SetField(ref _hasRoom, value); }
    public bool IsMediaActive { get => _isMediaActive; private set => SetField(ref _isMediaActive, value); }
    public Guid MediaId { get => _mediaId; private set => SetField(ref _mediaId, value); }
    public bool IsHost => string.Equals(Host, _network.Username, StringComparison.OrdinalIgnoreCase);

    public ICommand CreateRoomCommand { get; }
    public ICommand JoinRoomCommand { get; }
    public ICommand AddUserCommand { get; }
    public ICommand StartMediaCommand { get; }
    public ICommand StopMediaCommand { get; }
    public ICommand LeaveRoomCommand { get; }

    public event Action<RoomMediaPayload>? GroupMediaStarted;
    public event Action<RoomMediaPayload>? GroupMediaStopped;

    public RoomViewModel(NetworkClient network)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _network.RoomUpdated += OnRoomUpdated;
        _network.RoomError += OnRoomError;
        _network.RoomMediaStarted += OnRoomMediaStarted;
        _network.RoomMediaStopped += OnRoomMediaStopped;

        CreateRoomCommand = new AsyncCommand(async () =>
        {
            if (!HasRoom && !string.IsNullOrWhiteSpace(RoomId))
            {
                StatusMessage = "جاري إنشاء الغرفة...";
                await _network.CreateRoomAsync(RoomId.Trim());
            }
        });

        JoinRoomCommand = new AsyncCommand(async () =>
        {
            if (!HasRoom && !string.IsNullOrWhiteSpace(RoomId))
            {
                StatusMessage = "جاري الانضمام إلى الغرفة...";
                await _network.JoinRoomAsync(RoomId.Trim());
            }
        });

        AddUserCommand = new AsyncCommand(async () =>
        {
            if (!HasRoom || IsMediaActive || string.IsNullOrWhiteSpace(NewMemberUsername)) return;
            await _network.AddUserToRoomAsync(RoomId.Trim(), NewMemberUsername.Trim());
            NewMemberUsername = string.Empty;
        });

        StartMediaCommand = new AsyncCommand(async () =>
        {
            if (!HasRoom || !IsHost || IsMediaActive) return;
            StatusMessage = "جاري بدء المحادثة الجماعية...";
            await _network.StartRoomMediaAsync(RoomId.Trim());
        });

        StopMediaCommand = new AsyncCommand(async () =>
        {
            if (!HasRoom || !IsHost || !IsMediaActive) return;
            await _network.StopRoomMediaAsync(RoomId.Trim(), MediaId);
        });

        LeaveRoomCommand = new AsyncCommand(async () =>
        {
            if (!HasRoom) return;
            await _network.LeaveRoomAsync(RoomId.Trim());
            ResetRoom("تمت مغادرة الغرفة.");
        });
    }

    private void OnRoomUpdated(RoomUpdatePayload payload)
    {
        if (HasRoom && !string.Equals(payload.RoomId, RoomId, StringComparison.OrdinalIgnoreCase)) return;
        RoomId = payload.RoomId;
        Host = payload.Host;
        Members.Clear();
        foreach (var member in payload.Members.Distinct(StringComparer.OrdinalIgnoreCase)) Members.Add(member);
        HasRoom = true;
        Raise(nameof(IsHost));
        StatusMessage = IsHost ? "أنت مضيف الغرفة." : "تم تحديث أعضاء الغرفة.";
    }

    private void OnRoomMediaStarted(RoomMediaPayload payload)
    {
        if (!string.Equals(payload.RoomId, RoomId, StringComparison.OrdinalIgnoreCase)) return;
        MediaId = payload.MediaId;
        IsMediaActive = true;
        StatusMessage = "بدأت المحادثة الجماعية.";
        GroupMediaStarted?.Invoke(payload);
    }

    private void OnRoomMediaStopped(RoomMediaPayload payload)
    {
        if (!string.Equals(payload.RoomId, RoomId, StringComparison.OrdinalIgnoreCase)) return;
        IsMediaActive = false;
        MediaId = Guid.Empty;
        StatusMessage = "تم إيقاف المحادثة الجماعية.";
        GroupMediaStopped?.Invoke(payload);
    }

    private void OnRoomError(RoomErrorPayload payload)
    {
        StatusMessage = payload.ErrorCode switch
        {
            ErrorCodes.RoomAlreadyExists => "الغرفة موجودة بالفعل.",
            ErrorCodes.RoomNotFound => "الغرفة غير موجودة.",
            ErrorCodes.RoomFull => "الغرفة ممتلئة.",
            ErrorCodes.UserNotFound => "المستخدم غير متصل.",
            ErrorCodes.NotRoomMember => "أنت لست عضوًا أو لا تملك الصلاحية.",
            ErrorCodes.MediaAlreadyStarted => "المحادثة الجماعية بدأت بالفعل.",
            _ => payload.Message
        };
    }

    private void ResetRoom(string message)
    {
        HasRoom = false;
        IsMediaActive = false;
        MediaId = Guid.Empty;
        RoomId = string.Empty;
        Host = string.Empty;
        Members.Clear();
        StatusMessage = message;
    }

    public void Dispose()
    {
        _network.RoomUpdated -= OnRoomUpdated;
        _network.RoomError -= OnRoomError;
        _network.RoomMediaStarted -= OnRoomMediaStarted;
        _network.RoomMediaStopped -= OnRoomMediaStopped;
    }
}
