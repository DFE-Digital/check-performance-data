using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

public sealed class RulesConsumerTests
{
    private const string ValidMessage = """
    {
        "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
        "ReferenceNumber": "REF-001",
        "SubmittedAt": "2026-06-10T00:00:00Z",
        "SubmittedBy": { "UserId": "u1", "DisplayName": "Test User" },
        "CheckingWindowId": "11111111-1111-1111-1111-111111111111",
        "CheckingWindowType": "Spring",
        "RequestTypeCode": "not-on-roll",
        "School": { "Urn": "100000", "Name": "Test School" },
        "Pupil": {
            "Id": "p1", "CypmdId": "c1", "Firstname": "Ann", "Surname": "Bell",
            "DateOfBirth": "2015-01-01", "Sex": "F", "Age": 9, "Upn": "X123"
        },
        "Answers": []
    }
    """;

    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly IRulesProvider _rulesProvider = Substitute.For<IRulesProvider>();
    private readonly IRulesEngine _rulesEngine = Substitute.For<IRulesEngine>();
    private readonly IRuleContextMapper _contextMapper = Substitute.For<IRuleContextMapper>();
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();

    // The Zendesk service is held only to assert it is NEVER invoked by the Rules
    // consumer. The consumer is constructed without it on purpose.
    private readonly IZendeskService _zendesk = Substitute.For<IZendeskService>();

    private RulesConsumer CreateConsumer() =>
        new(_queueService, _rulesProvider, _rulesEngine, _contextMapper, _dbContext);

    // --- Rules consumer makes NO Zendesk call (SC10) ---

    [Fact]
    public async Task ProcessMessage_DoesNotCallZendesk()
    {
        var consumer = CreateConsumer();

        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);

        await _zendesk.DidNotReceive().CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
        Assert.Empty(_zendesk.ReceivedCalls());
    }

    // --- Rules consumer persists the decision + status in the dequeue transaction (D-10) ---

    [Fact]
    public async Task ProcessMessage_PersistsDecisionInTransaction()
    {
        var consumer = CreateConsumer();

        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);

        await _dbContext.Received().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    // --- Decision-mix analytics: the consumer emits a non-PII RequestDecisionEvent per message ---

    [Fact]
    public async Task ProcessMessage_EmitsRequestDecisionEvent_WithDecisionMetadata()
    {
        var analytics = Substitute.For<IAnalyticsService>();
        StubDecision(new Decision(DecisionStatus.AutoApproved, "Deceased", "EAL-1", []));
        var consumer = CreateConsumer(analytics);

        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);

        // Non-PII decision metadata, sourced from the decision, the rules snapshot, and the
        // parsed document. RequestTypeCode/CheckingWindowType come from ValidMessage;
        // RulesVersion from the stubbed snapshot.
        await analytics.Received(1).TrackAsync(
            Arg.Is<RequestDecisionEvent>(e =>
                e.DecisionStatus == "AutoApproved" &&
                e.OutcomeKey == "Deceased" &&
                e.MatchedRuleId == "EAL-1" &&
                e.RulesVersion == "v1" &&
                e.RequestTypeCode == "not-on-roll" &&
                e.CheckingWindowType == "Spring" &&
                !e.IsSyntheticFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessage_MarksDecisionEventSynthetic_WhenRuleIdIsFallback()
    {
        // Fallback decisions (mapper/engine error, unmatched outcome) carry a '_'-prefixed
        // MatchedRuleId; the event flags them so the metric can separate genuine rule
        // outcomes from error fallbacks.
        var analytics = Substitute.For<IAnalyticsService>();
        StubDecision(new Decision(DecisionStatus.Scrutiny, "_unknown", "_engine_error", []));
        var consumer = CreateConsumer(analytics);

        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);

        await analytics.Received(1).TrackAsync(
            Arg.Is<RequestDecisionEvent>(e => e.IsSyntheticFallback && e.DecisionStatus == "Scrutiny"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessage_StillPersists_WhenAnalyticsThrows()
    {
        // Analytics is observability; a sink failure must not throw out of processing (which
        // would leave the message to be retried/poisoned) nor undo the decision that was
        // already persisted in the dequeue transaction.
        var analytics = Substitute.For<IAnalyticsService>();
        analytics.TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("analytics sink down"));
        StubDecision(new Decision(DecisionStatus.Scrutiny, "k", "r", []));
        var consumer = CreateConsumer(analytics);

        // Does not throw.
        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);

        await _dbContext.Received().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    private RulesConsumer CreateConsumer(IAnalyticsService analytics) =>
        new(_queueService, _rulesProvider, _rulesEngine, _contextMapper, _dbContext, analytics: analytics);

    private void StubDecision(Decision decision)
    {
        _rulesProvider.Current.Returns(new RulesSnapshot(
            new RuleSet("v1", DateTimeOffset.UtcNow, []), Lookups.Empty, "v1", DateTimeOffset.UtcNow, RulesHealth.Healthy));
        _rulesEngine.Evaluate(Arg.Any<RuleSet>(), Arg.Any<RuleContext>(), Arg.Any<Lookups>()).Returns(decision);
    }
}
