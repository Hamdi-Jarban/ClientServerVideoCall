using System.Text.Json;

namespace VideoCall.Shared.Messages;

public sealed class Message
{
    public MessageType Type { get; init; }
    public string Payload { get; init; } = string.Empty;

    public static Message Create<T>(MessageType type, T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new Message
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload)
        };
    }

    public T? ReadPayload<T>()
    {
        if (string.IsNullOrWhiteSpace(Payload)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(Payload);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
