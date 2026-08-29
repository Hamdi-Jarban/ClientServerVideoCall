using System.Collections.Concurrent;

namespace VideoCall.Server;

/// <summary>
/// Keeps the currently authenticated TCP session for each username.
/// Authentication itself is intentionally delegated to ICredentialValidator;
/// this class only owns online presence.
/// </summary>
public sealed class PresenceManager
{
    private readonly ConcurrentDictionary<string, ClientSession> _online =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(string username, ClientSession session)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        return _online.TryAdd(username.Trim(), session);
    }

    public bool TryGet(string username, out ClientSession session) =>
        _online.TryGetValue(username, out session!);

    public bool IsOnline(string username) =>
        !string.IsNullOrWhiteSpace(username) && _online.ContainsKey(username.Trim());

    public bool Remove(string username, ClientSession expectedSession)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (!_online.TryGetValue(username.Trim(), out var current) ||
            !ReferenceEquals(current, expectedSession))
        {
            return false;
        }

        return _online.TryRemove(username.Trim(), out _);
    }

    public IReadOnlyList<string> GetUsernames() =>
        _online.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<ClientSession> GetSessions() => _online.Values.ToArray();
}

public interface ICredentialValidator
{
    bool Validate(string username, string password);
}

/// <summary>
/// Development-only validator. Do not use in production. Replace it with a
/// database-backed password-hash validator before deploying outside a lab LAN.
/// </summary>
public sealed class DevelopmentCredentialValidator : ICredentialValidator
{
    private readonly IReadOnlyDictionary<string, string> _accounts;

    public DevelopmentCredentialValidator(IReadOnlyDictionary<string, string> accounts)
    {
        _accounts = new Dictionary<string, string>(accounts, StringComparer.OrdinalIgnoreCase);
    }

    public bool Validate(string username, string password) =>
        !string.IsNullOrWhiteSpace(username) &&
        _accounts.TryGetValue(username.Trim(), out var expected) &&
        string.Equals(expected, password, StringComparison.Ordinal);
}
