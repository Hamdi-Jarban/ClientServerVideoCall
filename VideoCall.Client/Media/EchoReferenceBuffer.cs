namespace VideoCall.Client.Media;

/// <summary>
/// A small ring buffer of recently played-back (far-end) PCM samples.
/// The acoustic echo canceller uses this as the "reference signal": the
/// audio that came out of the speakers a moment ago, which the microphone
/// may be re-capturing as echo. Independent from NAudio's own playback
/// buffer because that one is consumed/drained during playback and can't
/// be read back after the fact.
/// </summary>
public sealed class EchoReferenceBuffer
{
    private readonly object _gate = new();
    private readonly short[] _buffer;
    private int _writePos;
    private int _count;

    public EchoReferenceBuffer(int sampleRate, int milliseconds)
    {
        _buffer = new short[Math.Max(1, sampleRate * milliseconds / 1000)];
    }

    /// <summary>Appends newly played-back 16-bit mono PCM bytes.</summary>
    public void Write(byte[] pcmBytes)
    {
        if (pcmBytes is null || pcmBytes.Length < 2) return;
        var sampleCount = pcmBytes.Length / 2;
        lock (_gate)
        {
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
                _buffer[_writePos] = sample;
                _writePos = (_writePos + 1) % _buffer.Length;
                if (_count < _buffer.Length) _count++;
            }
        }
    }

    /// <summary>
    /// Returns the most recent <paramref name="count"/> samples, oldest first.
    /// Positions with no history yet are returned as silence (0), which keeps
    /// the echo canceller well-defined right after a call starts.
    /// </summary>
    public short[] ReadRecent(int count)
    {
        var result = new short[count];
        lock (_gate)
        {
            var available = Math.Min(count, Math.Min(_count, _buffer.Length));
            if (available <= 0) return result;
            var start = (_writePos - available + _buffer.Length) % _buffer.Length;
            for (var i = 0; i < available; i++)
                result[count - available + i] = _buffer[(start + i) % _buffer.Length];
        }
        return result;
    }
}
