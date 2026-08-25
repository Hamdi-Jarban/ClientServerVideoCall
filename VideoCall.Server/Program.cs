using VideoCall.Server;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new Server();
await server.RunAsync(cts.Token);
