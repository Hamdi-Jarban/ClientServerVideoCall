using System.Net;
using System.Net.Sockets;
using VideoCall.Shared.Networking;

namespace VideoCall.Client.Services;

/// <summary>
/// One UDP socket per active call, used to send this client's own
/// audio/video packets to the server relay and receive the other
/// participant's packets back. Bound to a single CallId + SessionToken
/// for its whole lifetime - a new call gets a new UdpMediaClient.
/// </summary>
public class UdpMediaClient : IDisposable
{
    private readonly UdpClient _udpClient = new();
    private readonly IPEndPoint _serverEndpoint;
    private readonly Guid _sessionToken;
    private readonly Guid _callId;
    private CancellationTokenSource? _cts;

    private uint _audioSeq;
    private uint _videoSeq;

    public event Action<MediaPacket>? AudioPacketReceived;
    public event Action<MediaPacket>? VideoPacketReceived;

    public UdpMediaClient(string serverHost, Guid sessionToken, Guid callId)
    {
        _serverEndpoint = new IPEndPoint(ResolveHost(serverHost), NetworkConfig.UdpMediaPort);
        _sessionToken = sessionToken;
        _callId = callId;
    }

    private static IPAddress ResolveHost(string host)
    {
        return IPAddress.TryParse(host, out var ip) ? ip : Dns.GetHostAddresses(host)[0];
    }
    public void Start()
    {
        _cts = new CancellationTokenSource();

        SendImmediateHandshake();

        _ = ReceiveLoopAsync(_cts.Token);
        _ = SendHeartbeatLoopAsync(_cts.Token);
    }

    private void SendImmediateHandshake()
    {
        var dummyPacket = new MediaPacket
        {
            SessionToken = _sessionToken,
            CallId = _callId,
            MediaType = MediaType.Audio,
            SequenceNumber = 0,
            TimestampTicks = DateTime.UtcNow.Ticks,
            FragmentIndex = 0,
            FragmentCount = 1,
            Payload = new byte[1] { 0xFF }
        };

        var bytes = dummyPacket.Serialize();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                _udpClient.Send(bytes, bytes.Length, _serverEndpoint);
            }
        }
        catch (Exception) { }
    }
    private async Task SendHeartbeatLoopAsync(CancellationToken ct)
    {
        var dummyPacket = new MediaPacket
        {
            SessionToken = _sessionToken,
            CallId = _callId,
            MediaType = MediaType.Audio,
            SequenceNumber = 0,
            TimestampTicks = DateTime.UtcNow.Ticks,
            FragmentIndex = 0,
            FragmentCount = 1,
            Payload = new byte[1] { 0x00 } // æÖÚ byte æÇÍÏ áÊÌÇæÒ Ãí ÝÍÕ ááÃÍÌÇã
        };

        while (!ct.IsCancellationRequested)
        {
            await SendRawAsync(dummyPacket);
            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    public async Task SendAudioAsync(byte[] pcmChunk)
    {
        var packet = new MediaPacket
        {
            SessionToken = _sessionToken,
            CallId = _callId,
            MediaType = MediaType.Audio,
            SequenceNumber = _audioSeq++,
            TimestampTicks = DateTime.UtcNow.Ticks,
            FragmentIndex = 0,
            FragmentCount = 1,
            Payload = pcmChunk
        };

        await SendRawAsync(packet);
    }

    
    public async Task SendVideoFrameAsync(byte[] encodedFrame)
    {
        int fragmentCount = (int)Math.Ceiling(encodedFrame.Length / (double)MediaPacket.MaxSafeUdpPayload);
        fragmentCount = Math.Max(fragmentCount, 1);
        uint seq = _videoSeq++;
        long timestamp = DateTime.UtcNow.Ticks;

        for (int i = 0; i < fragmentCount; i++)
        {
            int offset = i * MediaPacket.MaxSafeUdpPayload;
            int length = Math.Min(MediaPacket.MaxSafeUdpPayload, encodedFrame.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(encodedFrame, offset, chunk, 0, length);

            var packet = new MediaPacket
            {
                SessionToken = _sessionToken,
                CallId = _callId,
                MediaType = MediaType.Video,
                SequenceNumber = seq,
                TimestampTicks = timestamp,
                FragmentIndex = (ushort)i,
                FragmentCount = (ushort)fragmentCount,
                Payload = chunk
            };

            await SendRawAsync(packet);
        }
    }

    private async Task SendRawAsync(MediaPacket packet)
    {
        try
        {
            var bytes = packet.Serialize();
            await _udpClient.SendAsync(bytes, bytes.Length, _serverEndpoint);
        }
        catch (SocketException)
        {
            // Best-effort real-time media: a lost send is dropped, not retried.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udpClient.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var packet = MediaPacket.TryDeserialize(result.Buffer, result.Buffer.Length);
            if (packet is null || packet.CallId != _callId)
            {
                continue; // malformed or belongs to a stale call - reject
            }

            if (packet.MediaType == MediaType.Audio)
            {
                AudioPacketReceived?.Invoke(packet);
            }
            else
            {
                VideoPacketReceived?.Invoke(packet);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient.Dispose();
    }
}
