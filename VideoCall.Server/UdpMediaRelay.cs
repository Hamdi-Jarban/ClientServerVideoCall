using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VideoCall.Shared.Networking;

namespace VideoCall.Server;

/// <summary>
/// Relay for both private calls and group rooms. For a room, every client
/// sends one UDP stream to this process and the relay forwards it to every
/// other current room member.
/// </summary>
public sealed class UdpMediaRelay
{
    private readonly UdpClient _udpClient;
    private readonly CallManager _callManager;
    private readonly Func<Guid, (string? UserA, string? UserB)> _resolveCallParticipants;
    private readonly Func<Guid, string?> _resolveRoomId;
    private readonly Func<string, IReadOnlyCollection<string>> _resolveRoomMembers;
    private readonly Func<string, Guid?> _resolveSessionToken;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, IPEndPoint>> _endpoints = new();
    private int _stopped;

    public UdpMediaRelay(
        CallManager callManager,
        Func<Guid, (string? UserA, string? UserB)> resolveCallParticipants,
        Func<Guid, string?> resolveRoomId,
        Func<string, IReadOnlyCollection<string>> resolveRoomMembers,
        Func<string, Guid?> resolveSessionToken)
    {
        _callManager = callManager;
        _resolveCallParticipants = resolveCallParticipants;
        _resolveRoomId = resolveRoomId;
        _resolveRoomMembers = resolveRoomMembers;
        _resolveSessionToken = resolveSessionToken;
        _udpClient = new UdpClient(NetworkConfig.UdpMediaPort);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Logger.Info($"UDP media relay listening on port {NetworkConfig.UdpMediaPort}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udpClient.ReceiveAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) when (Volatile.Read(ref _stopped) != 0) { break; }
                catch (SocketException ex)
                {
                    Logger.Warn($"UDP receive error: {ex.Message}");
                    continue;
                }

                await HandlePacketAsync(result.Buffer, result.RemoteEndPoint, ct);
            }
        }
        finally
        {
            await StopAsync();
        }
    }

    private async Task HandlePacketAsync(byte[] data, IPEndPoint sender, CancellationToken ct)
    {
        var packet = MediaPacket.TryDeserialize(data, data.Length);
        if (packet is null) return;

        var privateParticipants = _callManager.IsCallConnected(packet.CallId)
            ? _resolveCallParticipants(packet.CallId)
            : (null, null);
        var roomId = privateParticipants.UserA is not null
            ? null
            : _resolveRoomId(packet.CallId);

        IReadOnlyCollection<string> allowedMembers;
        if (privateParticipants.UserA is not null && privateParticipants.UserB is not null)
        {
            allowedMembers = new[] { privateParticipants.UserA, privateParticipants.UserB };
        }
        else if (roomId is not null)
        {
            allowedMembers = _resolveRoomMembers(roomId);
            if (allowedMembers.Count == 0) return;
        }
        else
        {
            return;
        }

        var sendingUser = ResolveSender(packet, allowedMembers);
        if (sendingUser is null) return;

        var endpoints = _endpoints.GetOrAdd(
            packet.CallId,
            _ => new ConcurrentDictionary<string, IPEndPoint>(StringComparer.OrdinalIgnoreCase));
        endpoints[sendingUser] = sender;

        foreach (var item in endpoints.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            if (item.Key.Equals(sendingUser, StringComparison.OrdinalIgnoreCase)) continue;
            if (!allowedMembers.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
            {
                endpoints.TryRemove(item.Key, out _);
                continue;
            }

            try
            {
                await _udpClient.SendAsync(data, data.Length, item.Value);
            }
            catch (SocketException ex)
            {
                Logger.Warn($"UDP forward to {item.Key} failed: {ex.Message}");
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }
        }
    }

    private string? ResolveSender(MediaPacket packet, IReadOnlyCollection<string> members)
    {
        if (!string.IsNullOrWhiteSpace(packet.SenderUsername) &&
            members.Contains(packet.SenderUsername, StringComparer.OrdinalIgnoreCase) &&
            _resolveSessionToken(packet.SenderUsername) == packet.SessionToken)
        {
            return packet.SenderUsername;
        }

        foreach (var member in members)
        {
            if (_resolveSessionToken(member) == packet.SessionToken) return member;
        }
        return null;
    }

    public void ForgetCall(Guid mediaId) => _endpoints.TryRemove(mediaId, out _);

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _udpClient.Dispose();
        await Task.CompletedTask;
    }
}
