using System.Text.Json;

namespace VideoCall.Shared.Messages;

/// <summary>
/// Generic envelope for every message sent over the TCP control channel.
/// "Payload" is the JSON-serialized form of one of the *Payload records
/// in this folder; which one depends on "Type". Keeping the envelope
/// generic means the framing/reading code never needs to know about
/// specific payload shapes - only Message itself is (de)serialized
/// directly off the wire, and payloads are decoded on demand.
/// </summary>
public class Message
{
    public MessageType Type { get; set; }
    public string Payload { get; set; } = string.Empty;

    public static Message Create<T>(MessageType type, T payload)
    {
        return new Message
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload)
        };
    }

    public T? ReadPayload<T>()
    {
        if (string.IsNullOrEmpty(Payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(Payload);
    }
}
