using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

/// <summary>
/// The metrics sink is observability, not correctness: a sink that throws must never
/// propagate the failure into the dequeue loop. Recording happens after the message has
/// already been processed and acked, so a dropped metric row is tolerable but a thrown
/// exception that aborts processing is not.
/// </summary>
public sealed class MetricsSinkTests
{
    private const string ValidMessage = """
    {
        "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
        "ReferenceNumber": "REF-METRIC-001",
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

    // --- A throwing sink does not break message processing (RESEARCH Pitfall 2) ---

    [Fact]
    public async Task ThrowingSink_DoesNotPropagateIntoProcessing()
    {
        var sink = Substitute.For<IMetricsSink>();
        sink.RecordAsync(Arg.Any<QueueMetricEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("sink is down"));

        var consumer = new RulesConsumer(
            _queueService, _rulesProvider, _rulesEngine, _contextMapper, _dbContext, sink);

        var message = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = QueueOptions.RulesEngineQueue,
            Payload = ValidMessage,
            Attempts = 1,
            EnqueuedAt = DateTime.UtcNow.AddSeconds(-3),
        };

        // The post-ack record throws, but the consumer must swallow it and report success.
        var exception = await Record.ExceptionAsync(() =>
            consumer.RecordMetricSafelyAsync(message, deadLettered: false, CancellationToken.None));

        Assert.Null(exception);
    }

    // --- A successful record passes the consumer's stage/reference/decision through ---

    [Fact]
    public async Task SuccessfulRecord_PassesStageReferenceAndLatencyToSink()
    {
        var sink = Substitute.For<IMetricsSink>();

        var consumer = new RulesConsumer(
            _queueService, _rulesProvider, _rulesEngine, _contextMapper, _dbContext, sink);

        var message = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = QueueOptions.RulesEngineQueue,
            Payload = ValidMessage,
            Attempts = 1,
            EnqueuedAt = DateTime.UtcNow.AddSeconds(-5),
        };

        await consumer.RecordMetricSafelyAsync(message, deadLettered: false, CancellationToken.None);

        await sink.Received(1).RecordAsync(
            Arg.Is<QueueMetricEvent>(m =>
                m.QueueName == QueueOptions.RulesEngineQueue
                && m.Stage == MetricStages.RulesEvaluated
                && m.ReferenceNumber == "REF-METRIC-001"
                && m.MessageId == message.Id
                && m.LatencyMs > 0),
            Arg.Any<CancellationToken>());
    }

    // --- A dead-lettered message records a DeadLettered stage event ---

    [Fact]
    public async Task DeadLetteredMessage_RecordsDeadLetteredStage()
    {
        var sink = Substitute.For<IMetricsSink>();

        var consumer = new RulesConsumer(
            _queueService, _rulesProvider, _rulesEngine, _contextMapper, _dbContext, sink);

        var message = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = QueueOptions.RulesEngineQueue,
            Payload = ValidMessage,
            Attempts = 5,
            EnqueuedAt = DateTime.UtcNow.AddSeconds(-9),
        };

        await consumer.RecordMetricSafelyAsync(message, deadLettered: true, CancellationToken.None);

        await sink.Received(1).RecordAsync(
            Arg.Is<QueueMetricEvent>(m => m.Stage == MetricStages.DeadLettered),
            Arg.Any<CancellationToken>());
    }
}
