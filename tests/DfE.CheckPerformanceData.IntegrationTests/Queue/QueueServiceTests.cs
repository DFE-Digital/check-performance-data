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
        // make the message available to a later dequeue — no permanently stranded lock.
        await using var firstContext = _fixture.CreateContext();
        var firstSut = new PostgresQueueService(firstContext);
        var firstTake = await firstSut.DequeueAsync(QueueOptions.RulesEngineQueue);
        Assert.NotNull(firstTake);

        await using var laterContext = _fixture.CreateContext();
        var laterSut = new PostgresQueueService(laterContext);
        var resurfaced = await laterSut.DequeueAsync(QueueOptions.RulesEngineQueue);

        Assert.NotNull(resurfaced);
        Assert.Equal(firstTake!.Id, resurfaced!.Id);
    }
}
