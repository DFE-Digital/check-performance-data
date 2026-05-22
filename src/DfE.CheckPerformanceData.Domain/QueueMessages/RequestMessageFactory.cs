using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

/// <summary>
/// Parses the queue-message JSON into a single normalised
/// <see cref="PendingRequestMessage"/>. The engine then decides the outcome —
/// the on-the-wire <c>DecisionType</c> (if present) is informational and
/// deliberately ignored here so a producer mistake can't force an auto-decision.
/// </summary>
public static class RequestMessageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Deserialise a queue-message body. Returns <c>null</c> only on malformed
    /// JSON; otherwise always returns a fully-typed message ready for the
    /// rules engine. Never throws on missing optional fields.
    /// </summary>
    public static RequestMessage? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<PendingRequestMessage>(json, JsonOptions);
    }
}
