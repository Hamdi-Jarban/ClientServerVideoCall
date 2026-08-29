namespace VideoCall.Shared.Models;

public enum ConversationType
{
    Private,
    Group
}

public enum ConversationState
{
    Created,
    Active,
    Ended
}

public sealed class Conversation
{
    public string Id { get; init; } = string.Empty;
    public ConversationType Type { get; init; }
    public string Host { get; set; } = string.Empty;
    public HashSet<string> Members { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConversationState State { get; set; } = ConversationState.Created;
    public Guid? MediaId { get; set; }
}
