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

public class OnlineUser
{
    public string Username { get; init; } = string.Empty;
    public Guid SessionId { get; init; } = Guid.NewGuid();
}
