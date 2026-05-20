using System.Text.Json;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.QueueMessages;

public static class RequestMessageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RequestDocument? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("DecisionType", out var typeProp))
            throw new InvalidOperationException("DecisionType not specified.");

        var decisionTypeName = typeProp.GetString();
        var success = Enum.TryParse<DecisionType>(decisionTypeName, out var decisionType);
        if (!success)
            throw new NotSupportedException($"Unknown decision type: {decisionTypeName}");

        RequestDocument message = decisionType switch
        {
            DecisionType.Scrutiny => JsonSerializer.Deserialize<ScrutinyMessage>(json, JsonOptions),
            DecisionType.Approved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.Rejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            DecisionType.AutoApproved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.AutoRejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            _ => throw new NotSupportedException($"Unsupported decision type: {decisionTypeName}")
        };


        return message;
    }
}