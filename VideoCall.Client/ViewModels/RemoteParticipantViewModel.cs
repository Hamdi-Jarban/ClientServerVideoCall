using System.Windows.Media.Imaging;

namespace VideoCall.Client.ViewModels;

public sealed class RemoteParticipantViewModel : ViewModelBase
{
    private BitmapSource? _video;
    private bool _isCameraOn = true;
    private bool _isMuted;

    public string Username { get; }
    public BitmapSource? Video { get => _video; set => SetField(ref _video, value); }
    public bool IsCameraOn { get => _isCameraOn; set => SetField(ref _isCameraOn, value); }
    public bool IsMuted { get => _isMuted; set => SetField(ref _isMuted, value); }

    public RemoteParticipantViewModel(string username) => Username = username;
}
