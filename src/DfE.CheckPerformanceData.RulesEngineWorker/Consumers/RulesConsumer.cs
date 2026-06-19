using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

/// <summary>
/// Consumes change-request messages: parses the request, evaluates it against the
/// current rule set, persists the decision and status to the matching
/// <see cref="Persistence.Entities.ChangeRequest"/> and enqueues a downstream
/// ticket message — all atomically with the dequeue. It never talks to Zendesk.
/// A mapping or evaluation failure routes to a synthetic Scrutiny decision that is
/// still persisted, so a fault never silently auto-approves or auto-rejects.
/// </summary>
public sealed class RulesConsumer : ConsumerBase
{
    private readonly IRulesProvider _rulesProvider;
    private readonly IRulesEngine _rulesEngine;
    private readonly IRuleContextMapper _contextMapper;
    private readonly IQueueService? _queueService;
    private readonly IPortalDbContext? _dbContext;
    private readonly ILogger _logger;

    // Test constructor: collaborators are injected directly. Internal so the DI
    // container only ever sees the public hosting constructor below.
    internal RulesConsumer(
        IQueueService queueService,
        IRulesProvider rulesProvider,
        IRulesEngine rulesEngine,
        IRuleContextMapper contextMapper,
        IPortalDbContext dbContext)
        : this(queueService, rulesProvider, rulesEngine, contextMapper, dbContext, metricsSink: null)
    {
    }

    // Test constructor variant that also wires the metrics sink so the post-ack record can
    // be exercised directly. Internal so DI never sees it.
    internal RulesConsumer(
        IQueueService queueService,
        IRulesProvider rulesProvider,
        IRulesEngine rulesEngine,
        IRuleContextMapper contextMapper,
        IPortalDbContext dbContext,
        IMetricsSink? metricsSink)
        : base(queueService, Options.Create(new QueueOptions()), NullLogger<RulesConsumer>.Instance)
    {
        _queueService = queueService;
        _dbContext = dbContext;
        _rulesProvider = rulesProvider;
        _rulesEngine = rulesEngine;
        _contextMapper = contextMapper;
        _logger = NullLogger<RulesConsumer>.Instance;
        UseMetricsSink(metricsSink);
    }

    // Hosting constructor: scoped collaborators (queue + DbContext) are resolved
    // per message from a fresh scope; the rules engine collaborators are singletons.
    public RulesConsumer(
        IServiceScopeFactory scopeFactory,
        IRulesProvider rulesProvider,
        IRulesEngine rulesEngine,
        IRuleContextMapper contextMapper,
        IOptions<QueueOptions> options,
        ILogger<RulesConsumer> logger)
        : base(scopeFactory, options, logger)
    {
        _rulesProvider = rulesProvider;
        _rulesEngine = rulesEngine;
        _contextMapper = contextMapper;
        _logger = logger;
    }

    protected override string QueueName => QueueOptions.RulesEngineQueue;

    // The decision and rules version are only known once ProcessAsync has evaluated the message,
    // but the metric is recorded by the base loop afterwards via DescribeMetric. The base loop
    // processes one message at a time per consumer instance and records immediately after
    // processing, so stashing the most recent outcome here and attaching it when the reference
    // matches lets the decision flow through to the metric row without reparsing or re-evaluating.
    private (string Reference, string DecisionStatus, string? RulesVersion)? _lastOutcome;

    protected override MetricDescription? DescribeMetric(string payload, bool deadLettered)
    {
        var parsed = RequestDocumentParser.Parse(payload);
        if (parsed is null)
        {
            return null;
        }

        // A dead-letter never produced a decision; the rules-evaluated path attaches the outcome
        // captured during ProcessAsync, but only when it belongs to this same reference.
        var outcome = !deadLettered && _lastOutcome is { } o && o.Reference == parsed.ReferenceNumber
            ? o
            : default;

        return new MetricDescription(
            MetricStages.RulesEvaluated,
            parsed.ReferenceNumber,
            DecisionStatus: outcome.DecisionStatus,
            RulesVersion: outcome.RulesVersion);
    }

    protected override Task ProcessMessageBodyAsync(
        string messageBody, IServiceProvider? services, CancellationToken cancellationToken)
    {
        var queueService = services?.GetRequiredService<IQueueService>() ?? _queueService!;
        var dbContext = services?.GetRequiredService<IPortalDbContext>() ?? _dbContext!;
        return ProcessAsync(messageBody, queueService, dbContext, cancellationToken);
    }

    public override Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken) =>
        ProcessAsync(messageBody, _queueService!, _dbContext!, cancellationToken);

    private async Task ProcessAsync(
        string messageBody, IQueueService queueService, IPortalDbContext dbContext, CancellationToken cancellationToken)
    {
        var parsed = RequestDocumentParser.Parse(messageBody)
            ?? throw new InvalidOperationException("Failed to parse message.");

        var snapshot = _rulesProvider.Current;
        var rulesVersion = snapshot?.Version;
        Decision decision;
        try
        {
            var context = _contextMapper.Map(parsed);
            decision = _rulesEngine.Evaluate(snapshot!.Rules, context, snapshot.Lookups);
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
            decision.Status, decision.OutcomeKey, decision.MatchedRuleId, rulesVersion, parsed.ReferenceNumber);

        // Capture the outcome so the post-ack metric record (DescribeMetric) can attach the
        // decision status and rules version for this reference.
        _lastOutcome = (parsed.ReferenceNumber, decision.Status.ToString(), rulesVersion);

        await dbContext.ExecuteInTransactionAsync(async () =>
        {
            await dbContext.ChangeRequests
                .Where(r => r.ReferenceNumber == parsed.ReferenceNumber)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RequestStatus.RulesProcessed)
                    .SetProperty(r => r.Outcome, decision.Status)
                    .SetProperty(r => r.OutcomeKey, decision.OutcomeKey)
                    .SetProperty(r => r.MatchedRuleId, decision.MatchedRuleId)
                    .SetProperty(r => r.RulesVersion, rulesVersion)
                    .SetProperty(r => r.DecidedAtUtc, DateTime.UtcNow),
                    cancellationToken);

            await queueService.EnqueueAsync(QueueOptions.ZendeskQueue, parsed, cancellationToken);
        }, cancellationToken);
    }
}
