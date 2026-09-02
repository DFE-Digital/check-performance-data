using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

/// <summary>
/// Consumes change-request messages: parses the request, evaluates it against the
/// current rule set, persists the decision and status to the matching
/// <see cref="Persistence.Entities.ChangeRequest"/> and enqueues a downstream
/// ticket message — all atomically with the de-queue. It never talks to Zendesk.
/// A mapping or evaluation failure routes to a synthetic Scrutiny decision that is
/// still persisted, so a fault never silently auto-approves or auto-rejects.
/// </summary>
public sealed class RulesConsumer : ConsumerBase
{
    private readonly IRulesProvider _rulesProvider;
    private readonly IRulesEngine _rulesEngine;
    private readonly IRuleContextMapper _contextMapper;
    
    private readonly IPortalDbContext? _dbContext;
    private readonly IAnalyticsService? _analytics;
    private readonly ILogger _logger;

    // Test constructor: collaborators are injected directly. Internal so the DI
    // container only ever sees the public hosting constructor below.
    internal RulesConsumer(
        IQueueService queueService,
        IRulesProvider rulesProvider,
        IRulesEngine rulesEngine,
        IRuleContextMapper contextMapper,
        IPortalDbContext dbContext,
        IAnalyticsService? analytics = null)
        : this(queueService, rulesProvider, rulesEngine, contextMapper, dbContext, metricsSink: null, analytics: analytics)
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
        IMetricsSink? metricsSink,
        IAnalyticsService? analytics = null)
        : base(queueService, Options.Create(new QueueOptions()), NullLogger<RulesConsumer>.Instance)
    {
        _dbContext = dbContext;
        _analytics = analytics;
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

    // Upper bound on the persisted trace. The engine caps the number of trace lines
    // (RulesEngine.MaxTraceLines) but not their width: a leaf line renders its field's actual
    // value verbatim, and a free-text journey answer reaches the rule context intact, so one
    // line can be arbitrarily long. Truncating here rather than with a column length keeps an
    // outsized trace from failing the write - and therefore from re-queueing a message whose
    // decision is already made.
    private const int MaxPersistedTraceChars = 8000;

    // The decision and rules version are only known once ProcessAsync has evaluated the message,
    // but the metric is recorded by the base loop after via DescribeMetric. The base loop
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
        var dbContext = services?.GetRequiredService<IPortalDbContext>() ?? _dbContext!;
        // Analytics is observability, not a hard dependency: resolve optionally so a host
        // that has not registered it (or a unit test that does not care) simply skips emission.
        var analytics = services?.GetService<IAnalyticsService>() ?? _analytics;
        return ProcessAsync(messageBody, dbContext, analytics, cancellationToken);
    }

    public override Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken) =>
        ProcessAsync(messageBody, _dbContext!, _analytics, cancellationToken);

    private async Task ProcessAsync(
        string messageBody, IPortalDbContext dbContext,
        IAnalyticsService? analytics, CancellationToken cancellationToken)
    {
        var parsed = RequestDocumentParser.Parse(messageBody)
            ?? throw new InvalidOperationException("Failed to parse message.");

        var snapshot = _rulesProvider.Current;
        var rulesVersion = snapshot.Version;
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
            "Decision={Status} Outcome={Outcome} Rule={Rule} RulesVersion={Version} Reference={Reference}{NewLine1}Trace:{NewLine2}{Trace}",
            decision.Status,
            decision.OutcomeKey,
            decision.MatchedRuleId,
            rulesVersion,
            parsed.ReferenceNumber,
            Environment.NewLine,
            Environment.NewLine,
            decision.Trace.Count > 0 ? string.Join(Environment.NewLine, decision.Trace) : "(none)");

        // Capture the outcome so the post-ack metric record (DescribeMetric) can attach the
        // decision status and rules version for this reference.
        _lastOutcome = (parsed.ReferenceNumber, decision.Status.ToString(), rulesVersion);

        var decisionTrace = TruncateTrace(decision.Trace);

        await dbContext.ExecuteInTransactionAsync(async () =>
        {
            await dbContext.ChangeRequests
                .Where(r => r.ReferenceNumber == parsed.ReferenceNumber)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Outcome, decision.Status)
                    .SetProperty(r => r.OutcomeKey, decision.OutcomeKey)
                    .SetProperty(r => r.MatchedRuleId, decision.MatchedRuleId)
                    .SetProperty(r => r.RulesVersion, rulesVersion)
                    .SetProperty(r => r.DecisionTrace, decisionTrace)
                    .SetProperty(r => r.DecidedAtUtc, DateTime.UtcNow)
                    .SetProperty(r => r.WorkerStatus, WorkerStatus.RulesProcessed),
                    cancellationToken);
        }, cancellationToken);

        // Emit the decision-mix analytics event after the decision is durably persisted and
        // the ticket enqueued — never inside the transaction. Decision metadata only, no PII;
        // a synthetic (fallback) decision is marked by a '_'-prefixed MatchedRuleId.
        // Best-effort: a sink failure must never fail processing (which would re-queue/poison
        // the message) — the message has already been handled by this point.
        if (analytics is not null)
        {
            try
            {
                await analytics.TrackAsync(new RequestDecisionEvent
                {
                    DecisionStatus = decision.Status.ToString(),
                    OutcomeKey = decision.OutcomeKey,
                    MatchedRuleId = decision.MatchedRuleId,
                    RulesVersion = rulesVersion,
                    RequestTypeCode = parsed.RequestTypeCode,
                    CheckingWindowType = parsed.CheckingWindowType,
                    IsSyntheticFallback = decision.MatchedRuleId.StartsWith('_'),
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to emit analytics decision event for Reference={Reference}; continuing.",
                    parsed.ReferenceNumber);
            }
        }
    }

    // Newline-joins the trace for storage, bounded by MaxPersistedTraceChars. An empty trace is
    // stored as null rather than "" so "no trace recorded" and "decided before this column
    // existed" read the same way to the admin page.
    internal static string? TruncateTrace(IReadOnlyList<string> trace)
    {
        if (trace is null || trace.Count == 0)
        {
            return null;
        }

        var joined = string.Join(Environment.NewLine, trace);
        return joined.Length <= MaxPersistedTraceChars
            ? joined
            : string.Concat(joined.AsSpan(0, MaxPersistedTraceChars), Environment.NewLine, "... trace truncated.");
    }
}
