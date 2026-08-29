using System.Net;
using System.Net.Sockets;
using System.IO;
using VideoCall.Shared.Networking;

namespace VideoCall.Client.Services;

/// <summary>
/// One UDP endpoint per active conversation. The same class supports a
/// private call and a group room; the server decides the recipients.
/// </summary>
public sealed class UdpMediaClient : IDisposable
{
    private readonly UdpClient _udpClient = new(0);
    private readonly IPEndPoint _serverEndpoint;
    private readonly Guid _sessionToken;
    private readonly Guid _mediaId;
    private readonly string _senderUsername;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private uint _audioSeq;
    private uint _videoSeq;
    private int _disposed;

    public event Action<MediaPacket>? AudioPacketReceived;
    public event Action<MediaPacket>? VideoPacketReceived;
    public event Action<Exception>? TransportError;

    public UdpMediaClient(
        string serverHost,
        Guid sessionToken,
        Guid mediaId,
        string? senderUsername = null)
    {
        if (string.IsNullOrWhiteSpace(serverHost)) throw new ArgumentException("Server host is required.", nameof(serverHost));
        if (sessionToken == Guid.Empty) throw new ArgumentException("Session token is required.", nameof(sessionToken));
        if (mediaId == Guid.Empty) throw new ArgumentException("Media id is required.", nameof(mediaId));

        _serverEndpoint = new IPEndPoint(ResolveHost(serverHost), NetworkConfig.UdpMediaPort);
        _sessionToken = sessionToken;
        _mediaId = mediaId;
        _senderUsername = senderUsername?.Trim() ?? string.Empty;
    }

    private static IPAddress ResolveHost(string host) =>
        IPAddress.TryParse(host, out var ip) ? ip : Dns.GetHostAddresses(host).First();

    public void Start()
    {
        if (_receiveTask is not null) return;
        _cts = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(_cts.Token);
        _heartbeatTask = SendHeartbeatLoopAsync(_cts.Token);
    }

    private MediaPacket CreatePacket(MediaType type, uint sequence, ushort index, ushort count, byte[] payload) => new()
    {
        SessionToken = _sessionToken,
        CallId = _mediaId,
        SenderUsername = _senderUsername,
        MediaType = type,
        SequenceNumber = sequence,
        TimestampTicks = DateTime.UtcNow.Ticks,
        FragmentIndex = index,
        FragmentCount = count,
        Payload = payload
    };

    private async Task SendHeartbeatLoopAsync(CancellationToken ct)
    {
        var handshake = CreatePacket(MediaType.Handshake, 0, 0, 1, new byte[] { 1 });
        while (!ct.IsCancellationRequested)
        {
            await SendRawAsync(handshake, ct).ConfigureAwait(false);
            try { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public async Task SendAudioAsync(byte[] pcmChunk, CancellationToken ct = default)
    {
        if (pcmChunk is null || pcmChunk.Length == 0) return;
        var count = (pcmChunk.Length + MediaPacket.MaxSafeUdpPayload - 1) /
                    MediaPacket.MaxSafeUdpPayload;
        if (count > ushort.MaxValue) throw new InvalidDataException("Audio chunk is too large.");

        var sequence = _audioSeq++;
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var offset = i * MediaPacket.MaxSafeUdpPayload;
            var length = Math.Min(MediaPacket.MaxSafeUdpPayload, pcmChunk.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(pcmChunk, offset, chunk, 0, length);
            await SendRawAsync(CreatePacket(MediaType.Audio, sequence, (ushort)i, (ushort)count, chunk), ct).ConfigureAwait(false);
        }
    }

    public async Task SendVideoFrameAsync(byte[] encodedFrame, CancellationToken ct = default)
    {
        if (encodedFrame is null || encodedFrame.Length == 0) return;
        var count = (encodedFrame.Length + MediaPacket.MaxSafeUdpPayload - 1) /
                    MediaPacket.MaxSafeUdpPayload;
        if (count <= 0 || count > ushort.MaxValue)
            throw new InvalidDataException("Video frame is too large.");

        var sequence = _videoSeq++;
        var timestamp = DateTime.UtcNow.Ticks;
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var offset = i * MediaPacket.MaxSafeUdpPayload;
            var length = Math.Min(MediaPacket.MaxSafeUdpPayload, encodedFrame.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(encodedFrame, offset, chunk, 0, length);
            var packet = new MediaPacket
            {
                SessionToken = _sessionToken,
                CallId = _mediaId,
                SenderUsername = _senderUsername,
                MediaType = MediaType.Video,
                SequenceNumber = sequence,
                TimestampTicks = timestamp,
                FragmentIndex = (ushort)i,
                FragmentCount = (ushort)count,
                Payload = chunk
            };
            await SendRawAsync(packet, ct).ConfigureAwait(false);
        }
    }

    private async Task SendRawAsync(MediaPacket packet, CancellationToken ct)
    {
        try
        {
            var bytes = packet.Serialize();
            await _udpClient.SendAsync(bytes, bytes.Length, _serverEndpoint).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        catch (Exception ex) when (ex is SocketException or InvalidDataException)
        {
            TransportError?.Invoke(ex);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                var packet = MediaPacket.TryDeserialize(result.Buffer, result.Buffer.Length);
                if (packet is null || packet.CallId != _mediaId || packet.MediaType == MediaType.Handshake) continue;
                if (packet.MediaType == MediaType.Audio) AudioPacketReceived?.Invoke(packet);
                else if (packet.MediaType == MediaType.Video) VideoPacketReceived?.Invoke(packet);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        catch (SocketException ex) when (!ct.IsCancellationRequested) { TransportError?.Invoke(ex); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts?.Cancel();
        _udpClient.Dispose();
        try { _receiveTask?.GetAwaiter().GetResult(); } catch { }
        try { _heartbeatTask?.GetAwaiter().GetResult(); } catch { }
        _cts?.Dispose();
    }
}
