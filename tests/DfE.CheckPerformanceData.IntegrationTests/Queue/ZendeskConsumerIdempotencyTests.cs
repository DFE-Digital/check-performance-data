using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Queue;

// The durable idempotency guard for the "ticket created" transition. The process-local
// optimisation is gone, so a redelivery that lands on a fresh worker scope (a new DbContext,
// as happens after a restart or a redrive) must still create no second ticket — the guarantee
// rests on the database, not on in-memory state.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class ZendeskConsumerIdempotencyTests
{
    private const string Reference = "REF-IDEM-001";

    private static readonly string Message = $$"""
    {
        "ReferenceNumber": "{{Reference}}",
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

    private readonly PostgresFixture _fixture;

    public ZendeskConsumerIdempotencyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RedeliveryAcrossScopes_CreatesNoSecondTicket_AndPersistsCrmIdOnce()
    {
        await ResetChangeRequestsAsync();
        await SeedChangeRequestAsync();

        var zendesk = Substitute.For<IZendeskService>();
        var ticketId = 0;
        zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
            .Returns(_ => new CreateTicketResponseDto
            {
                Ticket = new TicketDto { Id = Interlocked.Increment(ref ticketId) + 7000 }
            });

        // First delivery: a fresh scope (its own DbContext) creates the ticket and persists CrmId.
        await using (var ctx1 = _fixture.CreateContext())
        {
            var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx1);
            await consumer.ProcessMessageBodyAsync(Message, CancellationToken.None);
        }

        // Redelivery: a brand-new scope with NO shared in-memory state. The durable CrmId read
        // back from the database must make this a no-op.
        await using (var ctx2 = _fixture.CreateContext())
        {
            var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx2);
            await consumer.ProcessMessageBodyAsync(Message, CancellationToken.None);
        }

        await zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());

        await using var verify = _fixture.CreateContext();
        var saved = await verify.ChangeRequests.SingleAsync(r => r.ReferenceNumber == Reference);
        Assert.False(string.IsNullOrEmpty(saved.CrmId));
        Assert.Equal(RequestStatus.ZendeskTicketCreated, saved.Status);
    }

    [Fact]
    public async Task ConcurrentDeliveries_PersistAtMostOneCrmId_DurableConstraint()
    {
        await ResetChangeRequestsAsync();
        await SeedChangeRequestAsync();

        var zendesk = Substitute.For<IZendeskService>();
        var ticketId = 0;
        zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
            .Returns(async _ =>
            {
                // A real Zendesk create takes time; the delay widens the TOCTOU window so two
                // deliveries that both saw CrmId == null can both reach the post-create write.
                await Task.Delay(50);
                return new CreateTicketResponseDto
                {
                    Ticket = new TicketDto { Id = Interlocked.Increment(ref ticketId) + 9000 }
                };
            });

        // Two concurrent deliveries, each its own DbContext (separate worker scopes). Without a
        // durable unique constraint over CrmId, both can write a (different) ticket id, leaving a
        // duplicate ticket recorded. The DB-level unique partial index must reject the second.
        async Task DeliverAsync()
        {
            await using var ctx = _fixture.CreateContext();
            var consumer = new ZendeskConsumer(
                Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx);
            await consumer.ProcessMessageBodyAsync(Message, CancellationToken.None);
        }

        await Task.WhenAll(DeliverAsync(), DeliverAsync());

        // Only one delivery may win the "ticket created" transition; the loser must skip the
        // Zendesk call entirely. Exactly one ticket is created for the reference.
        await zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());

        await using var verify = _fixture.CreateContext();
        var saved = await verify.ChangeRequests.SingleAsync(r => r.ReferenceNumber == Reference);
        Assert.False(string.IsNullOrEmpty(saved.CrmId));
        Assert.Equal(RequestStatus.ZendeskTicketCreated, saved.Status);
    }

    private async Task<Guid> SeedChangeRequestAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Unspecified),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "Idempotency window",
        };
        ctx.CheckingWindows.Add(window);

        ctx.ChangeRequests.Add(new ChangeRequest
        {
            Id = Guid.NewGuid(),
            WindowId = window.Id,
            OrganisationUrn = 100000,
            Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = RequestStatus.RulesProcessed,
            ReferenceNumber = Reference,
            RequestType = "not-on-roll",
            DecisionStatus = DecisionStatus.Scrutiny,
        });
        await ctx.SaveChangesAsync();
        return window.Id;
    }

    private async Task ResetChangeRequestsAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"ChangeRequests\" WHERE \"ReferenceNumber\" = {0};", Reference);
    }
}
