using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.IntegrationTests.Queue;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.RetriedTransactions;

// #757: RedriveAsync adds a queue message with the id of the dead letter. A retry that keeps the
// failed attempt's message tracked would add a second one with the same id, and EF refuses.
[Collection(nameof(PostgresCollection))]
public sealed class QueueRedriveRetryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    // The requeued message stays in the rules engine queue. Other tests dequeue from that queue
    // without a reset, so this class leaves the queue tables empty.
    public Task DisposeAsync() => QueueTestData.ResetAsync(_fixture);

    private async Task<Guid> SeedDeadLetterAsync()
    {
        await QueueTestData.ResetAsync(_fixture);
        await using var ctx = _fixture.CreateContext();
        var queue = new PostgresQueueService(ctx);
        await queue.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "REDRIVE" });
        var taken = await queue.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queue.DeadLetterAsync(taken!.Id, "test");
        return taken.Id;
    }

    private async Task AssertRequeuedOnceAsync(Guid id)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.QueueMessages.CountAsync(m => m.Id == id));
        Assert.Equal(0, await verify.DeadLetters.CountAsync(d => d.Id == id));
    }

    [Fact]
    public async Task Redrive_that_is_retried_after_a_transient_fault_requeues_the_message_once()
    {
        var id = await SeedDeadLetterAsync();
        var interceptor = new FailFirstCommandInterceptor("INSERT INTO queue_messages");
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await new PostgresQueueService(ctx).RedriveAsync([id]);

        Assert.Equal(2, interceptor.Matches);
        await AssertRequeuedOnceAsync(id);
    }

    [Fact]
    public async Task Redrive_that_is_retried_after_a_failed_commit_requeues_the_message_once()
    {
        var id = await SeedDeadLetterAsync();
        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx = _fixture.CreateRetryingContext(interceptor);

        await new PostgresQueueService(ctx).RedriveAsync([id]);

        Assert.Equal(2, interceptor.Commits);
        await AssertRequeuedOnceAsync(id);
    }
}
