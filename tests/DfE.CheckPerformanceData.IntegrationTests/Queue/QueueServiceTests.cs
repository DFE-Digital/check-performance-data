using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.Queue;

namespace DfE.CheckPerformanceData.IntegrationTests.Queue;

[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class QueueServiceTests
{
    private readonly PostgresFixture _fixture;

    public QueueServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Enqueue inside a rolled-back transaction persists NO message (SC1) ---

    [Fact]
    public async Task EnqueueInsideRolledBackTransaction_LeavesNoMessage()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var sut = new PostgresQueueService(context);

        // Enqueue happens inside a transaction that the test deliberately abandons by
        // throwing before commit. An atomic outbox enqueue must not leak the message.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await context.ExecuteInTransactionAsync(async () =>
            {
                await sut.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "ABC123" });
                throw new InvalidOperationException("force rollback");
            });
        });

        await using var verifyContext = _fixture.CreateContext();
        var verifySut = new PostgresQueueService(verifyContext);
        var message = await verifySut.DequeueAsync(QueueOptions.RulesEngineQueue);

        Assert.Null(message);
    }

    // --- Two concurrent dequeues never return the same message (SC2) ---

    [Fact]
    public async Task ConcurrentDequeues_NeverReturnTheSameMessage()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var enqueueContext = _fixture.CreateContext();
        var enqueueSut = new PostgresQueueService(enqueueContext);
        await enqueueSut.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "ONLY-ONE" });

        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var sutA = new PostgresQueueService(contextA);
        var sutB = new PostgresQueueService(contextB);

        var dequeueA = sutA.DequeueAsync(QueueOptions.RulesEngineQueue);
        var dequeueB = sutB.DequeueAsync(QueueOptions.RulesEngineQueue);

        var results = await Task.WhenAll(dequeueA, dequeueB);

        // Exactly one consumer should receive the single message; the other gets null.
        var received = results.Where(m => m is not null).ToList();
        Assert.Single(received);
    }

    // --- A crash before ack re-surfaces the message after the visibility timeout (SC2) ---

    [Fact]
    public async Task CrashBeforeAck_MessageResurfacesAfterVisibilityTimeout_NoZombieLock()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var enqueueContext = _fixture.CreateContext();
        var enqueueSut = new PostgresQueueService(enqueueContext);
        await enqueueSut.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "RESURFACE" });

        // First consumer dequeues but "crashes" (never acks). The visibility timeout must
        // make the message available to a later dequeue — no permanently stranded lock. A
        // zero timeout collapses the wait so the recovery path is exercised without sleeping.
        var options = new QueueOptions { VisibilityTimeout = TimeSpan.Zero };

        await using var firstContext = _fixture.CreateContext();
        var firstSut = new PostgresQueueService(firstContext, options);
        var firstTake = await firstSut.DequeueAsync(QueueOptions.RulesEngineQueue);
        Assert.NotNull(firstTake);

        await using var laterContext = _fixture.CreateContext();
        var laterSut = new PostgresQueueService(laterContext, options);
        var resurfaced = await laterSut.DequeueAsync(QueueOptions.RulesEngineQueue);

        Assert.NotNull(resurfaced);
        Assert.Equal(firstTake!.Id, resurfaced!.Id);
    }

    // --- A message that only times out (never fails) is not dead-lettered (WR-05) ---

    [Fact]
    public async Task MessageThatTimesOutManyTimesWithoutFailing_IsNotDeadLettered()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var enqueueContext = _fixture.CreateContext();
        var enqueueSut = new PostgresQueueService(enqueueContext);
        await enqueueSut.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "SLOW" });

        // A zero visibility timeout makes every claim re-surface immediately, simulating a
        // message whose processing repeatedly ran past the visibility window. Attempts climbs
        // with each claim — MaxAttempts counts claims, not failures — but dead-lettering happens
        // only when processing actually throws. Re-claiming the message many times without ever
        // dead-lettering it must leave it on the queue, not in the DLQ.
        var options = new QueueOptions { VisibilityTimeout = TimeSpan.Zero };

        QueueMessage? claimed = null;
        for (var i = 0; i < QueueOptions.RulesEngineQueue.Length + 10; i++)
        {
            await using var ctx = _fixture.CreateContext();
            var sut = new PostgresQueueService(ctx, options);
            claimed = await sut.DequeueAsync(QueueOptions.RulesEngineQueue);
            Assert.NotNull(claimed);
        }

        // Attempts has climbed well past MaxAttempts purely from timed-out re-claims...
        Assert.True(claimed!.Attempts > new QueueOptions().MaxAttempts);

        // ...yet nothing is dead-lettered, because no processing failure ever occurred.
        await using var verify = _fixture.CreateContext();
        var dlq = await new QueueAdminService(new PostgresQueueService(verify)).GetDlqMessagesAsync();
        Assert.Empty(dlq);

        // The message is still claimable and can finally be acked on a successful processing.
        await using var ackCtx = _fixture.CreateContext();
        var ackSut = new PostgresQueueService(ackCtx, options);
        var finalTake = await ackSut.DequeueAsync(QueueOptions.RulesEngineQueue);
        Assert.NotNull(finalTake);
        await ackSut.AckAsync(finalTake!.Id);

        await using var afterAck = _fixture.CreateContext();
        Assert.Null(await new PostgresQueueService(afterAck, options).DequeueAsync(QueueOptions.RulesEngineQueue));
    }

    // --- The per-queue view-all pages in SQL with a total count, oldest first ---

    [Fact]
    public async Task GetQueueMessagesPage_PagesInSql_WithTotalCount()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using (var seed = _fixture.CreateContext())
        {
            var sut = new PostgresQueueService(seed);
            for (var i = 0; i < 23; i++)
                await sut.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = $"PAGE-{i:00}" });
        }

        await using var context = _fixture.CreateContext();
        var service = new PostgresQueueService(context);

        var page1 = await service.GetQueueMessagesPageAsync(QueueOptions.RulesEngineQueue, page: 1, pageSize: 20);

        Assert.Equal(23, page1.TotalCount);
        Assert.Equal(20, page1.Messages.Count);

        var page2 = await service.GetQueueMessagesPageAsync(QueueOptions.RulesEngineQueue, page: 2, pageSize: 20);

        Assert.Equal(23, page2.TotalCount);
        Assert.Equal(3, page2.Messages.Count);

        // The two pages share no message — the SQL OFFSET genuinely moved the window.
        var page1Ids = page1.Messages.Select(m => m.Id).ToHashSet();
        Assert.DoesNotContain(page2.Messages, m => page1Ids.Contains(m.Id));
    }
}
