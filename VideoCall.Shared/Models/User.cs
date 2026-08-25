namespace VideoCall.Shared.Models;

/// <summary>
/// Represents a registered (in-memory, test) user account.
/// Passwords are only used for comparison and are never logged.
/// </summary>
public class User
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Represents a user that is currently connected and authenticated.
/// SessionId uniquely identifies a single TCP connection / login,
/// so a reconnect always gets a brand-new SessionId.
/// </summary>
public class OnlineUser
{
    public string Username { get; init; } = string.Empty;
    public Guid SessionId { get; init; } = Guid.NewGuid();
}
