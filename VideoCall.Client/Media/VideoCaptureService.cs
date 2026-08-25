using OpenCvSharp;

namespace VideoCall.Client.Media;

public class VideoCaptureService : IDisposable
{
    private const int TargetFps = 15;
    private const int JpegQuality = 60;

    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public bool IsCameraOn { get; private set; }

    public event Action<byte[], Mat>? FrameCaptured;

    public void Start()
    {
        if (_capture is not null) return;

        _capture = new VideoCapture(0);
        if (!_capture.IsOpened())
        {
            throw new InvalidOperationException("CAMERA_UNAVAILABLE");
        }

        IsCameraOn = true;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
    }

    public void SetCameraOn(bool on) => IsCameraOn = on;

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        var frameIntervalMs = 1000 / TargetFps;
        using var frame = new Mat();

        while (!ct.IsCancellationRequested)
        {
            if (!IsCameraOn)
            {
                await Task.Delay(100, ct);
                continue;
            }

            if (_capture!.Read(frame) && !frame.Empty())
            {
                Cv2.ImEncode(".jpg", frame, out var jpegBytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
                using var previewCopy = frame.Clone();
                FrameCaptured?.Invoke(jpegBytes, previewCopy);
            }

            try
            {
                await Task.Delay(frameIntervalMs, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loopTask?.Wait(500); } catch {  }
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        IsCameraOn = false;
    }

    public void Dispose() => Stop();
}
