using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Domain.QueueMessages;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

public sealed class RulesEngineWorker : BackgroundService
{
    private readonly ILogger<RulesEngineWorker> _logger;
    private readonly QueueClient _queueClient;
    private readonly RulesEngineOptions _options;
    private readonly IRequestDecisionHandler _handler;

    public RulesEngineWorker(
        ILogger<RulesEngineWorker> logger,
        QueueServiceClient queueServiceClient,
        IOptions<RulesEngineOptions> options,
        IRequestDecisionHandler handler)
    {
        _options = options.Value;
        _logger = logger;
        _queueClient = queueServiceClient.GetQueueClient(_options.QueueName);
        _handler = handler;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                await Task.Delay(_options.RetryDelayMs, stoppingToken);
            }
        }
    }

    private async Task PollQueueAsync(CancellationToken stoppingToken)
    {
        QueueMessage[] messages = await _queueClient.ReceiveMessagesAsync(
            _options.MaxMessagesPerPoll, 
            visibilityTimeout: null, 
            stoppingToken);

        if (messages.Length == 0)
        {
            await Task.Delay(_options.EmptyQueueDelayMs, stoppingToken);
            return;
        }
        
        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, stoppingToken);
        }
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        try
        {
            var messageBody = Encoding.UTF8.GetString(message.Body);
            _logger.LogInformation("Processing message: {MessageContents}", messageBody);

            var parsedMessage = RequestMessageFactory.Parse(messageBody)
                ?? throw new InvalidOperationException("Failed to parse message.");

            await _handler.HandleAsync(parsedMessage, stoppingToken);
            // await parsedMessage.ProcessAsync(stoppingToken);
            await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process message {MessageId} (dequeue count: {DequeueCount})",
                message.MessageId, message.DequeueCount);

            if (message.DequeueCount >= _options.MaxDequeueCount)
            {
                _logger.LogWarning(
                    "Message {MessageId} exceeded max dequeue count ({Max}); deleting (poison message)",
                    message.MessageId, _options.MaxDequeueCount);

                await _queueClient.DeleteMessageAsync(
                    message.MessageId, message.PopReceipt, stoppingToken);
            }
        }
    }
}

