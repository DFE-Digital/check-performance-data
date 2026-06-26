using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

/// <summary>
/// The per-message facts a consumer contributes to a metric row: which stage it represents,
/// the request reference it concerns and — where the consumer knows them — the decision the
/// rules engine reached and the rules version that produced it. The queue, message id, dwell
/// latency and timing (post-ack vs dead-letter) are owned by the base loop.
/// </summary>
public readonly record struct MetricDescription(
    string Stage,
    string ReferenceNumber,
    string? DecisionStatus = null,
    string? RulesVersion = null);

/// <summary>
/// Shared dequeue loop for the queue consumers: claim a message, process it,
/// ack on success or dead-letter once it exceeds the configured attempt cap.
/// Cancellation is honoured cleanly and transient errors back off by the
/// configured retry delay rather than tearing down the host.
///
/// When a <see cref="IServiceScopeFactory"/> is supplied (the hosting path), each
/// message is processed inside its own DI scope so scoped collaborators such as the
/// DbContext and queue service get a fresh, per-message instance. When it is absent
/// (the unit-test path) the directly-injected collaborators are used as-is.
/// </summary>
public abstract class ConsumerBase : BackgroundService
{
    private readonly IQueueService? _queueService;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly QueueOptions _options;
    private readonly ILogger _logger;
    private IMetricsSink? _metricsSink;

    /// <summary>
    /// Supplies the metrics sink used by the directly-injected (unit-test) path. The hosting
    /// path resolves the sink per message from the scope instead.
    /// </summary>
    protected void UseMetricsSink(IMetricsSink? metricsSink) => _metricsSink = metricsSink;

    /// <summary>
    /// The per-message stage and reference this consumer contributes to a metric row.
    /// Returning <c>null</c> suppresses the metric for that message. Implementations must not
    /// throw — a parse failure should return <c>null</c>, not propagate.
    /// </summary>
    protected abstract MetricDescription? DescribeMetric(string payload, bool deadLettered);

    /// <summary>
    /// Records a metric for a processed message. Observability is not correctness: any sink
    /// failure is logged and swallowed so it can never roll back or abort message processing.
    /// The caller invokes this only after a successful ack or a dead-letter, outside any
    /// dequeue transaction.
    /// </summary>
    public async Task RecordMetricSafelyAsync(QueueMessage message, bool deadLettered, CancellationToken cancellationToken)
    {
        if (_metricsSink is null)
        {
            return;
        }

        try
        {
            var description = DescribeMetric(message.Payload, deadLettered);
            if (description is null)
            {
                return;
            }

            var stage = deadLettered ? MetricStages.DeadLettered : description.Value.Stage;
            var latencyMs = Math.Max(0, (DateTime.UtcNow - message.EnqueuedAt).TotalMilliseconds);

            await _metricsSink.RecordAsync(new QueueMetricEvent(
                QueueName: message.QueueName,
                Stage: stage,
                ReferenceNumber: description.Value.ReferenceNumber,
                MessageId: message.Id,
                DecisionStatus: description.Value.DecisionStatus,
                RulesVersion: description.Value.RulesVersion,
                LatencyMs: latencyMs,
                RecordedAtUtc: DateTime.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Metrics sink failed recording {Queue}/{MessageId}; continuing.",
                message.QueueName, message.Id);
        }
    }

    protected ConsumerBase(IQueueService queueService, IOptions<QueueOptions> options, ILogger logger)
    {
        _queueService = queueService;
        _options = options.Value;
        _logger = logger;
    }

    protected ConsumerBase(IServiceScopeFactory scopeFactory, IOptions<QueueOptions> options, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Queue this consumer claims messages from.</summary>
    protected abstract string QueueName { get; }

    /// <summary>
    /// Process a single message body. Throwing routes the message to retry/DLQ.
    /// </summary>
    public abstract Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken);

    protected virtual Task ProcessMessageBodyAsync(
        string messageBody, IServiceProvider? services, CancellationToken cancellationToken) =>
        ProcessMessageBodyAsync(messageBody, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling queue {Queue}", QueueName);
                await Task.Delay(_options.RetryDelay, stoppingToken);
            }
        }
    }

    // A data-minimised dead-letter reason: the exception type and a fixed message, never the
    // exception's own text (which can carry a pupil field). Triage detail lives in the logs.
    private static string SanitiseReason(Exception ex) =>
        $"{ex.GetType().Name}: processing failed after the maximum number of attempts.";

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        if (_scopeFactory is not null)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
            await PollOnceAsync(queueService, scope.ServiceProvider, stoppingToken);
        }
        else
        {
            await PollOnceAsync(_queueService!, services: null, stoppingToken);
        }
    }

    private async Task PollOnceAsync(IQueueService queueService, IServiceProvider? services, CancellationToken stoppingToken)
    {
        var message = await queueService.DequeueAsync(QueueName, stoppingToken);
        if (message is null)
        {
            await Task.Delay(_options.RetryDelay, stoppingToken);
            return;
        }

        // In the hosting path the sink is scoped, so rebind it per message; the unit-test
        // path keeps the directly-injected instance (or none).
        if (services is not null)
        {
            _metricsSink = services.GetService<IMetricsSink>();
        }

        try
        {
            await ProcessMessageBodyAsync(message.Payload, services, stoppingToken);
            await queueService.AckAsync(message.Id, stoppingToken);

            // Recorded after ack, outside any dequeue transaction: a sink failure must never
            // roll back the message that was just processed.
            await RecordMetricSafelyAsync(message, deadLettered: false, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process message {MessageId} on {Queue} (attempt {Attempt})",
                message.Id, QueueName, message.Attempts);

            if (message.Attempts >= _options.MaxAttempts)
            {
                // The DLQ reason is rendered unredacted on the dead-letter list view with no
                // full-payload gate, so a raw ex.Message (which can carry a pupil field from a
                // mapping/parse failure) must never reach it. Store the exception type plus a
                // fixed message; the data-bearing detail stays in the logs above, behind the
                // worker's access controls.
                await queueService.DeadLetterAsync(message.Id, SanitiseReason(ex), stoppingToken);
                await RecordMetricSafelyAsync(message, deadLettered: true, stoppingToken);
            }
        }
    }
}
