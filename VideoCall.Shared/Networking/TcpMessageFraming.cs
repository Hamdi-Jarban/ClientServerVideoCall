using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VideoCall.Shared.Messages;

namespace VideoCall.Shared.Networking;

/// <summary>
/// TCP is a byte stream, not a message protocol: a single ReadAsync() call
/// can return a partial message, several messages back to back, or
/// anything in between. Every message is therefore framed on the wire as:
///
///   [4-byte big-endian length prefix] [UTF-8 JSON payload of that length]
///
/// This class is the single place that implements that framing, so
/// Server and Client both read/write messages the same way instead of
/// duplicating the logic.
/// </summary>
public class TcpMessageReaderWriter
{
    private const int MaxMessageSizeBytes = 10 * 1024 * 1024; // 10 MB safety cap

    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public TcpMessageReaderWriter(NetworkStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Reads exactly one complete framed message from the stream, handling
    /// partial reads transparently. Returns null if the remote side closed
    /// the connection cleanly while we were waiting for the next message.
    /// </summary>
    public async Task<Message?> ReadMessageAsync(CancellationToken ct)
    {
        var lengthBuffer = await ReadExactAsync(4, ct);
        if (lengthBuffer is null)
        {
            return null; // graceful disconnect
        }

        int length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0 || length > MaxMessageSizeBytes)
        {
            throw new InvalidDataException($"Invalid message length received: {length}");
        }

        var payloadBuffer = await ReadExactAsync(length, ct);
        if (payloadBuffer is null)
        {
            return null; // remote closed mid-message
        }

        string json = Encoding.UTF8.GetString(payloadBuffer);
        var message = JsonSerializer.Deserialize<Message>(json);
        if (message is null)
        {
            throw new InvalidDataException("Received message could not be deserialized.");
        }

        return message;
    }

    /// <summary>
    /// Writes exactly one framed message. Guarded by a semaphore because
    /// multiple logical senders (e.g. relaying a broadcast while handling
    /// a direct reply) could otherwise interleave partial writes on the
    /// same NetworkStream.
    /// </summary>
    public async Task WriteMessageAsync(Message message, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(lengthPrefix, ct);
            await _stream.WriteAsync(payload, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes, looping over ReadAsync()
    /// as many times as needed since a single call may return fewer bytes
    /// than requested. Returns null only if the stream ended cleanly before
    /// any byte of a new message was read (graceful disconnect); throws if
    /// it ends mid-message (abrupt disconnect).
    /// </summary>
    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        int totalRead = 0;

        while (totalRead < count)
        {
            int read = await _stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0)
            {
                if (totalRead == 0)
                {
                    return null;
                }

                throw new IOException("Connection closed mid-message.");
            }

            totalRead += read;
        }

        return buffer;
    }
}
