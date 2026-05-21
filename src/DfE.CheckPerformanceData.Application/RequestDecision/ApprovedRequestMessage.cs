using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.RequestSubmission;


namespace DfE.CheckPerformanceData.Application.RequestDecision;

public sealed class ApprovedRequestMessage : RequestDocument
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

}

