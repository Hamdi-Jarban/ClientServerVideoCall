namespace VideoCall.Client.Media;

/// <summary>
/// Software acoustic echo cancellation using a Normalized LMS adaptive
/// filter. It estimates how the far-end (played-back) signal leaks into the
/// microphone through the speaker-to-mic acoustic path, then subtracts that
/// estimate from the captured audio before it is sent to the other side.
///
/// This is a lightweight, pure-C# alternative to a full AEC stack (e.g. the
/// WebRTC audio processing module). It works well for a fixed, moderate
/// echo path (typical laptop speakers/mic on the same desk) but is not a
/// complete replacement for one: it does not do double-talk detection or
/// non-linear echo suppression. For a genuinely echo-free call — especially
/// when two clients run on the same machine for testing — a headset is
/// still the most reliable fix, since then there is no acoustic path for
/// the mic to pick up the speaker output at all.
/// </summary>
public sealed class AcousticEchoCanceller
{
    private readonly double[] _weights;
    private readonly double _stepSize;
    private const double Epsilon = 1e-6;

    public int FilterLength { get; }

    /// <param name="filterLengthSamples">
    /// How many taps (samples) of echo tail to model. At 16 kHz, 800 taps
    /// covers a 50 ms echo path, which is generous for desktop speaker/mic
    /// setups. Increase this if echo persists with a longer/louder room.
    /// </param>
    /// <param name="stepSize">
    /// NLMS adaptation rate (0 &lt; step &lt; 2). Lower values adapt more
    /// slowly but are more stable; 0.4-0.6 is a reasonable default.
    /// </param>
    public AcousticEchoCanceller(int filterLengthSamples = 800, double stepSize = 0.5)
    {
        FilterLength = filterLengthSamples;
        _weights = new double[filterLengthSamples];
        _stepSize = stepSize;
    }

    /// <summary>
    /// Removes the estimated echo from a block of captured microphone
    /// samples. <paramref name="farEndHistory"/> must contain at least
    /// <c>micSamples.Length + FilterLength - 1</c> samples of the most
    /// recently played-back audio, oldest first (see
    /// <see cref="EchoReferenceBuffer.ReadRecent"/>).
    /// </summary>
    public short[] Process(short[] micSamples, short[] farEndHistory)
    {
        var n = micSamples.Length;
        var output = new short[n];

        for (var i = 0; i < n; i++)
        {
            var baseIdx = i + FilterLength - 1;
            double estimatedEcho = 0.0;
            double energy = Epsilon;

            for (var k = 0; k < FilterLength; k++)
            {
                var x = farEndHistory[baseIdx - k];
                estimatedEcho += _weights[k] * x;
                energy += (double)x * x;
            }

            double micSample = micSamples[i];
            var error = micSample - estimatedEcho;
            var normalizedStep = _stepSize / energy;

            for (var k = 0; k < FilterLength; k++)
            {
                var x = farEndHistory[baseIdx - k];
                _weights[k] += normalizedStep * error * x;
            }

            output[i] = (short)Math.Clamp(error, short.MinValue, short.MaxValue);
        }

        return output;
    }
}
