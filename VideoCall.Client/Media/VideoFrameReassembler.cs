using VideoCall.Shared.Networking;

namespace VideoCall.Client.Media;

/// <summary>
/// Reassembles a sequence of MediaPacket fragments (same SequenceNumber,
/// FragmentIndex/FragmentCount) back into one complete encoded video
/// frame. Frames are received out of order or partially lost over UDP,
/// so: fragments for a sequence number that never fully arrives are
/// simply discarded once a newer sequence number starts arriving
/// (better to skip a frame than to wait forever / show a corrupt one).
/// </summary>
public class VideoFrameReassembler
{
    private uint? _currentSequence;
    private byte[]?[] _fragments = Array.Empty<byte[]?>();
    private int _receivedCount;

    /// <summary>Returns the complete frame bytes once all fragments for a sequence number have arrived, otherwise null.</summary>
    public byte[]? Accept(MediaPacket packet)
    {
        if (_currentSequence != packet.SequenceNumber)
        {
            // A new frame started - drop whatever partial data we had for the previous one.
            _currentSequence = packet.SequenceNumber;
            _fragments = new byte[packet.FragmentCount][];
            _receivedCount = 0;
        }

        if (packet.FragmentIndex >= _fragments.Length || _fragments[packet.FragmentIndex] is not null)
        {
            return null; // out-of-range or duplicate fragment
        }

        _fragments[packet.FragmentIndex] = packet.Payload;
        _receivedCount++;

        if (_receivedCount < _fragments.Length)
        {
            return null; // still waiting on more fragments
        }

        int totalLength = _fragments.Sum(f => f!.Length);
        var frame = new byte[totalLength];
        int offset = 0;
        foreach (var fragment in _fragments)
        {
            Buffer.BlockCopy(fragment!, 0, frame, offset, fragment!.Length);
            offset += fragment.Length;
        }

        return frame;
    }
}
