using System.Net.Sockets;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Networking;

namespace VideoCall.Server;

/// <summary>
/// Owns one connected TCP client for its entire lifetime: reads messages
/// in a loop, dispatches them to Server for handling, and writes replies
/// back out. All state that is specific to this one connection (which
/// user is logged in, their session token) lives here.
/// </summary>
public class ClientSession
{
    private readonly TcpClient _tcpClient;
    private readonly TcpMessageReaderWriter _framing;
    private readonly Server _server;
    private readonly CancellationTokenSource _cts = new();

    public string? Username { get; private set; }
    public Guid SessionToken { get; } = Guid.NewGuid();

    public ClientSession(TcpClient tcpClient, Server server)
    {
        _tcpClient = tcpClient;
        _server = server;
        _framing = new TcpMessageReaderWriter(tcpClient.GetStream());
    }

    public async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                Message? message;
                try
                {
                    message = await _framing.ReadMessageAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Client {DescribeForLog()} sent invalid data: {ex.Message}");
                    break;
                }

                if (message is null)
                {
                    break; // graceful disconnect
                }

                try
                {
                    await _server.DispatchAsync(this, message);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unhandled error dispatching {message.Type} for {DescribeForLog()}: {ex.Message}");
                    await SendAsync(Message.Create(MessageType.Error, new ErrorPayload(ErrorCodes.UnexpectedError, "An unexpected error occurred.")));
                }
            }
        }
        finally
        {
            await _server.OnClientDisconnectedAsync(this);
            _tcpClient.Close();
        }
    }

    public void SetUsername(string username) => Username = username;

    public async Task SendAsync(Message message)
    {
        try
        {
            await _framing.WriteMessageAsync(message, _cts.Token);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send {message.Type} to {DescribeForLog()}: {ex.Message}");
            Disconnect();
        }
    }

    public void Disconnect()
    {
        _cts.Cancel();
    }

    private string DescribeForLog() => Username ?? _tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
}
