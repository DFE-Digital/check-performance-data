using DfE.CheckPerformanceData.Application.RequestSubmission;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.RequestDecision;

public sealed class ScrutinyMessage : RequestDocument
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
