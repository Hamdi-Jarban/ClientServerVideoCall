using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VideoCall.Client.Media;
using VideoCall.Client.Services;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Client.ViewModels;

public sealed class GroupCallViewModel : ViewModelBase, IDisposable
{
    private readonly NetworkClient _network;
    private readonly string _serverHost;
    private readonly string _roomId;
    private readonly Guid _mediaId;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _videoQueue = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly ObservableCollection<RemoteParticipantViewModel> _participants = new();
    private readonly Dictionary<string, VideoFrameReassembler> _reassemblers = new(StringComparer.OrdinalIgnoreCase);
    private readonly AudioChunkReassembler _audioReassembler = new();

    private UdpMediaClient? _udp;
    private AudioCaptureService? _audioCapture;
    private AudioPlaybackService? _audioPlayback;
    private VideoCaptureService? _videoCapture;
    private Task? _videoSender;
    private BitmapSource? _localVideo;
    private bool _muted;
    private bool _cameraOn = true;
    private string _statusMessage = "جاري تهيئة المحادثة...";
    private int _closed;

    public string RoomId => _roomId;
    public Guid MediaId => _mediaId;
    public BitmapSource? LocalVideo { get => _localVideo; private set => SetField(ref _localVideo, value); }
    public bool IsMuted { get => _muted; private set => SetField(ref _muted, value); }
    public bool IsCameraOn { get => _cameraOn; private set => SetField(ref _cameraOn, value); }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public ObservableCollection<RemoteParticipantViewModel> Participants => _participants;

    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleCameraCommand { get; }
    public ICommand LeaveCommand { get; }
    public event Action? Closed;

    public GroupCallViewModel(
        NetworkClient network,
        string serverHost,
        string roomId,
        Guid mediaId,
        IEnumerable<string> members)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _serverHost = serverHost;
        _roomId = roomId;
        _mediaId = mediaId;

        foreach (var member in members.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!member.Equals(_network.Username, StringComparison.OrdinalIgnoreCase))
                AddParticipant(member);
        }

        _network.RoomMediaStopped += OnRoomMediaStopped;
        _network.Disconnected += OnDisconnected;
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        ToggleCameraCommand = new RelayCommand(ToggleCamera);
        LeaveCommand = new AsyncCommand(LeaveAsync);
        StartMediaPipeline();
    }

    private void StartMediaPipeline()
    {
        if (_network.SessionToken is not { } token || string.IsNullOrWhiteSpace(_network.Username))
        {
            StatusMessage = "جلسة المستخدم غير صالحة.";
            return;
        }

        _udp = new UdpMediaClient(_serverHost, token, _mediaId, _network.Username);
        _udp.AudioPacketReceived += packet =>
        {
            var complete = _audioReassembler.Accept(packet);
            if (complete is not null) _audioPlayback?.Enqueue(complete);
        };
        _udp.VideoPacketReceived += OnRemoteVideo;
        _udp.TransportError += ex => RunOnUi(() => StatusMessage = $"خطأ UDP: {ex.Message}");
        _udp.Start();

        _audioPlayback = new AudioPlaybackService();
        // Pass the playback's reference buffer in so captured mic audio has
        // the speaker's own output subtracted out (echo cancellation).
        _audioCapture = new AudioCaptureService(_audioPlayback.EchoReference);
        _audioCapture.ChunkCaptured += chunk => _ = _udp.SendAudioAsync(chunk);
        _audioCapture.Start();

        try
        {
            _videoCapture = new VideoCaptureService();
            _videoCapture.FrameCaptured += OnLocalFrame;
            _videoCapture.Start();
            _videoSender = SendVideoLoopAsync(_stop.Token);
            StatusMessage = "المحادثة الجماعية متصلة.";
        }
        catch (InvalidOperationException)
        {
            IsCameraOn = false;
            StatusMessage = "الصوت متصل، لكن الكاميرا غير متاحة.";
        }
    }

    private void OnLocalFrame(byte[] encodedFrame, byte[] preview)
    {
        _videoQueue.Writer.TryWrite(encodedFrame);
        RunOnUi(() =>
        {
            try { LocalVideo = FrameCodec.BytesToBitmapSource(preview); }
            catch { }
        });
    }

    private async Task SendVideoLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _videoQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (_udp is not null) await _udp.SendVideoFrameAsync(frame, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void OnRemoteVideo(MediaPacket packet)
    {
        if (!_reassemblers.TryGetValue(packet.SenderUsername, out var reassembler))
        {
            reassembler = new VideoFrameReassembler();
            _reassemblers[packet.SenderUsername] = reassembler;
        }

        var complete = reassembler.Accept(packet);
        if (complete is null) return;
        RunOnUi(() =>
        {
            try
            {
                var participant = AddParticipant(packet.SenderUsername);
                participant.Video = FrameCodec.BytesToBitmapSource(complete);
                participant.IsCameraOn = true;
            }
            catch { }
        });
    }

    private RemoteParticipantViewModel AddParticipant(string username)
    {
        var existing = _participants.FirstOrDefault(x =>
            x.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var participant = new RemoteParticipantViewModel(username);
        _participants.Add(participant);
        return participant;
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

    private void OnRoomMediaStopped(RoomMediaPayload payload)
    {
        if (!payload.RoomId.Equals(_roomId, StringComparison.OrdinalIgnoreCase)) return;
        RunOnUi(() => _ = CloseAsync("تم إيقاف المحادثة من المضيف."));
    }

    private void OnDisconnected() => RunOnUi(() => _ = CloseAsync("انقطع الاتصال بالخادم."));

    private async Task LeaveAsync() => await CloseAsync("تمت مغادرة المحادثة.");

    private async Task CloseAsync(string message)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        StatusMessage = message;
        _stop.Cancel();
        _videoQueue.Writer.TryComplete();
        // Stop all producers before waiting for their pending sends.
        _videoCapture?.Dispose();
        _audioCapture?.Dispose();
        if (_videoSender is not null)
        {
            try { await _videoSender.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        try { await _network.LeaveRoomAsync(_roomId).ConfigureAwait(false); } catch { }

        _audioPlayback?.Dispose();
        _udp?.Dispose();

        _network.RoomMediaStopped -= OnRoomMediaStopped;
        _network.Disconnected -= OnDisconnected;
        Closed?.Invoke();
    }

    // Safe to block on: every awaited step above uses ConfigureAwait(false), so the
    // continuations never need this (UI) thread to resume — that's what prevents the
    // classic WPF dispatcher deadlock when this runs from a window's Closed handler.
    public void Dispose() => CloseAsync("تم إغلاق نافذة المحادثة.").GetAwaiter().GetResult();

    public Task DisposeAsync() => CloseAsync("تم إغلاق نافذة المحادثة.");

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
