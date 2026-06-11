using System.Text;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

public sealed class RulesEngineWorker : BackgroundService
{
    private readonly ILogger<RulesEngineWorker> _logger;
    private readonly QueueClient _queueClient;
    private readonly RulesEngineOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRulesProvider _rulesProvider;
    private readonly IRulesEngine _rulesEngine;
    private readonly IRuleContextMapper _contextMapper;

    public RulesEngineWorker(
        ILogger<RulesEngineWorker> logger,
        QueueServiceClient queueServiceClient,
        IOptions<RulesEngineOptions> options,
        IServiceScopeFactory scopeFactory,
        IRulesProvider rulesProvider,
        IRulesEngine rulesEngine,
        IRuleContextMapper contextMapper)
    {
        if (options?.Value == null)
            throw new ArgumentException("RulesEngineOptions are required. Configure the 'RulesEngineOptions' section in appsettings.json or via environment variables.");

        _options = options.Value;
        _logger = logger;
        _queueClient = queueServiceClient.GetQueueClient(_options.QueueName);
        _scopeFactory = scopeFactory;
        _rulesProvider = rulesProvider;
        _rulesEngine = rulesEngine;
        _contextMapper = contextMapper;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Queue creation is inside the loop so a transient storage error at startup
        // is retried like any other failure rather than escaping ExecuteAsync. An
        // unhandled exception here would stop the host, and the worker has no
        // readiness probe, so the pod would crash-loop and the deploy rollout would
        // time out. Once created, the flag short-circuits the idempotent call.
        var queueReady = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!queueReady)
                {
                    await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
                    queueReady = true;
                }

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
            visibilityTimeout: _options.VisibilityTimeout,
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
            await ProcessMessageBodyAsync(messageBody, stoppingToken);
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

    /// <summary>
    /// Pure(-ish) processing path: parse → map → evaluate → handle. Extracted
    /// from <see cref="ProcessMessageAsync"/> so tests can drive it without
    /// constructing Azure SDK <c>QueueMessage</c> objects.
    /// </summary>
    internal async Task ProcessMessageBodyAsync(string messageBody, CancellationToken stoppingToken)
    {
        var parsed = RequestDocumentParser.Parse(messageBody)
            ?? throw new InvalidOperationException("Failed to parse message.");

        var snapshot = _rulesProvider.Current;
        Decision decision;
        try
        {
            var context = _contextMapper.Map(parsed);
            decision = _rulesEngine.Evaluate(snapshot.Rules, context, snapshot.Lookups);
        }
        catch (RuleContextMappingException ex)
        {
            _logger.LogError(ex,
                "Mapper rejected message for Reference={Reference}; routing to Scrutiny.", parsed.ReferenceNumber);
            decision = Decision.SyntheticScrutiny("_mapper_error", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Engine threw for Reference={Reference}; routing to Scrutiny.", parsed.ReferenceNumber);
            decision = Decision.SyntheticScrutiny("_engine_error", ex.GetType().Name + ": " + ex.Message);
        }

        _logger.LogInformation(
            "Decision={Status} Outcome={Outcome} Rule={Rule} RulesVersion={Version} Reference={Reference}",
            decision.Status, decision.OutcomeKey, decision.MatchedRuleId, snapshot.Version, parsed.ReferenceNumber);

        // The handler (and its Zendesk dependencies) are scoped, and a hosted
        // service is a singleton — resolve per message rather than capturing.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IRequestDecisionHandler>();
        await handler.HandleAsync(parsed, decision, stoppingToken);
    }
}

public sealed class RulesEngineOptions
{
    public int RetryDelayMs { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public int MaxMessagesPerPoll { get; set; }
    public int EmptyQueueDelayMs { get; set; }
    public long MaxDequeueCount { get; set; }
    public TimeSpan? VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
