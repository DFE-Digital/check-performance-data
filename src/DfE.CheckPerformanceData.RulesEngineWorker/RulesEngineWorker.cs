using System.Text;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

public class RulesEngineWorker : BackgroundService
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
            _logger.LogInformation("Processing message: {MessageContents}", Encoding.UTF8.GetString(message.Body));
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

public class RulesEngineOptions
{
    public int RetryDelayMs { get; set; }
    public string QueueName { get; set; }
    public int MaxMessagesPerPoll { get; set; }
    public int EmptyQueueDelayMs { get; set; }
    public long MaxDequeueCount { get; set; }
}