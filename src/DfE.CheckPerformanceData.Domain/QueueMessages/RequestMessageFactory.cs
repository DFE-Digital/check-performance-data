using System.Text.Json;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

public static class RequestMessageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RequestMessage? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("DecisionType", out var typeProp))
            throw new InvalidOperationException("DecisionType not specified.");

        var decisionTypeName = typeProp.GetString();
        var success = Enum.TryParse<DecisionType>(decisionTypeName, out var decisionType);
        if (!success)
            throw new NotSupportedException($"Unknown decision type: {decisionTypeName}");

        return decisionType switch
        {
            DecisionType.Scrutiny => JsonSerializer.Deserialize<ScrutinyMessage>(json, JsonOptions),
            DecisionType.Approved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.Rejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            DecisionType.AutoApproved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.AutoRejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            _ => throw new NotSupportedException($"Unsupported decision type: {decisionTypeName}")
        };
    }
}