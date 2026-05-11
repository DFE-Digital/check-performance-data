using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
   

namespace DfE.CheckPerformanceData.RulesEngineWorker;

public sealed class RulesEngineWorker : BackgroundService
{
    private readonly ILogger<RulesEngineWorker> _logger;
    private readonly QueueClient _queueClient;
    private readonly RulesEngineOptions _options;

    public RulesEngineWorker(ILogger<RulesEngineWorker> logger, QueueServiceClient queueServiceClient, IOptions<RulesEngineOptions> options)
    {
        _options = options.Value;
        _logger = logger;
        _queueClient = queueServiceClient.GetQueueClient(_options.QueueName);
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
        QueueMessage[] messages = await _queueClient.ReceiveMessagesAsync(_options.MaxMessagesPerPoll, visibilityTimeout: null, stoppingToken);

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

            // TODO: Add actual message processing logic here
            // check if message is valid, if not throw an exception to trigger retry logic
            // assume all messages are cypmd request related for the specified queue name.
            // check message type and route to appropriate handler, if unknown type throw an exception to trigger retry logic
            // possible message types : IRequestMessage (base Interface), IRejectedRequestMessage, IAcceptedRequestMessage, IScrutinyMessage, IRuleEvaluationResultMessage
            // or just have a single message type with a "type" property to determine the message type and route to appropriate handler and switch logic based on the decision type
            var parsedMessage = MessageFactory.Parse(messageBody)
                ?? throw new InvalidOperationException("Failed to parse message.");

            await parsedMessage.ProcessAsync(stoppingToken);


            /*
             * decision types :
             * 
             * {
          "id": 19056253669394,
          "name": "Approved",
          "raw_name": "Approved",
          "value": "approved",
          "default": false
        },
        {
          "id": 19056253669522,
          "name": "Rejected",
          "raw_name": "Rejected",
          "value": "rejected",
          "default": false
        },
        {
          "id": 19056253669650,
          "name": "Auto_Approved",
          "raw_name": "Auto_Approved",
          "value": "auto_approved",
          "default": false
        },
        {
          "id": 19056253669778,
          "name": "Auto_Rejected",
          "raw_name": "Auto_Rejected",
          "value": "auto_rejected",
          "default": false
        },
        {
          "id": 19103017562770,
          "name": "Scrutiny",
          "raw_name": "Scrutiny",
          "value": "scrutiny",
          "default": false
        }


            base data
            `{
"WindowId": "WindowIDGUID",
"RequestId": "RequestIdGUID"
}`

            upload storage container message format
            {
   "uploads": [
      {
         "filename" : "myevidence.pdf",
         "id": "theguidoftheblob"
      }
   ]
}
             * 
             * 
             * 
             */

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
    public string QueueName { get; set; }
    public int MaxMessagesPerPoll { get; set; }
    public int EmptyQueueDelayMs { get; set; }
    public long MaxDequeueCount { get; set; }
}


public interface IRequestMessageUploads
{
    public List<UploadInfo> Uploads { get; set; }

}

public class UploadInfo
{
    public string filename { get; set; } = string.Empty;
    public Guid Id { get; set; }

}

public abstract class RequestMessage
{
    public Guid WindowId { get; set; }
    public Guid RequestId { get; set; }
    public DecisionType DecisionType { get; set; }
    public abstract Task ProcessAsync(CancellationToken token);
}
// todo rename approved and rejected to auto approved and auto rejected based on the assumption that approved/rejected is a manual process done by aser in the zendesk portal.
public class RejectedRequestMessage : RequestMessage, IRequestMessageUploads
{
    public string? Reason { get; set; }
    public List<UploadInfo> Uploads { get; set; } = new List<UploadInfo>();
    public override Task ProcessAsync(CancellationToken token)
    {
        // call zendesk api service to create a ticket with the rejection reason and other details, and upload any evidence files to the ticket using the upload ids in the message if present
        Console.WriteLine($"Processing RejectedRequestMessage: WindowId={WindowId}, RequestId={RequestId}, Reason={Reason}");
        return Task.CompletedTask;
    }
}

public class ApprovedRequestMessage : RequestMessage, IRequestMessageUploads
{
    public string? Reason { get; set; }
    public List<UploadInfo> Uploads { get; set; } = new List<UploadInfo>();
    public override Task ProcessAsync(CancellationToken token)
    {
        // call zendesk api service to create a ticket with the approval reason and other details, and upload any evidence files to the ticket using the upload ids in the message if present
        Console.WriteLine($"Processing RejectedRequestMessage: WindowId={WindowId}, RequestId={RequestId}, Reason={Reason}");
        return Task.CompletedTask;
    }
}

public class ScrutinyMessage : RequestMessage
{
    public string? Reason { get; set; }

    public override Task ProcessAsync(CancellationToken token)
    {
        // call zendesk api service to create a ticket with the scrutiny reason and other details, and upload any evidence files to the ticket using the upload ids in the message if present
        Console.WriteLine($"Processing ScrutinyMessage: WindowId={WindowId}, RequestId={RequestId}, Reason={Reason}");
        return Task.CompletedTask;
    }
}


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
            throw new InvalidOperationException("Decision type not specified.");

        var decisionTypeName = typeProp.GetString(); // this can be used like decision type
        var success = Enum.TryParse<DecisionType>(decisionTypeName, out var decisionType);
        if (!success)
            throw new NotSupportedException($"Unknown message type: {decisionTypeName}");
        return decisionType switch
        {
            DecisionType.Scrutiny => JsonSerializer.Deserialize<ScrutinyMessage>(json, JsonOptions),
            DecisionType.Approved => JsonSerializer.Deserialize<ApprovedRequestMessage>(json, JsonOptions),
            DecisionType.Rejected => JsonSerializer.Deserialize<RejectedRequestMessage>(json, JsonOptions),
            _ => throw new NotSupportedException($"Unknown message type: {decisionTypeName}")
        };
    }
}


public abstract class BaseMessage
{
    public abstract Task ProcessAsync(CancellationToken token);
}

public class MessageA : BaseMessage
{
    public string? Data { get; set; }

    public override Task ProcessAsync(CancellationToken token)
    {
        Console.WriteLine($"Processing MessageA: {Data}");
        return Task.CompletedTask;
    }
}

public class MessageB : BaseMessage
{
    public int Count { get; set; }

    public override Task ProcessAsync(CancellationToken token)
    {
        Console.WriteLine($"Processing MessageB: Count={Count}");
        return Task.CompletedTask;
    }
}
// message needs a type discriminator property to determine the message type and route to appropriate handler and switch logic based on the decision type
//{ "Type": "MessageA", "Data": "Hello World" }
//{ "Type": "MessageB", "Count": 6 }

public static class MessageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BaseMessage? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Type", out var typeProp))
            throw new InvalidOperationException("Message type not specified.");

        var typeName = typeProp.GetString(); // this can be used like decision type
        return typeName switch
        {
            nameof(MessageA) => JsonSerializer.Deserialize<MessageA>(json, JsonOptions),
            nameof(MessageB) => JsonSerializer.Deserialize<MessageB>(json, JsonOptions),
            _ => throw new NotSupportedException($"Unknown message type: {typeName}")
        };
    }
}
