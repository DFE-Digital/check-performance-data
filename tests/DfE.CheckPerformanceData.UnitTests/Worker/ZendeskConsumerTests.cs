using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

public sealed class ZendeskConsumerTests
{
    private const string ValidMessage = """
    {
        "ReferenceNumber": "REF-DUP-001",
        "SubmittedAt": "2026-06-10T00:00:00Z",
        "SubmittedBy": { "UserId": "u1", "DisplayName": "Test User" },
        "CheckingWindowId": "11111111-1111-1111-1111-111111111111",
        "CheckingWindowType": "Spring",
        "WhatToChange": "not-on-roll",
        "School": { "Urn": "100000", "Name": "Test School" },
        "Pupil": {
            "Id": "p1", "CypmdId": "c1", "Firstname": "Ann", "Surname": "Bell",
            "DateOfBirth": "2015-01-01", "Sex": "F", "Age": 9, "Upn": "X123"
        },
        "Answers": []
    }
    """;

    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly IZendeskService _zendesk = Substitute.For<IZendeskService>();
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();

    private ZendeskConsumer CreateConsumer()
    {
        _zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
            .Returns(new CreateTicketResponseDto { Ticket = new TicketDto { Id = 4242 } });

        return new ZendeskConsumer(_queueService, _zendesk, _dbContext);
    }

    // --- Redelivery of the same message creates NO duplicate ticket (SC11) ---

    [Fact]
    public async Task Redelivery_DoesNotCreateDuplicateTicket()
    {
        var consumer = CreateConsumer();

        // First delivery creates the ticket; redelivery of the SAME reference must be a
        // no-op via the CrmId check-before-create. Net effect: exactly one ticket created.
        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);
        await consumer.ProcessMessageBodyAsync(ValidMessage, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
    }
}
