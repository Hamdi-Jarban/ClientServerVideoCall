using System.Collections.Concurrent;
using VideoCall.Shared.Models;

namespace VideoCall.Server;

public enum ConversationOperation
{
    Success,
    AlreadyExists,
    NotFound,
    AlreadyMember,
    NotMember,
    Full,
    InvalidType,
    NotHost,
    InvalidState,
    PrivateConversationMustHaveTwoMembers
}

/// <summary>
/// Owns conversation membership and state. It never touches TCP or UDP.
/// All mutations are serialized through one lock to keep the educational
/// implementation easy to reason about. Replace with per-conversation locks
/// or a database when scaling beyond one server process.
/// </summary>
public sealed class ConversationManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Conversation> _conversations =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly int _maxGroupMembers;

    public ConversationManager(int maxGroupMembers = 8)
    {
        if (maxGroupMembers < 2) throw new ArgumentOutOfRangeException(nameof(maxGroupMembers));
        _maxGroupMembers = maxGroupMembers;
    }

    public ConversationOperation CreatePrivate(
        string conversationId,
        string caller,
        string callee,
        out Conversation? conversation)
    {
        conversation = null;
        if (string.IsNullOrWhiteSpace(conversationId) ||
            string.IsNullOrWhiteSpace(caller) ||
            string.IsNullOrWhiteSpace(callee) ||
            caller.Equals(callee, StringComparison.OrdinalIgnoreCase))
        {
            return ConversationOperation.InvalidType;
        }

        lock (_gate)
        {
            if (_conversations.ContainsKey(conversationId))
                return ConversationOperation.AlreadyExists;

            var item = new Conversation
            {
                Id = conversationId,
                Type = ConversationType.Private,
                Host = caller,
                State = ConversationState.Created
            };
            item.Members.Add(caller);
            item.Members.Add(callee);
            _conversations.Add(item.Id, item);
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation CreateGroup(
        string conversationId,
        string host,
        out Conversation? conversation)
    {
        conversation = null;
        if (!IsValidId(conversationId) || string.IsNullOrWhiteSpace(host))
            return ConversationOperation.InvalidType;

        lock (_gate)
        {
            if (_conversations.ContainsKey(conversationId))
                return ConversationOperation.AlreadyExists;

            var item = new Conversation
            {
                Id = conversationId.Trim(),
                Type = ConversationType.Group,
                Host = host.Trim(),
                State = ConversationState.Created
            };
            item.Members.Add(host.Trim());
            _conversations.Add(item.Id, item);
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation AddMember(
        string conversationId,
        string requestingUser,
        string memberUsername,
        out Conversation? conversation)
    {
        conversation = null;
        if (string.IsNullOrWhiteSpace(memberUsername)) return ConversationOperation.InvalidType;
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item)) return ConversationOperation.NotFound;
            if (!item.Members.Contains(requestingUser)) return ConversationOperation.NotMember;
            if (!item.Host.Equals(requestingUser, StringComparison.OrdinalIgnoreCase)) return ConversationOperation.NotHost;
            if (item.State == ConversationState.Active) return ConversationOperation.InvalidState;
            if (item.Members.Contains(memberUsername.Trim()))
            {
                conversation = SnapshotUnsafe(item);
                return ConversationOperation.AlreadyMember;
            }
            if (item.Members.Count >= _maxGroupMembers) return ConversationOperation.Full;
            item.Members.Add(memberUsername.Trim());
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation Join(
        string conversationId,
        string username,
        out Conversation? conversation)
    {
        conversation = null;
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;

            if (item.Type == ConversationType.Private)
                return ConversationOperation.InvalidType;

            if (item.Members.Contains(username))
            {
                conversation = SnapshotUnsafe(item);
                return ConversationOperation.AlreadyMember;
            }

            if (item.Members.Count >= _maxGroupMembers)
                return ConversationOperation.Full;

            item.Members.Add(username);
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation Leave(
        string conversationId,
        string username,
        out Conversation? conversation,
        out bool removed)
    {
        conversation = null;
        removed = false;

        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;

            if (!item.Members.Remove(username))
                return ConversationOperation.NotMember;

            if (item.Members.Count == 0)
            {
                _conversations.Remove(conversationId);
                removed = true;
                return ConversationOperation.Success;
            }

            if (item.Host.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                item.Host = item.Members.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
            }

            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation StartMedia(
        string conversationId,
        string username,
        out Conversation? conversation)
    {
        conversation = null;
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;
            if (!item.Members.Contains(username))
                return ConversationOperation.NotMember;
            if (!item.Host.Equals(username, StringComparison.OrdinalIgnoreCase))
                return ConversationOperation.NotHost;
            if (item.Type == ConversationType.Private && item.Members.Count != 2)
                return ConversationOperation.PrivateConversationMustHaveTwoMembers;

            item.State = ConversationState.Active;
            item.MediaId ??= Guid.NewGuid();
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation EndPrivate(
        string conversationId,
        string username,
        out IReadOnlyList<string> members)
    {
        members = Array.Empty<string>();
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;
            if (item.Type != ConversationType.Private || !item.Members.Contains(username))
                return ConversationOperation.NotMember;

            members = item.Members.ToArray();
            item.State = ConversationState.Ended;
            _conversations.Remove(conversationId);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation End(
        string conversationId,
        string username,
        out IReadOnlyList<string> members)
    {
        members = Array.Empty<string>();
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;
            if (!item.Members.Contains(username))
                return ConversationOperation.NotMember;
            if (!item.Host.Equals(username, StringComparison.OrdinalIgnoreCase))
                return ConversationOperation.NotHost;

            members = item.Members.ToArray();
            item.State = ConversationState.Ended;
            _conversations.Remove(conversationId);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation ActivateMedia(
        string conversationId,
        string username,
        out Conversation? conversation)
    {
        conversation = null;
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;
            if (!item.Members.Contains(username))
                return ConversationOperation.NotMember;

            item.State = ConversationState.Active;
            item.MediaId ??= Guid.NewGuid();
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public ConversationOperation StopMedia(
        string conversationId,
        string username,
        out Conversation? conversation)
    {
        conversation = null;
        lock (_gate)
        {
            if (!_conversations.TryGetValue(conversationId, out var item))
                return ConversationOperation.NotFound;
            if (!item.Members.Contains(username))
                return ConversationOperation.NotMember;
            if (!item.Host.Equals(username, StringComparison.OrdinalIgnoreCase))
                return ConversationOperation.NotHost;

            item.State = ConversationState.Created;
            item.MediaId = null;
            conversation = SnapshotUnsafe(item);
            return ConversationOperation.Success;
        }
    }

    public bool TryGet(string conversationId, out Conversation conversation)
    {
        lock (_gate)
        {
            if (_conversations.TryGetValue(conversationId, out var item))
            {
                conversation = SnapshotUnsafe(item);
                return true;
            }
        }

        conversation = null!;
        return false;
    }

    public bool IsMember(string conversationId, string username)
    {
        lock (_gate)
        {
            return _conversations.TryGetValue(conversationId, out var item) &&
                   item.Members.Contains(username);
        }
    }

    public bool TryGetActiveConversationByMediaId(Guid mediaId, out Conversation conversation)
    {
        lock (_gate)
        {
            var item = _conversations.Values.FirstOrDefault(x => x.State == ConversationState.Active && x.MediaId == mediaId);
            if (item is not null)
            {
                conversation = SnapshotUnsafe(item);
                return true;
            }
        }

        conversation = null!;
        return false;
    }

    public bool IsMediaActive(string conversationId)
    {
        lock (_gate)
        {
            return _conversations.TryGetValue(conversationId, out var item) &&
                   item.State == ConversationState.Active;
        }
    }

    public IReadOnlyList<string> GetMembersSnapshot(string conversationId)
    {
        lock (_gate)
        {
            return _conversations.TryGetValue(conversationId, out var item)
                ? item.Members.ToArray()
                : Array.Empty<string>();
        }
    }

    public IReadOnlyList<Conversation> RemoveUserFromAll(string username)
    {
        var changed = new List<Conversation>();
        lock (_gate)
        {
            foreach (var item in _conversations.Values.ToArray())
            {
                if (!item.Members.Remove(username)) continue;

                if (item.Members.Count == 0)
                {
                    _conversations.Remove(item.Id);
                    continue;
                }

                if (item.Host.Equals(username, StringComparison.OrdinalIgnoreCase))
                    item.Host = item.Members.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();

                changed.Add(SnapshotUnsafe(item));
            }
        }

        return changed;
    }

    private static bool IsValidId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 64;

    private static Conversation SnapshotUnsafe(Conversation source)
    {
        var snapshot = new Conversation
        {
            Id = source.Id,
            Type = source.Type,
            Host = source.Host,
            State = source.State,
            MediaId = source.MediaId
        };
        foreach (var member in source.Members) snapshot.Members.Add(member);
        return snapshot;
    }
}
