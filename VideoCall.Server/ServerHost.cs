using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Server;

public sealed class ServerHost : IAsyncDisposable
{
    private readonly TcpListener _tcpListener;
    private readonly ConcurrentDictionary<ClientSession, byte> _sessions = new();
    private readonly CancellationTokenSource _stop = new();
    private int _stopped;

    public PresenceManager Presence { get; }
    public ConversationManager Conversations { get; }
    public MediaRelay Media { get; }
    public ProtocolRouter Router { get; }

    public ServerHost(
        ICredentialValidator credentials,
        IPAddress? bindAddress = null,
        int tcpPort = NetworkConfig.TcpControlPort,
        int udpPort = NetworkConfig.UdpMediaPort,
        int maxGroupMembers = 8)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        Presence = new PresenceManager();
        Conversations = new ConversationManager(maxGroupMembers);
        Media = new MediaRelay(Conversations, Presence, udpPort);
        Router = new ProtocolRouter(Presence, Conversations, Media, credentials, SendToUserAsync);
        _tcpListener = new TcpListener(bindAddress ?? IPAddress.Any, tcpPort);
    }

    public async Task RunAsync(CancellationToken externalCancellation = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            externalCancellation, _stop.Token);
        var ct = linked.Token;

        _tcpListener.Start();
        Logger.Info($"TCP listening on {((IPEndPoint)_tcpListener.LocalEndpoint).Port}.");

        var mediaTask = Media.RunAsync(ct);
        var sessionTasks = new ConcurrentBag<Task>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _tcpListener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                var session = new ClientSession(client, this);
                if (!_sessions.TryAdd(session, 0))
                {
                    await session.CloseAsync();
                    continue;
                }

                Logger.Info($"TCP client connected from {client.Client.RemoteEndPoint}.");
                var task = session.RunAsync(ct);
                sessionTasks.Add(task);
                _ = ObserveSessionAsync(task, session);
            }
        }
        finally
        {
            _tcpListener.Stop();
            await StopAsync();

            foreach (var session in _sessions.Keys.ToArray())
            {
                try { await session.CloseAsync(); }
                catch (Exception ex) { Logger.Warn($"Session close failed: {ex.Message}"); }
            }

            try { await Task.WhenAll(sessionTasks.ToArray()); }
            catch { /* individual session errors were already logged */ }

            try { await mediaTask; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }

    public async Task OnSessionClosedAsync(ClientSession session)
    {
        if (!_sessions.TryRemove(session, out _)) return;

        try
        {
            await Router.HandleDisconnectAsync(session, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Disconnect cleanup failed: {ex.Message}");
        }
    }

    private async Task ObserveSessionAsync(Task sessionTask, ClientSession session)
    {
        try { await sessionTask; }
        catch (Exception ex) { Logger.Error($"Session task failed: {ex.Message}"); }
    }

    private async Task SendToUserAsync(string username, Message message, CancellationToken ct)
    {
        if (!Presence.TryGet(username, out var session)) return;
        try
        {
            await session.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Logger.Warn($"Could not send {message.Type} to {username}: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _stop.Cancel();
        _tcpListener.Stop();
        await Media.StopAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
