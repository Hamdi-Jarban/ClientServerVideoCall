using System.Collections.Concurrent;
using VideoCall.Shared.Models;

namespace VideoCall.Server;


public class UserManager
{
    
    private static readonly Dictionary<string, string> TestAccounts = new()
    {
        ["hamdi"] = "1234",
        ["ali1"] = "1111",
        ["ali2"] = "2222",
        ["ali3"] = "3333"
    };


    private readonly ConcurrentDictionary<string, ClientSession> _online = new(StringComparer.OrdinalIgnoreCase);

    public bool ValidateCredentials(string username, string password)
    {
        return TestAccounts.TryGetValue(username, out var expected) && expected == password;
    }

    public bool TryLogin(string username, ClientSession session)
    {
        return _online.TryAdd(username, session);
    }

    public void Logout(string username)
    {
        _online.TryRemove(username, out _);
    }

    public bool IsOnline(string username) => _online.ContainsKey(username);

    public bool TryGetSession(string username, out ClientSession session)
    {
        return _online.TryGetValue(username, out session!);
    }

    public List<string> GetOnlineUsernames() => _online.Keys.ToList();
}
