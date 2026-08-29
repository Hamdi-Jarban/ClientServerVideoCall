using OpenCvSharp;

namespace VideoCall.Client.Media;

public sealed class VideoCaptureService : IAsyncDisposable, IDisposable
{
    private const int TargetFps = 15;
    private const int JpegQuality = 60;
    private readonly object _gate = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _disposed;

    public bool IsCameraOn { get; private set; }
    public event Action<byte[], byte[]>? FrameCaptured;
    public event Action<Exception>? CaptureError;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        lock (_gate)
        {
            if (_capture is not null) return;
            var capture = new VideoCapture(0);
            if (!capture.IsOpened())
            {
                capture.Dispose();
                throw new InvalidOperationException("CAMERA_UNAVAILABLE");
            }
            _capture = capture;
            _cts = new CancellationTokenSource();
            IsCameraOn = true;
            _loop = CaptureLoopAsync(_cts.Token);
        }
    }

    public void SetCameraOn(bool enabled) => IsCameraOn = enabled;

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        using var frame = new Mat();
        var delay = TimeSpan.FromMilliseconds(1000.0 / TargetFps);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                VideoCapture? capture;
                lock (_gate) capture = _capture;
                if (capture is null) break;
                if (!IsCameraOn)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                    continue;
                }

                if (capture.Read(frame) && !frame.Empty())
                {
                    Cv2.ImEncode(".jpg", frame, out var encoded,
                        new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
                    Cv2.ImEncode(".bmp", frame, out var preview);
                    FrameCaptured?.Invoke(encoded, preview);
                }
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { CaptureError?.Invoke(ex); }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        VideoCapture? capture;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            capture = _capture;
            _cts = null;
            _loop = null;
            _capture = null;
            IsCameraOn = false;
        }
        cts?.Cancel();
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        capture?.Release();
        capture?.Dispose();
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync().ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
