using System.Text;

namespace VideoCall.Shared.Networking;

public enum MediaType : byte
{
    Audio = 1,
    Video = 2,
    Handshake = 3
}

public class MediaPacket
{
    public const int BaseHeaderSize = 16 + 16 + 1 + 4 + 8 + 2 + 2 + 2 + 4;
    public const int MaxSafeUdpPayload = 1200;

    public Guid SessionToken { get; init; }
    public Guid CallId { get; init; }
    public string SenderUsername { get; init; } = string.Empty;
    public MediaType MediaType { get; init; }
    public uint SequenceNumber { get; init; }
    public long TimestampTicks { get; init; }
    public ushort FragmentIndex { get; init; }
    public ushort FragmentCount { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        var usernameBytes = Encoding.UTF8.GetBytes(SenderUsername ?? string.Empty);
        if (usernameBytes.Length > ushort.MaxValue) throw new InvalidDataException("Sender username is too long.");
        if (Payload.Length > MaxSafeUdpPayload) throw new InvalidDataException("Payload exceeds the safe UDP size.");
        if (FragmentCount == 0 || FragmentIndex >= FragmentCount) throw new InvalidDataException("Invalid fragment metadata.");
        var totalHeaderSize = BaseHeaderSize + usernameBytes.Length;
        var buffer = new byte[totalHeaderSize + Payload.Length];
        int offset = 0;

        WriteGuid(buffer, ref offset, SessionToken);
        WriteGuid(buffer, ref offset, CallId);
        buffer[offset++] = (byte)MediaType;
        WriteUInt32BE(buffer, ref offset, SequenceNumber);
        WriteInt64BE(buffer, ref offset, TimestampTicks);
        WriteUInt16BE(buffer, ref offset, FragmentIndex);
        WriteUInt16BE(buffer, ref offset, FragmentCount);

        // ﬂ «»… ÿÊ· «”„ «·„” Œœ„ À„ «·«”„ ﬂ‹ UTF-8
        WriteUInt16BE(buffer, ref offset, (ushort)usernameBytes.Length);
        Buffer.BlockCopy(usernameBytes, 0, buffer, offset, usernameBytes.Length);
        offset += usernameBytes.Length;

        // ﬂ «»… ÿÊ· «·Õ„Ê·… (Payload) À„ «·Õ„Ê·… ‰›”Â«
        WriteInt32BE(buffer, ref offset, Payload.Length);
        Buffer.BlockCopy(Payload, 0, buffer, offset, Payload.Length);

        return buffer;
    }

    public static MediaPacket? TryDeserialize(byte[] data, int length)
    {
        if (length < BaseHeaderSize)
        {
            return null;
        }

        int offset = 0;
        var sessionToken = ReadGuid(data, ref offset);
        var callId = ReadGuid(data, ref offset);
        var mediaType = (MediaType)data[offset++];
        if (mediaType is not (MediaType.Audio or MediaType.Video or MediaType.Handshake)) return null;
        var seq = ReadUInt32BE(data, ref offset);
        var ts = ReadInt64BE(data, ref offset);
        var fragIndex = ReadUInt16BE(data, ref offset);
        var fragCount = ReadUInt16BE(data, ref offset);

        // ﬁ—«¡… «”„ «·„” Œœ„
        var usernameLen = ReadUInt16BE(data, ref offset);
        if (offset + usernameLen > length) return null;

        var senderUsername = Encoding.UTF8.GetString(data, offset, usernameLen);
        offset += usernameLen;

        // ﬁ—«¡… «·Õ„Ê·…
        if (offset + 4 > length) return null;
        var payloadLength = ReadInt32BE(data, ref offset);

        if (payloadLength < 0 || payloadLength > MaxSafeUdpPayload || offset + payloadLength > length ||
            fragCount == 0 || fragIndex >= fragCount)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        Buffer.BlockCopy(data, offset, payload, 0, payloadLength);

        return new MediaPacket
        {
            SessionToken = sessionToken,
            CallId = callId,
            SenderUsername = senderUsername,
            MediaType = mediaType,
            SequenceNumber = seq,
            TimestampTicks = ts,
            FragmentIndex = fragIndex,
            FragmentCount = fragCount,
            Payload = payload
        };
    }

    private static void WriteGuid(byte[] buffer, ref int offset, Guid value)
    {
        value.TryWriteBytes(buffer.AsSpan(offset, 16));
        offset += 16;
    }

    private static Guid ReadGuid(byte[] buffer, ref int offset)
    {
        var g = new Guid(buffer.AsSpan(offset, 16));
        offset += 16;
        return g;
    }

    private static void WriteUInt32BE(byte[] buffer, ref int offset, uint value)
    {
        buffer[offset++] = (byte)(value >> 24);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    private static uint ReadUInt32BE(byte[] buffer, ref int offset)
    {
        uint value = (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
        offset += 4;
        return value;
    }

    private static void WriteInt32BE(byte[] buffer, ref int offset, int value) => WriteUInt32BE(buffer, ref offset, unchecked((uint)value));

    private static int ReadInt32BE(byte[] buffer, ref int offset) => unchecked((int)ReadUInt32BE(buffer, ref offset));

    private static void WriteInt64BE(byte[] buffer, ref int offset, long value)
    {
        for (int i = 7; i >= 0; i--)
        {
            buffer[offset++] = (byte)(value >> (i * 8));
        }
    }

    private static long ReadInt64BE(byte[] buffer, ref int offset)
    {
        long value = 0;
        for (int i = 0; i < 8; i++)
        {
            value = (value << 8) | buffer[offset++];
        }
        return value;
    }

    private static void WriteUInt16BE(byte[] buffer, ref int offset, ushort value)
    {
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    private static ushort ReadUInt16BE(byte[] buffer, ref int offset)
    {
        ushort value = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        offset += 2;
        return value;
    }
}