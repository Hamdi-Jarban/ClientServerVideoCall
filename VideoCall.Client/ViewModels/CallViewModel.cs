using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VideoCall.Client.Media;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Client.ViewModels;

public sealed class CallViewModel : ViewModelBase, IDisposable
{
    private readonly NetworkClient _network;
    private readonly string _serverHost;
    private readonly Guid _callId;
    private readonly CancellationTokenSource _stop = new();
    private readonly VideoFrameReassembler _reassembler = new();
    private readonly AudioChunkReassembler _audioReassembler = new();
    private readonly ConcurrentBag<Task> _sendTasks = new();
    private UdpMediaClient? _udp;
    private AudioCaptureService? _audioCapture;
    private AudioPlaybackService? _audioPlayback;
    private VideoCaptureService? _videoCapture;
    private BitmapSource? _localVideo;
    private BitmapSource? _remoteVideo;
    private bool _isMuted;
    private bool _isCameraOn = true;
    private string _stateText = "جارٍ الاتصال...";
    private int _mediaStarted;
    private int _closed;

    public string OtherParty { get; }
    public BitmapSource? LocalVideo { get => _localVideo; private set => SetField(ref _localVideo, value); }
    public BitmapSource? RemoteVideo { get => _remoteVideo; private set => SetField(ref _remoteVideo, value); }
    public bool IsMuted { get => _isMuted; private set => SetField(ref _isMuted, value); }
    public bool IsCameraOn { get => _isCameraOn; private set => SetField(ref _isCameraOn, value); }
    public string StateText { get => _stateText; private set => SetField(ref _stateText, value); }

    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleCameraCommand { get; }
    public ICommand EndCallCommand { get; }
    public event Action? CallClosed;

    public CallViewModel(NetworkClient network, string serverHost, string otherParty, Guid callId)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _callId = callId;
        OtherParty = otherParty?.Trim() ?? throw new ArgumentNullException(nameof(otherParty));
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        ToggleCameraCommand = new RelayCommand(ToggleCamera);
        EndCallCommand = new AsyncCommand(EndCallAsync);
        _network.CallAccepted += OnCallAccepted;
        _network.CallEnded += OnCallEnded;
        _network.RoomMediaStarted += OnMediaStarted;
        _network.Disconnected += OnDisconnected;
    }

    private void OnCallAccepted(CallAcceptedPayload payload)
    {
        if (payload.CallId != _callId) return;
        RunOnUi(() => StateText = "تم قبول الاتصال، جاري تشغيل الوسائط...");
    }

    private void OnMediaStarted(RoomMediaPayload payload)
    {
        if (!string.Equals(payload.RoomId, _callId.ToString("N"), StringComparison.OrdinalIgnoreCase)) return;
        if (payload.MediaId == Guid.Empty || Interlocked.Exchange(ref _mediaStarted, 1) != 0) return;
        RunOnUi(() => StateText = "متصل");
        StartMediaPipeline(payload.MediaId);
    }

    private void StartMediaPipeline(Guid mediaId)
    {
        if (_network.SessionToken is not { } token || string.IsNullOrWhiteSpace(_network.Username))
        {
            RunOnUi(() => StateText = "جلسة المستخدم غير صالحة");
            return;
        }

        try
        {
            _udp = new UdpMediaClient(_serverHost, token, mediaId, _network.Username);
            _udp.AudioPacketReceived += packet =>
            {
                var complete = _audioReassembler.Accept(packet);
                if (complete is not null) _audioPlayback?.Enqueue(complete);
            };
            _udp.VideoPacketReceived += OnRemoteVideo;
            _udp.TransportError += ex => RunOnUi(() => StateText = $"خطأ UDP: {ex.Message}");
            _udp.Start();

            _audioPlayback = new AudioPlaybackService();
            // Pass the playback's reference buffer in so captured mic audio
            // has the speaker's own output subtracted out (echo cancellation).
            _audioCapture = new AudioCaptureService(_audioPlayback.EchoReference) { IsMuted = IsMuted };
            _audioCapture.ChunkCaptured += chunk => TrackSend(_udp.SendAudioAsync(chunk, _stop.Token));
            _audioCapture.Start();

            _videoCapture = new VideoCaptureService();
            _videoCapture.FrameCaptured += OnLocalFrame;
            _videoCapture.Start();
        }
        catch (Exception ex)
        {
            RunOnUi(() => StateText = $"تعذر تشغيل الكاميرا/الصوت: {ex.Message}");
        }
    }

    private void OnLocalFrame(byte[] encodedFrame, byte[] preview)
    {
        if (!IsCameraOn) return;
        RunOnUi(() =>
        {
            try { LocalVideo = FrameCodec.BytesToBitmapSource(preview); }
            catch { }
        });
        if (_udp is not null) TrackSend(_udp.SendVideoFrameAsync(encodedFrame, _stop.Token));
    }

    private void OnRemoteVideo(MediaPacket packet)
    {
        var complete = _reassembler.Accept(packet);
        if (complete is null) return;
        RunOnUi(() =>
        {
            try { RemoteVideo = FrameCodec.BytesToBitmapSource(complete); }
            catch { }
        });
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        if (_audioCapture is not null) _audioCapture.IsMuted = IsMuted;
    }

    private void ToggleCamera()
    {
        IsCameraOn = !IsCameraOn;
        _videoCapture?.SetCameraOn(IsCameraOn);
        if (!IsCameraOn) LocalVideo = null;
    }

    private void OnCallEnded(CallEndedPayload payload)
    {
        if (payload.CallId != _callId) return;
        RunOnUi(() => _ = CloseAsync("انتهت المكالمة"));
    }

    private void OnDisconnected() => RunOnUi(() => _ = CloseAsync("انقطع الاتصال بالخادم"));

    private async Task EndCallAsync()
    {
        if (_callId != Guid.Empty)
        {
            try { await _network.EndCallAsync(_callId).ConfigureAwait(false); } catch { }
        }
        await CloseAsync("انتهت المكالمة").ConfigureAwait(false);
    }

    private async Task CloseAsync(string message)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        RunOnUi(() => StateText = message);
        _stop.Cancel();
        // Stop producers first; otherwise a capture callback may enqueue a new
        // send while the shutdown code is waiting for the old sends.
        _videoCapture?.Dispose();
        _audioCapture?.Dispose();
        try { await Task.WhenAll(_sendTasks.ToArray()).ConfigureAwait(false); } catch (OperationCanceledException) { }
        _audioPlayback?.Dispose();
        _udp?.Dispose();
        _network.CallAccepted -= OnCallAccepted;
        _network.CallEnded -= OnCallEnded;
        _network.RoomMediaStarted -= OnMediaStarted;
        _network.Disconnected -= OnDisconnected;
        CallClosed?.Invoke();
    }

    private void TrackSend(Task task) => _sendTasks.Add(task);

    // Safe to block on: every awaited step in CloseAsync uses ConfigureAwait(false),
    // so its continuations run on the thread pool and never need to resume on this
    // (UI) thread. That's what avoids the classic WPF dispatcher deadlock.
    public void Dispose() => CloseAsync("تم إغلاق المكالمة").GetAwaiter().GetResult();

    public Task DisposeAsync() => CloseAsync("تم إغلاق المكالمة");

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
