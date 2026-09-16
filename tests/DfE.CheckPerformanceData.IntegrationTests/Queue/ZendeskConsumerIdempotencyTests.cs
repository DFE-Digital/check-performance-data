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
public sealed class ZendeskConsumerIdempotencyTests
{
    private const string Reference = "REF-IDEM-001";

    private static readonly string Message = $$"""
    {
        "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
        "ReferenceNumber": "{{Reference}}",
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
        Assert.Equal(WorkerStatus.ZendeskTicketCreated, saved.WorkerStatus);
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

        // The loser of the claim throws so the queue retries it later (by which time the winner's
        // CrmId makes the retry a no-op) — it must never be acked as if handled. Either delivery
        // may lose, so both are awaited individually and at most one may throw.
        var outcomes = await Task.WhenAll(
            Task.Run(async () => { try { await DeliverAsync(); return true; } catch (InvalidOperationException) { return false; } }),
            Task.Run(async () => { try { await DeliverAsync(); return true; } catch (InvalidOperationException) { return false; } }));
        Assert.True(outcomes.Count(won => won) >= 1);

        // Only one delivery may win the "ticket created" transition; the loser must skip the
        // Zendesk call entirely. Exactly one ticket is created for the reference.
        await zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());

        await using var verify = _fixture.CreateContext();
        var saved = await verify.ChangeRequests.SingleAsync(r => r.ReferenceNumber == Reference);
        Assert.False(string.IsNullOrEmpty(saved.CrmId));
        Assert.Equal(WorkerStatus.ZendeskTicketCreated, saved.WorkerStatus);
    }

    // The retry path. Attempt 1 claims the row (RulesProcessed -> ZendeskTicketCreating) and then
    // Zendesk fails. The redelivery must re-claim and create the ticket. It did not: the claim
    // insisted on RulesProcessed, so attempt 2 matched no row, returned as if handled, and the
    // message was acked away — 120 preprod requests vanished this way with nothing dead-lettered.
    [Fact]
    public async Task RedeliveryAfterFailedCreate_ReclaimsAndCreatesTheTicket()
    {
        await ResetChangeRequestsAsync();
        await SeedChangeRequestAsync();

        var zendesk = Substitute.For<IZendeskService>();
        var calls = 0;
        zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
            .Returns(_ => ++calls == 1
                ? throw new ZendeskApiException("Failed to create ticket.")
                : new CreateTicketResponseDto { Ticket = new TicketDto { Id = 7100 } });

        await using (var ctx1 = _fixture.CreateContext())
        {
            var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx1);
            await Assert.ThrowsAsync<ZendeskApiException>(
                () => consumer.ProcessMessageBodyAsync(Message, CancellationToken.None));
        }

        await using (var ctx2 = _fixture.CreateContext())
        {
            var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx2);
            await consumer.ProcessMessageBodyAsync(Message, CancellationToken.None);
        }

        await zendesk.Received(2).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());

        await using var verify = _fixture.CreateContext();
        var saved = await verify.ChangeRequests.SingleAsync(r => r.ReferenceNumber == Reference);
        Assert.Equal("7100", saved.CrmId);
        Assert.Equal(WorkerStatus.ZendeskTicketCreated, saved.WorkerStatus);
    }

    // A results enquiry never passes the rules engine, so its WorkerStatus is null by design. It
    // must be ticketed, with DeriveDecision's Scrutiny fallback, rather than dropped by the claim.
    [Fact]
    public async Task EnquiryRowWithNoRulesDecision_IsStillTicketed()
    {
        await ResetChangeRequestsAsync();
        await SeedChangeRequestAsync(workerStatus: null, requestType: RequestType.ResultsEnquiry);

        var zendesk = Substitute.For<IZendeskService>();
        zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
            .Returns(new CreateTicketResponseDto { Ticket = new TicketDto { Id = 7200 } });

        await using (var ctx = _fixture.CreateContext())
        {
            var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx);
            await consumer.ProcessMessageBodyAsync(Message, CancellationToken.None);
        }

        await zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());

        await using var verify = _fixture.CreateContext();
        var saved = await verify.ChangeRequests.SingleAsync(r => r.ReferenceNumber == Reference);
        Assert.Equal("7200", saved.CrmId);
        Assert.Equal(WorkerStatus.ZendeskTicketCreated, saved.WorkerStatus);
    }

    // An AMENDMENT with no rules decision is not claimable (SC-005): its decision was never
    // recorded, so it must not be ticketed under a guess. But it must not be acked away either —
    // throwing sends it to the DLQ with a reason an admin can act on.
    [Fact]
    public async Task AmendmentRowWithNoRulesDecision_ThrowsSoTheMessageIsRetriedNotAcked()
    {
        await ResetChangeRequestsAsync();
        await SeedChangeRequestAsync(workerStatus: null, requestType: RequestType.Amendment);

        var zendesk = Substitute.For<IZendeskService>();

        await using var ctx = _fixture.CreateContext();
        var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.ProcessMessageBodyAsync(Message, CancellationToken.None));
        await zendesk.DidNotReceive().CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
    }

    // A row stuck in ZendeskTicketCreating with no ticket id is an earlier attempt that died
    // mid-call (or another worker still in flight). Returning quietly acks the message and
    // loses the request; throwing makes the queue retry and, if the row stays stuck, dead-letter
    // it with a reason an admin can see.
    [Fact]
    public async Task RowStuckInCreating_ThrowsSoTheMessageIsRetriedNotAcked()
    {
        await ResetChangeRequestsAsync();
        await SeedChangeRequestAsync(workerStatus: WorkerStatus.ZendeskTicketCreating);

        var zendesk = Substitute.For<IZendeskService>();

        await using var ctx = _fixture.CreateContext();
        var consumer = new ZendeskConsumer(Substitute.For<DfE.CheckPerformanceData.Application.Queue.IQueueService>(), zendesk, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.ProcessMessageBodyAsync(Message, CancellationToken.None));
        await zendesk.DidNotReceive().CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
    }

    private async Task<Guid> SeedChangeRequestAsync(
        WorkerStatus? workerStatus = WorkerStatus.RulesProcessed,
        RequestType requestType = RequestType.Amendment)
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
            WorkerStatus = workerStatus,
            Status = RequestStatus.SubmittedCommitted,
            ReferenceNumber = Reference,
            RequestType = requestType,
            RequestTypeDescription = "Not on roll",
            Outcome = DecisionStatus.Scrutiny,
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
