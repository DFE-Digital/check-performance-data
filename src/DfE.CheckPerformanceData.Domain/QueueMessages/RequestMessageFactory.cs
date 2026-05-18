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

        RequestMessage message = decisionType switch
        {
            DecisionType.Scrutiny => JsonSerializer.Deserialize<ScrutinyMessage>(json, JsonOptions),
            DecisionType.Approved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.Rejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            DecisionType.AutoApproved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.AutoRejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            _ => throw new NotSupportedException($"Unsupported decision type: {decisionTypeName}")
        };

        // Map file uploads from question-level Answers to the message-level Uploads property
        if (message is IRequestMessageUploads uploadsMessage)
        {
            foreach (var answer in message.Answers)
            {
                if (answer.Files == null || answer.Files.Count == 0)
                    continue;

                foreach (var file in answer.Files)
                {
                    if (Guid.TryParse(file.StoredFileName, out var fileId))
                    {
                        uploadsMessage.Uploads.Add(new UploadInfo
                        {
                            Filename = file.OriginalFileName,
                            Id = fileId
                        });
                    }
                }
            }
        }

        return message;
    }
}