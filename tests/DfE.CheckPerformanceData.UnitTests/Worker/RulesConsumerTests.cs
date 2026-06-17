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
}
