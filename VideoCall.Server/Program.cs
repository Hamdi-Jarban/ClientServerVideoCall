using System.Net;
using VideoCall.Server;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};


var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["hamdi"] = "1234",
    ["ali1"] = "1111",
    ["ali2"] = "2222",
    ["ali3"] = "3333"
};

await using var server = new ServerHost(
    credentials: new DevelopmentCredentialValidator(accounts),
    bindAddress: IPAddress.Any,
    maxGroupMembers: 8);

Console.WriteLine("VideoCall server started. Press Ctrl+C to stop.");
await server.RunAsync(shutdown.Token);
