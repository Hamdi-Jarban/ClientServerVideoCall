using System.Net;
using System.Net.Sockets;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Server;

public sealed class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly TcpMessageReaderWriter _wire;
    private readonly ServerHost _host;
    private readonly CancellationTokenSource _stop = new();
    private int _closed;

    public Guid SessionToken { get; } = Guid.NewGuid();
    public string? Username { get; private set; }
    public bool IsAuthenticated => Username is not null;
    public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;

    public ClientSession(TcpClient client, ServerHost host)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _wire = new TcpMessageReaderWriter(client.GetStream());
        _client.NoDelay = true;
    }

    public void SetAuthenticatedUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));
        Username = username.Trim();
    }

    public async Task RunAsync(CancellationToken serverCancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            serverCancellation, _stop.Token);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                var message = await _wire.ReadMessageAsync(linked.Token);
                if (message is null) break;
                await _host.Router.DispatchAsync(this, message, linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (IOException ex)
        {
            Logger.Warn($"TCP connection closed for {Describe()}: {ex.Message}");
        }
        catch (SocketException ex)
        {
            Logger.Warn($"TCP socket failed for {Describe()}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Unhandled session error for {Describe()}: {ex}");
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task SendAsync(Message message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        try
        {
            await _wire.WriteMessageAsync(message, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Logger.Warn($"Failed to send {message.Type} to {Describe()}: {ex.Message}");
            await CloseAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    public async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;

        _stop.Cancel();
        try { _client.Client.Shutdown(SocketShutdown.Both); }
        catch { /* already disconnected */ }
        _client.Dispose();
        await _host.OnSessionClosedAsync(this);
    }

    private string Describe() => Username ?? RemoteEndPoint?.ToString() ?? "unknown";
}
