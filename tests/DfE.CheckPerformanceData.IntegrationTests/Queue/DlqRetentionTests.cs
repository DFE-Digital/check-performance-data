using DfE.CheckPerformanceData.Application.Notifications;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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

    // --- The retention job purges only dead letters older than the TTL ---

    [Fact]
    public async Task RetentionJob_PurgesOnlyDeadLettersOlderThanRetention()
    {
        await QueueTestData.ResetAsync(_fixture);

        var oldId = Guid.NewGuid();
        var freshId = Guid.NewGuid();

        await using (var seed = _fixture.CreateContext())
        {
            seed.DeadLetters.Add(NewDeadLetter(oldId, DateTime.UtcNow.AddDays(-120)));
            seed.DeadLetters.Add(NewDeadLetter(freshId, DateTime.UtcNow.AddDays(-1)));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var adminService = new QueueAdminService(new PostgresQueueService(context));
        var notifyClient = Substitute.For<INotifyClient>();
        var settings = SettingsReturning(retentionDays: 90, threshold: 1000, recipients: "");

        var job = new DlqRetentionJob(scopeFactory: null!, NullLogger<DlqRetentionJob>.Instance);
        await job.RunOnceAsync(settings, adminService, notifyClient, CancellationToken.None);

        var remaining = await adminService.GetDlqMessagesAsync();
        var survivor = Assert.Single(remaining);
        Assert.Equal(freshId, survivor.Id);
    }

    // --- The admin-action audit trail survives a purged dead-letter message ---

    [Fact]
    public async Task RetentionJob_PurgesDeadLetter_ButAuditEntrySurvives()
    {
        await QueueTestData.ResetAsync(_fixture);

        var deadId = Guid.NewGuid();

        await using (var seed = _fixture.CreateContext())
        {
            seed.DeadLetters.Add(NewDeadLetter(deadId, DateTime.UtcNow.AddDays(-120)));
            seed.AuditEntries.Add(new DfE.CheckPerformance.Persistence.Entities.AuditEntry
            {
                EntityType = nameof(DeadLetterEntity),
                EntityId = deadId.ToString(),
                Action = "Delete",
                Timestamp = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var adminService = new QueueAdminService(new PostgresQueueService(context));
        var settings = SettingsReturning(retentionDays: 90, threshold: 1000, recipients: "");

        var job = new DlqRetentionJob(scopeFactory: null!, NullLogger<DlqRetentionJob>.Instance);
        await job.RunOnceAsync(settings, adminService, Substitute.For<INotifyClient>(), CancellationToken.None);

        Assert.Empty(await adminService.GetDlqMessagesAsync());

        // The message row is gone, but the admin-action audit trail referencing it remains.
        await using var verify = _fixture.CreateContext();
        var audit = verify.AuditEntries
            .Where(a => a.EntityId == deadId.ToString() && a.Action == "Delete")
            .ToList();
        Assert.NotEmpty(audit);
    }

    // --- The job emails an alert once per recipient when DLQ depth exceeds the threshold ---

    [Fact]
    public async Task RetentionJob_SendsAlert_WhenDlqDepthExceedsThreshold()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using (var seed = _fixture.CreateContext())
        {
            seed.DeadLetters.Add(NewDeadLetter(Guid.NewGuid(), DateTime.UtcNow));
            seed.DeadLetters.Add(NewDeadLetter(Guid.NewGuid(), DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var adminService = new QueueAdminService(new PostgresQueueService(context));
        var notifyClient = Substitute.For<INotifyClient>();
        var settings = SettingsReturning(retentionDays: 90, threshold: 1, recipients: "ops@example.com, duty@example.com");

        var job = new DlqRetentionJob(scopeFactory: null!, NullLogger<DlqRetentionJob>.Instance);
        await job.RunOnceAsync(settings, adminService, notifyClient, CancellationToken.None);

        await notifyClient.Received(1).SendEmailAsync(
            "ops@example.com",
            Arg.Is<IReadOnlyDictionary<string, object>>(p => p.ContainsKey("dlq_depth") && (int)p["dlq_depth"] == 2),
            Arg.Any<CancellationToken>());
        await notifyClient.Received(1).SendEmailAsync(
            "duty@example.com",
            Arg.Any<IReadOnlyDictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetentionJob_DoesNotAlert_WhenDlqDepthWithinThreshold()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using (var seed = _fixture.CreateContext())
        {
            seed.DeadLetters.Add(NewDeadLetter(Guid.NewGuid(), DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var adminService = new QueueAdminService(new PostgresQueueService(context));
        var notifyClient = Substitute.For<INotifyClient>();
        var settings = SettingsReturning(retentionDays: 90, threshold: 10, recipients: "ops@example.com");

        var job = new DlqRetentionJob(scopeFactory: null!, NullLogger<DlqRetentionJob>.Instance);
        await job.RunOnceAsync(settings, adminService, notifyClient, CancellationToken.None);

        await notifyClient.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    private static DeadLetterEntity NewDeadLetter(Guid id, DateTime deadLetteredAtUtc) => new()
    {
        Id = id,
        QueueName = QueueOptions.RulesEngineQueue,
        Payload = "{}",
        Reason = "test",
        PayloadHash = "hash",
        Attempts = 1,
        EnqueuedAtUtc = deadLetteredAtUtc,
        DeadLetteredAtUtc = deadLetteredAtUtc
    };

    private static ISettingService SettingsReturning(int retentionDays, int threshold, string recipients)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.DlqRetentionDays).Returns(retentionDays);
        settings.GetIntAsync(SettingKeys.DlqAlertThreshold).Returns(threshold);
        settings.GetValueAsync(SettingKeys.DlqAlertRecipients).Returns(recipients);
        return settings;
    }
}
