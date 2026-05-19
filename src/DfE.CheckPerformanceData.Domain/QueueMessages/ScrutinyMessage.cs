using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public sealed class ScrutinyMessage : RequestMessage
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
