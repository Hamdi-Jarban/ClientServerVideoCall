using VideoCall.Shared.Networking;

namespace VideoCall.Client.Media;

public sealed class AudioChunkReassembler
{
    private readonly object _gate = new();
    private uint? _sequence;
    private byte[]?[] _fragments = Array.Empty<byte[]?>();
    private int _received;

    public byte[]? Accept(MediaPacket packet)
    {
        if (packet.MediaType != MediaType.Audio || packet.FragmentCount == 0) return null;
        lock (_gate)
        {
            if (_sequence != packet.SequenceNumber || _fragments.Length != packet.FragmentCount)
            {
                _sequence = packet.SequenceNumber;
                _fragments = new byte[packet.FragmentCount][];
                _received = 0;
            }
            if (packet.FragmentIndex >= _fragments.Length || _fragments[packet.FragmentIndex] is not null) return null;
            _fragments[packet.FragmentIndex] = packet.Payload;
            if (++_received != _fragments.Length) return null;
            var result = new byte[_fragments.Sum(x => x?.Length ?? 0)];
            var offset = 0;
            foreach (var fragment in _fragments)
            {
                if (fragment is null) return null;
                Buffer.BlockCopy(fragment, 0, result, offset, fragment.Length);
                offset += fragment.Length;
            }
            _fragments = Array.Empty<byte[]?>();
            _received = 0;
            return result;
        }
    }
}
