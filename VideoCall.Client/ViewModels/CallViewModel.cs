using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VideoCall.Client.Media;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Client.ViewModels;

public enum CallUiState
{
    Calling,
    Ringing,
    Connected,
    Ended
}


public class CallViewModel : ViewModelBase, IDisposable
{
    private readonly NetworkClient _network;
    private readonly string _serverAddress;
    private readonly string _otherParty;
    private readonly bool _isOutgoing;

    private Guid _callId;
    private CallUiState _state;
    private string _stateText = string.Empty;
    private bool _isMuted;
    private bool _isCameraOn = true;
    private BitmapSource? _localVideo;
    private BitmapSource? _remoteVideo;

    private UdpMediaClient? _udpMedia;
    private AudioCaptureService? _audioCapture;
    private AudioPlaybackService? _audioPlayback;
    private VideoCaptureService? _videoCapture;
    private readonly VideoFrameReassembler _remoteFrameReassembler = new();

    public string OtherParty => _otherParty;

    public CallUiState State
    {
        get => _state;
        private set => SetField(ref _state, value);
    }

    public string StateText
    {
        get => _stateText;
        private set => SetField(ref _stateText, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => SetField(ref _isMuted, value);
    }

    public bool IsCameraOn
    {
        get => _isCameraOn;
        private set => SetField(ref _isCameraOn, value);
    }

    public BitmapSource? LocalVideo
    {
        get => _localVideo;
        private set => SetField(ref _localVideo, value);
    }

    public BitmapSource? RemoteVideo
    {
        get => _remoteVideo;
        private set => SetField(ref _remoteVideo, value);
    }

    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleCameraCommand { get; }
    public ICommand EndCallCommand { get; }

    public event Action? CallClosed;

    /// <summary>Constructor for an outgoing call (we are the caller).</summary>
    /// <summary>Constructor for an outgoing call (we are the caller).</summary>
    public CallViewModel(NetworkClient network, string serverAddress, string callee)
    {
        _network = network;
        _serverAddress = serverAddress;
        _otherParty = callee;
        _isOutgoing = true;
        State = CallUiState.Calling;
        StateText = "جاري الاتصال...";
        Subscribe();
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        ToggleCameraCommand = new RelayCommand(_ => ToggleCamera());
        EndCallCommand = new RelayCommand(async _ => await EndCallAsync());

        // إرسال طلب الاتصال عبر TCP
        _ = _network.RequestCallAsync(callee);
    }
    /// <summary>Constructor for an incoming call that was just accepted (we are the callee).</summary>
    public CallViewModel(NetworkClient network, string serverAddress, string caller, Guid callId)
    {
        _network = network;
        _serverAddress = serverAddress;
        _otherParty = caller;
        _isOutgoing = false;
        _callId = callId;
        State = CallUiState.Connected;
        StateText = "متصل";

        Subscribe();
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        ToggleCameraCommand = new RelayCommand(_ => ToggleCamera());
        EndCallCommand = new RelayCommand(async _ => await EndCallAsync());

        StartMediaPipeline();
    }

    private void Subscribe()
    {
        _network.CallAccepted += OnCallAccepted;
        _network.CallRejected += OnCallRejected;
        _network.CallEnded += OnCallEnded;
        _network.CallTimedOut += OnCallTimedOut;
        _network.Disconnected += OnDisconnected;
    }

    private void Unsubscribe()
    {
        _network.CallAccepted -= OnCallAccepted;
        _network.CallRejected -= OnCallRejected;
        _network.CallEnded -= OnCallEnded;
        _network.CallTimedOut -= OnCallTimedOut;
        _network.Disconnected -= OnDisconnected;
    }

    private void OnCallAccepted(CallAcceptedPayload payload)
    {
        if (!_isOutgoing || !string.Equals(payload.Callee, _otherParty, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _callId = payload.CallId;
        State = CallUiState.Connected;
        StateText = "متصل";
        StartMediaPipeline();
    }

    private void OnCallRejected(CallRejectedPayload payload)
    {
        if (!_isOutgoing || !string.Equals(payload.Callee, _otherParty, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        State = CallUiState.Ended;
        StateText = "تم رفض المكالمة";
        Cleanup();
        CallClosed?.Invoke();
    }

    private void OnCallEnded(CallEndedPayload payload)
    {
        if (_callId != Guid.Empty && payload.CallId != _callId)
        {
            return;
        }

        State = CallUiState.Ended;
        StateText = "تم إنهاء المكالمة";
        Cleanup();
        CallClosed?.Invoke();
    }

    private void OnCallTimedOut(CallTimedOutPayload payload)
    {
        if (_callId != Guid.Empty && payload.CallId != _callId)
        {
            return;
        }

        State = CallUiState.Ended;
        StateText = "تم إنهاء المكالمة";
        Cleanup();
        CallClosed?.Invoke();
    }

    private void OnDisconnected()
    {
        State = CallUiState.Ended;
        StateText = "تعذر الاتصال بالخادم";
        Cleanup();
        CallClosed?.Invoke();
    }

    private void StartMediaPipeline()
    {
        if (_network.SessionToken is not { } token)
        {
            return;
        }

        _udpMedia = new UdpMediaClient(_serverAddress, token, _callId);
        _udpMedia.AudioPacketReceived += OnRemoteAudioPacket;
        _udpMedia.VideoPacketReceived += OnRemoteVideoPacket;
        _udpMedia.Start();

        _audioPlayback = new AudioPlaybackService();

        _audioCapture = new AudioCaptureService();
        _audioCapture.ChunkCaptured += chunk => _ = _udpMedia?.SendAudioAsync(chunk);
        _audioCapture.Start();

        try
        {
            _videoCapture = new VideoCaptureService();
            _videoCapture.FrameCaptured += OnLocalFrameCaptured;
            _videoCapture.Start();
        }
        catch (InvalidOperationException)
        {
            StateText = "الكاميرا غير متاحة";
            IsCameraOn = false;
        }
    }
    private void OnLocalFrameCaptured(byte[] jpegBytes, OpenCvSharp.Mat previewMat)
    {
        // إرسال الفيديو عبر UDP
        _ = _udpMedia?.SendVideoFrameAsync(jpegBytes);

        // عرض المعاينة المحلية
        RunOnUi(() =>
        {
            try
            {
                LocalVideo = FrameCodec.MatToBitmapSource(previewMat);
            }
            catch (Exception)
            {
                // Skip corrupt preview frame
            }
        });
    }
    private void OnRemoteAudioPacket(MediaPacket packet)
    {
        _audioPlayback?.EnqueueChunk(packet.Payload);
    }

    private void OnRemoteVideoPacket(MediaPacket packet)
    {
        var completeFrame = _remoteFrameReassembler.Accept(packet);

        if (completeFrame is null)
        {
            return;
        }

        RunOnUi(() =>
        {
            try
            {
                RemoteVideo = FrameCodec.JpegBytesToBitmapSource(completeFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to decode remote video frame: {ex.Message}");
            }
        });
    }
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        if (_audioCapture is not null)
        {
            _audioCapture.IsMuted = IsMuted;
        }
    }

    private void ToggleCamera()
    {
        IsCameraOn = !IsCameraOn;
        _videoCapture?.SetCameraOn(IsCameraOn);
        if (!IsCameraOn)
        {
            LocalVideo = null;
        }
    }

    private async Task EndCallAsync()
    {
        if (_callId != Guid.Empty)
        {
            await _network.EndCallAsync(_callId);
        }

        State = CallUiState.Ended;
        StateText = "تم إنهاء المكالمة";
        Cleanup();
        CallClosed?.Invoke();
    }

    private static void RunOnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private bool _cleanedUp;

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        _videoCapture?.Dispose();
        _audioCapture?.Dispose();
        _audioPlayback?.Dispose();
        _udpMedia?.Dispose();
    }

    public void Dispose()
    {
        Unsubscribe();
        Cleanup();
    }
}
