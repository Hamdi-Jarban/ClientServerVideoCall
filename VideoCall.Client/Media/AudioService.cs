using NAudio.Wave;

namespace VideoCall.Client.Media;

/// <summary>
/// Captures real microphone audio via NAudio's WaveInEvent and raises one
/// event per captured chunk (raw 16-bit PCM, mono, 16kHz - small and
/// simple enough to send as a single UDP packet without fragmentation,
/// and standard enough for NAudio's playback side to consume directly).
/// Muting is implemented by simply not forwarding captured chunks,
/// rather than stopping capture, so mute/unmute is instant.
/// </summary>
public class AudioCaptureService : IDisposable
{
    public static readonly WaveFormat Format = new(16000, 16, 1);

    private WaveInEvent? _waveIn;
    public bool IsMuted { get; set; }

    public event Action<byte[]>? ChunkCaptured;

    public void Start()
    {
        if (_waveIn is not null) return;

        _waveIn = new WaveInEvent
        {
            WaveFormat = Format,
            BufferMilliseconds = 40
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (IsMuted || e.BytesRecorded == 0)
        {
            return;
        }

        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
        ChunkCaptured?.Invoke(chunk);
    }

    public void Stop()
    {
        if (_waveIn is null) return;
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Plays back received PCM chunks via NAudio's WaveOutEvent, using a
/// BufferedWaveProvider as a small jitter buffer so slight arrival-time
/// variance between UDP packets doesn't cause audible clicking.
/// </summary>
public class AudioPlaybackService : IDisposable
{
    private readonly WaveOutEvent _waveOut = new();
    private readonly BufferedWaveProvider _buffer;

    public AudioPlaybackService()
    {
        _buffer = new BufferedWaveProvider(AudioCaptureService.Format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };
        _waveOut.Init(_buffer);
        _waveOut.Play();
    }

    public void EnqueueChunk(byte[] pcmChunk)
    {
        _buffer.AddSamples(pcmChunk, 0, pcmChunk.Length);
    }

    public void Dispose()
    {
        _waveOut.Stop();
        _waveOut.Dispose();
    }
}
