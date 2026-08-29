using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VideoCall.Shared.Messages;

namespace VideoCall.Shared.Networking;

public sealed class TcpMessageReaderWriter
{
    private const int LengthPrefixBytes = 4;
    private const int MaxMessageSizeBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public TcpMessageReaderWriter(NetworkStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task<Message?> ReadMessageAsync(CancellationToken ct)
    {
        var prefix = await ReadExactAsync(LengthPrefixBytes, ct);
        if (prefix is null) return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length <= 0 || length > MaxMessageSizeBytes)
            throw new InvalidDataException($"Invalid message length: {length}.");

        var payload = await ReadExactAsync(length, ct);
        if (payload is null)
            throw new EndOfStreamException("Connection closed in the middle of a message.");

        try
        {
            var message = JsonSerializer.Deserialize<Message>(payload, JsonOptions);
            return message ?? throw new InvalidDataException("Message is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid JSON message.", ex);
        }
    }

    public async Task WriteMessageAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length == 0 || payload.Length > MaxMessageSizeBytes)
            throw new InvalidDataException("Message size is outside the allowed range.");

        var prefix = new byte[LengthPrefixBytes];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(prefix, ct);
            await _stream.WriteAsync(payload, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }


    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
            {
                if (offset == 0) return null;
                throw new EndOfStreamException("Connection closed in the middle of a frame.");
            }
            offset += read;
        }
        return buffer;
    }
}
