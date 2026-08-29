using NAudio.Wave;

namespace VideoCall.Client.Media;

public sealed class AudioCaptureService : IDisposable
{
    public static readonly WaveFormat Format = new(16000, 16, 1);
    private readonly EchoReferenceBuffer? _echoReference;
    private readonly AcousticEchoCanceller? _aec;
    private WaveInEvent? _input;
    private int _disposed;

    public bool IsMuted { get; set; }
    public event Action<byte[]>? ChunkCaptured;

    /// <param name="echoReference">
    /// The far-end reference buffer from the <see cref="AudioPlaybackService"/>
    /// used in this same call, if echo cancellation should be applied. Pass
    /// null to capture raw, unprocessed audio (e.g. when the caller already
    /// knows a headset is in use and echo isn't a concern).
    /// </param>
    public AudioCaptureService(EchoReferenceBuffer? echoReference = null)
    {
        _echoReference = echoReference;
        if (echoReference is not null) _aec = new AcousticEchoCanceller();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_input is not null) return;

        _input = new WaveInEvent
        {
            WaveFormat = Format,
            BufferMilliseconds = 40
        };
        _input.DataAvailable += OnDataAvailable;
        _input.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (IsMuted || e.BytesRecorded <= 0) return;
        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);

        if (_aec is not null && _echoReference is not null)
            chunk = CancelEcho(chunk);

        ChunkCaptured?.Invoke(chunk);
    }

    private byte[] CancelEcho(byte[] pcmBytes)
    {
        var sampleCount = pcmBytes.Length / 2;
        if (sampleCount == 0) return pcmBytes;

        var micSamples = new short[sampleCount];
        for (var i = 0; i < sampleCount; i++)
            micSamples[i] = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));

        var history = _echoReference!.ReadRecent(sampleCount + _aec!.FilterLength - 1);
        var cleaned = _aec.Process(micSamples, history);

        var result = new byte[pcmBytes.Length];
        for (var i = 0; i < sampleCount; i++)
        {
            result[i * 2] = (byte)(cleaned[i] & 0xFF);
            result[i * 2 + 1] = (byte)((cleaned[i] >> 8) & 0xFF);
        }
        return result;
    }

    public void Stop()
    {
        var input = Interlocked.Exchange(ref _input, null);
        if (input is null) return;
        input.DataAvailable -= OnDataAvailable;
        try { input.StopRecording(); } catch { }
        input.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
    }
}

public sealed class AudioPlaybackService : IDisposable
{
    private readonly WaveOutEvent _output = new();
    private readonly BufferedWaveProvider _buffer;
    private int _disposed;

    /// <summary>
    /// Rolling history of what was just played through the speakers. Pass
    /// this into an <see cref="AudioCaptureService"/> for the same call so
    /// it can subtract the speaker's leak-back out of the microphone signal
    /// (acoustic echo cancellation).
    /// </summary>
    public EchoReferenceBuffer EchoReference { get; } = new(sampleRate: 16000, milliseconds: 500);

    public AudioPlaybackService()
    {
        _buffer = new BufferedWaveProvider(AudioCaptureService.Format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(800),
            DiscardOnBufferOverflow = true
        };
        _output.Init(_buffer);
        _output.Play();
    }

    public void Enqueue(byte[] pcm)
    {
        if (Volatile.Read(ref _disposed) != 0 || pcm is null || pcm.Length == 0) return;
        _buffer.AddSamples(pcm, 0, pcm.Length);
        EchoReference.Write(pcm);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _output.Stop();
        _output.Dispose();
    }
}
