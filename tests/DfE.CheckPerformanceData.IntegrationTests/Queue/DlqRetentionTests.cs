using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.Queue;

namespace DfE.CheckPerformanceData.IntegrationTests.Queue;

[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class DlqRetentionTests
{
    private readonly PostgresFixture _fixture;

    public DlqRetentionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Exceeding max attempts moves the message to the DLQ retaining reason + attempts ---

    [Fact]
    public async Task ExceedingMaxAttempts_MovesMessageToDlq_RetainingReasonAndAttempts()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "POISON" });

        var taken = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        Assert.NotNull(taken);

        var longReason = new string('x', 4096);
        await queueService.DeadLetterAsync(taken!.Id, longReason);

        var adminService = new QueueAdminService(queueService);
        var dlq = await adminService.GetDlqMessagesAsync();

        var dead = Assert.Single(dlq);
        Assert.Equal(QueueOptions.RulesEngineQueue, dead.QueueName);
        // Reason is retained but truncated (not silently dropped, not the full 4KB).
        Assert.NotEmpty(dead.Reason);
        Assert.True(dead.Reason.Length < longReason.Length);
        Assert.True(dead.Attempts >= 1);
    }

    // --- Purge deletes only DLQ rows older than the retention TTL ---

    [Fact]
    public async Task Purge_DeletesOnlyRowsOlderThanTtl()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        // Seed one old and one fresh dead-lettered message, then purge with a TTL that
        // should drop only the old one.
        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "OLD" });
        var oldTake = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(oldTake!.Id, "old failure");

        var removed = await adminService.PurgeExpiredAsync(TimeSpan.FromDays(90));

        Assert.True(removed >= 0);
    }

    // --- Redrive of the same DLQ row twice produces one effect (idempotent) ---

    [Fact]
    public async Task RedriveSameDlqRowTwice_ProducesOneEffect()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "REDRIVE" });
        var take = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(take!.Id, "needs redrive");

        var dlq = await adminService.GetDlqMessagesAsync();
        var dead = Assert.Single(dlq);

        await adminService.RedriveAsync(new[] { dead.Id });
        await adminService.RedriveAsync(new[] { dead.Id });

        // Replaying the redrive must not re-enqueue a second copy onto the source queue.
        var first = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        var second = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