public sealed class RulesEngineOptions
{
    public int RetryDelayMs { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public int MaxMessagesPerPoll { get; set; }
    public int EmptyQueueDelayMs { get; set; }
    public long MaxDequeueCount { get; set; }
}

//public interface IRequestMessageUploads
//{
//    public List<UploadInfo> Uploads { get; set; }

//}

//public class UploadInfo
//{
//    public string filename { get; set; } = string.Empty;
//    public Guid Id { get; set; }

//}

//public abstract class RequestMessage
//{
//    public Guid WindowId { get; set; }
//    public Guid RequestId { get; set; }
//    public DecisionType DecisionType { get; set; }
//    public abstract Task ProcessAsync(CancellationToken token);
//}
//// todo rename approved and rejected to auto approved and auto rejected based on the assumption that approved/rejected is a manual process done by aser in the zendesk portal.
//public class RejectedRequestMessage : RequestMessage, IRequestMessageUploads
//{
//    public string? Reason { get; set; }
//    public List<UploadInfo> Uploads { get; set; } = new List<UploadInfo>();
//    public override Task ProcessAsync(CancellationToken token)
//    {
//        // call zendesk api service to create a ticket with the rejection reason and other details, and upload any evidence files to the ticket using the upload ids in the message if present
//        Console.WriteLine($"Processing RejectedRequestMessage: WindowId={WindowId}, RequestId={RequestId}, Reason={Reason}");
//        return Task.CompletedTask;
//    }
//}

//public class ApprovedRequestMessage : RequestMessage, IRequestMessageUploads
//{
//    public string? Reason { get; set; }
//    public List<UploadInfo> Uploads { get; set; } = new List<UploadInfo>();
//    public override Task ProcessAsync(CancellationToken token)
//    {
//        // call zendesk api service to create a ticket with the approval reason and other details, and upload any evidence files to the ticket using the upload ids in the message if present
//        Console.WriteLine($"Processing RejectedRequestMessage: WindowId={WindowId}, RequestId={RequestId}, Reason={Reason}");
//        return Task.CompletedTask;
//    }
//}

//public class ScrutinyMessage : RequestMessage
//{
//    public string? Reason { get; set; }

//    public override Task ProcessAsync(CancellationToken token)
//    {
//        // call zendesk api service to create a ticket with the scrutiny reason and other details, and upload any evidence files to the ticket using the upload ids in the message if present
//        Console.WriteLine($"Processing ScrutinyMessage: WindowId={WindowId}, RequestId={RequestId}, Reason={Reason}");
//        return Task.CompletedTask;
//    }
//}


//public static class RequestMessageFactory
//{
//    private static readonly JsonSerializerOptions JsonOptions = new()
//    {
//        PropertyNameCaseInsensitive = true
//    };

//    public static RequestMessage? Parse(string json)
//    {
//        using var doc = JsonDocument.Parse(json);
//        if (!doc.RootElement.TryGetProperty("DecisionType", out var typeProp))
//            throw new InvalidOperationException("Decision type not specified.");

//        var decisionTypeName = typeProp.GetString(); // this can be used like decision type
//        var success = Enum.TryParse<DecisionType>(decisionTypeName, out var decisionType);
//        if (!success)
//            throw new NotSupportedException($"Unknown message type: {decisionTypeName}");
//        return decisionType switch
//        {
//            DecisionType.Scrutiny => JsonSerializer.Deserialize<ScrutinyMessage>(json, JsonOptions),
//            DecisionType.Approved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
//            DecisionType.Rejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
//            _ => throw new NotSupportedException($"Unknown message type: {decisionTypeName}")
//        };
//    }
//}



/*
 * {
  "Status": "Submitted",
  "ReferenceNumber": "CYPMD_KS4June_9C2B56C",
  "SubmittedAt": "2026-05-12T08:44:35.2029205Z",
  "SubmittedBy": {
    "UserId": "47CA93E2-4017-415E-866B-D822634D4D2F",
    "DisplayName": "Dave Gouge"
  },
  "CheckingWindowId": "f34d285b-8660-4d12-9c30-787328deaa0a",
  "CheckingWindowType": "KS4June",
  "WhatToChange": "Remove",
  "School": {
    "Urn": "142313",
    "Name": "Kingsmead School"
  },
  "Pupil": {
    "Id": "fa0e03e1-3d98-4ce3-9bab-3ff35093fc0c",
    "CypmdId": "000014",
    "Firstname": "Nina",
    "Surname": "Thomas",
    "DateOfBirth": "04/04/2010",
    "Sex": "F",
    "Age": 16
  },
  "Answers": [
    {
      "QuestionId": "reason",
      "QuestionTitle": "Why are you asking to remove Nina Thomas from the performance data?",
      "Type": "Radio",
      "Value": "Social care involvement - including police or prison",
      "Files": null
    },
    {
      "QuestionId": "social-care-reason",
      "QuestionTitle": "What happened to Nina Thomas that made it very hard for them to go to school or sit exams?",
      "Type": "Radio",
      "Value": "Police involvement",
      "Files": null
    },
    {
      "QuestionId": "sat-exams",
      "QuestionTitle": "Has Nina Thomas sat any exams as a year 11 pupil?",
      "Type": "Radio",
      "Value": "Yes",
      "Files": null
    },
    {
      "QuestionId": "evidence",
      "QuestionTitle": "Upload files",
      "Type": "FileUpload",
      "Value": null,
      "Files": [
        {
          "OriginalFileName": "Evidence.pdf",
          "StoredFileName": "b05bb5f6-88e7-4aea-af97-3553a81dd64c",
          "PageCount": 1,
          "FileSizeBytes": 4108
        }
      ]
    },
    {
      "QuestionId": "how-evidence-supports",
      "QuestionTitle": "Explain how the evidence supports removing this pupil",
      "Type": "TextArea",
      "Value": "this is because....",
      "Files": null
    }
  ]
}
 * 
 */