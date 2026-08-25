using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VideoCall.Shared.Networking;

namespace VideoCall.Server;

public class UdpMediaRelay
{
    private readonly UdpClient _udpClient;
    private readonly CallManager _callManager;
    private readonly Func<Guid, (string? UserA, string? UserB)> _resolveCallParticipants;
    private readonly Func<string, Guid?> _resolveSessionToken;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, IPEndPoint>> _endpointsByCall = new();

    public UdpMediaRelay(
        CallManager callManager,
        Func<Guid, (string? UserA, string? UserB)> resolveCallParticipants,
        Func<string, Guid?> resolveSessionToken)
    {
        _callManager = callManager;
        _resolveCallParticipants = resolveCallParticipants;
        _resolveSessionToken = resolveSessionToken;
        _udpClient = new UdpClient(NetworkConfig.UdpMediaPort);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Logger.Info($"UDP media relay listening on port {NetworkConfig.UdpMediaPort}");

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
            catch (SocketException ex)
            {
                Logger.Warn($"UDP receive error: {ex.Message}");
                continue;
            }

            HandlePacket(result.Buffer, result.RemoteEndPoint);
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint sender)
    {
        var packet = MediaPacket.TryDeserialize(data, data.Length);
        if (packet is null)
        {
            return;
        }

        if (!_callManager.IsCallConnected(packet.CallId))
        {
            return;
        }

        var (userA, userB) = _resolveCallParticipants(packet.CallId);
        if (userA is null || userB is null)
        {
            return;
        }

        string? sendingUser = null;
        if (_resolveSessionToken(userA) == packet.SessionToken)
        {
            sendingUser = userA;
        }
        else if (_resolveSessionToken(userB) == packet.SessionToken)
        {
            sendingUser = userB;
        }

        if (sendingUser is null)
        {
            return;
        }

        var endpoints = _endpointsByCall.GetOrAdd(packet.CallId, _ => new ConcurrentDictionary<string, IPEndPoint>(StringComparer.OrdinalIgnoreCase));
        endpoints[sendingUser] = sender;

        foreach (var kvp in endpoints)
        {
            if (!string.Equals(kvp.Key, sendingUser, StringComparison.OrdinalIgnoreCase))
            {
                _ = _udpClient.SendAsync(data, data.Length, kvp.Value);
            }
        }
    }

    public void ForgetCall(Guid callId)
    {
        _endpointsByCall.TryRemove(callId, out ConcurrentDictionary<string, IPEndPoint>? removed);
    }
}