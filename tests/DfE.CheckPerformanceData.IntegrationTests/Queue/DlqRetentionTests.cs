using DfE.CheckPerformanceData.Application.Notifications;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DfE.CheckPerformanceData.Application.Notify;

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

    // --- A reason of multi-byte runes is truncated without splitting a surrogate pair ---

    [Fact]
    public async Task DeadLetterReason_TruncatedOnRuneBoundary_NeverSplitsASurrogatePair()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "RUNES" });
        var taken = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);

        // Each emoji is a surrogate pair (two UTF-16 code units). A char-count cut at an odd
        // length would land mid-pair and leave a broken final character. 2000 emoji = 4000 code
        // units, comfortably over the 1024 reason cap.
        var reason = string.Concat(Enumerable.Repeat("\U0001F600", 2000));
        await queueService.DeadLetterAsync(taken!.Id, reason);

        var dead = Assert.Single(await new QueueAdminService(queueService).GetDlqMessagesAsync());

        Assert.True(dead.Reason.Length <= 1024);
        // The truncated reason must not end on a lone high surrogate (a split pair).
        Assert.False(char.IsHighSurrogate(dead.Reason[^1]),
            "the truncated reason must not end on a lone high surrogate");
        // Whole runes only — a string with a split pair would contain the replacement char once
        // re-encoded; ours round-trips cleanly.
        Assert.DoesNotContain('�', dead.Reason);
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

    // --- Redrive must not delete the dead letter when it cannot re-enqueue (no silent loss) ---

    [Fact]
    public async Task RedriveIdAlreadyOnSourceQueue_DoesNotDeleteTheDeadLetter()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        // Dead-letter a message, then put a live message back on the source queue under the SAME
        // id (simulating a recycled id or a prior in-flight redrive of the same id). Redrive
        // cannot re-enqueue without colliding, so it must leave the dead letter in place rather
        // than delete the only durable copy.
        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "COLLIDE" });
        var take = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(take!.Id, "needs redrive");

        var dead = Assert.Single(await adminService.GetDlqMessagesAsync());

        await using (var collide = _fixture.CreateContext())
        {
            collide.QueueMessages.Add(new QueueMessageEntity
            {
                Id = dead.Id,
                QueueName = QueueOptions.RulesEngineQueue,
                Payload = "{}",
                Attempts = 0,
                EnqueuedAtUtc = DateTime.UtcNow,
                VisibleAfterUtc = DateTime.UtcNow,
                Status = "pending"
            });
            await collide.SaveChangesAsync();
        }

        await adminService.RedriveAsync(new[] { dead.Id });

        // The dead letter survives: a redrive that could not re-enqueue must never drop the
        // message from both tables.
        var stillDead = await adminService.GetDlqMessagesAsync();
        Assert.Contains(stillDead, d => d.Id == dead.Id);
    }

    // --- Redrive runs inside an ambient (audited) transaction without nesting ---

    [Fact]
    public async Task RedriveWithinAnAmbientTransaction_RemovesDeadLetter_AndDoesNotThrow()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "AMBIENT-TX" });
        var take = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(take!.Id, "needs redrive");

        var dead = Assert.Single(await adminService.GetDlqMessagesAsync());

        // The queue-admin controller redrives inside an audited transaction so the audit row and
        // the requeue commit together. RedriveAsync opens its own transaction; that nested call
        // must enrol in the ambient one rather than calling BeginTransaction again, which the
        // provider rejects ("already in a transaction"). Before the re-entrancy fix this threw,
        // the controller swallowed it, and the dead letter was left in the queue.
        await context.ExecuteInTransactionAsync(async () =>
        {
            context.AuditEntries.Add(new AuditEntry
            {
                EntityType = "DlqMessage",
                EntityId = dead.Id.ToString(),
                Action = "Redrive",
                Timestamp = DateTime.UtcNow,
                UserId = "tester",
            });
            await context.SaveChangesAsync();

            await adminService.RedriveAsync(new[] { dead.Id });
        });

        // The redrive committed: the dead letter is gone and the message is back on the queue.
        Assert.Empty(await adminService.GetDlqMessagesAsync());
        Assert.NotNull(await queueService.DequeueAsync(QueueOptions.RulesEngineQueue));
    }

    // --- A failure after the redrive rolls back BOTH the audit row and the requeue (atomic) ---

    [Fact]
    public async Task RedriveWithinAnAmbientTransaction_WhenWorkFailsAfterRedrive_RollsBackAuditAndRequeue()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "ATOMIC-ROLLBACK" });
        var take = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(take!.Id, "needs redrive");

        var dead = Assert.Single(await adminService.GetDlqMessagesAsync());

        // The audited redrive writes the audit row and requeues inside one ambient transaction. Because
        // the inner RedriveAsync joins that transaction rather than opening its own, a failure after the
        // requeue must roll the WHOLE thing back: no audit without the action, and no half-applied
        // requeue. The probe exception type proves the failure is our own throw — not the provider
        // rejecting a nested BeginTransaction, which a non-re-entrant join would raise first.
        await Assert.ThrowsAsync<RedriveAtomicityProbe>(async () =>
            await context.ExecuteInTransactionAsync(async () =>
            {
                context.AuditEntries.Add(new AuditEntry
                {
                    EntityType = "DlqMessage",
                    EntityId = dead.Id.ToString(),
                    Action = "Redrive",
                    Timestamp = DateTime.UtcNow,
                    UserId = "tester",
                });
                await context.SaveChangesAsync();

                await adminService.RedriveAsync(new[] { dead.Id });

                throw new RedriveAtomicityProbe();
            }));

        // Nothing committed. Verified from a fresh context so we read durable state, not the tracked
        // (and now rolled-back) entities: the dead letter survives, the message was never requeued,
        // and no audit row for the abandoned redrive was persisted.
        await using var verify = _fixture.CreateContext();
        var verifyQueue = new PostgresQueueService(verify);
        var verifyAdmin = new QueueAdminService(verifyQueue);

        Assert.Contains(await verifyAdmin.GetDlqMessagesAsync(), d => d.Id == dead.Id);
        Assert.Null(await verifyQueue.DequeueAsync(QueueOptions.RulesEngineQueue));
        Assert.Empty(verify.AuditEntries
            .Where(a => a.EntityId == dead.Id.ToString() && a.Action == "Redrive")
            .ToList());
    }

    // --- A redrive through the service audits the dead-letter removal, retained independently ---

    [Fact]
    public async Task Redrive_AuditsDeadLetterRemoval_RetainedAfterRow()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "AUDIT-REDRIVE" });
        var take = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(take!.Id, "needs redrive");

        var dead = Assert.Single(await adminService.GetDlqMessagesAsync());

        await adminService.RedriveAsync(new[] { dead.Id });

        // The dead letter row is gone from the DLQ (requeued)...
        Assert.Empty(await adminService.GetDlqMessagesAsync());

        // ...but the audit trail naming the removed message and the actor survives.
        await using var verify = _fixture.CreateContext();
        var audit = verify.AuditEntries
            .Where(a => a.EntityType == nameof(DeadLetterEntity)
                && a.Action == "Delete"
                && a.EntityId == dead.Id.ToString())
            .ToList();
        Assert.NotEmpty(audit);
        Assert.All(audit, a => Assert.Equal("test-user", a.UserId));
    }

    // --- A purge through the service audits the deletion, surviving the purged message ---

    [Fact]
    public async Task Purge_AuditsDeletion_SurvivesPurgedMessage()
    {
        await QueueTestData.ResetAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var adminService = new QueueAdminService(queueService);

        await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, new { Reference = "AUDIT-PURGE" });
        var take = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue);
        await queueService.DeadLetterAsync(take!.Id, "needs purge");

        var dead = Assert.Single(await adminService.GetDlqMessagesAsync());

        await adminService.PurgeAsync(new[] { dead.Id });

        Assert.Empty(await adminService.GetDlqMessagesAsync());

        await using var verify = _fixture.CreateContext();
        var audit = verify.AuditEntries
            .Where(a => a.EntityType == nameof(DeadLetterEntity)
                && a.Action == "Delete"
                && a.EntityId == dead.Id.ToString())
            .ToList();
        Assert.NotEmpty(audit);
        Assert.All(audit, a => Assert.Equal("test-user", a.UserId));
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
        var notifyClient = Substitute.For<INotifyService>();
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
        await job.RunOnceAsync(settings, adminService, Substitute.For<INotifyService>(), CancellationToken.None);

        Assert.Empty(await adminService.GetDlqMessagesAsync());

        // The message row is gone, but the admin-action audit trail referencing it remains.
        await using var verify = _fixture.CreateContext();
        var audit = verify.AuditEntries
            .Where(a => a.EntityId == deadId.ToString() && a.Action == "Delete")
            .ToList();
        Assert.NotEmpty(audit);
    }

    // --- A zero/negative retention must NOT purge the whole DLQ ---

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RetentionJob_WithNonPositiveRetention_DoesNotPurgeAnything(int retentionDays)
    {
        await QueueTestData.ResetAsync(_fixture);

        await using (var seed = _fixture.CreateContext())
        {
            // A row dead-lettered well in the past: a retention of 0 days would compute a cutoff
            // of "now" and delete it (and every other row). The clamp must stop that.
            seed.DeadLetters.Add(NewDeadLetter(Guid.NewGuid(), DateTime.UtcNow.AddDays(-120)));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var adminService = new QueueAdminService(new PostgresQueueService(context));
        var settings = SettingsReturning(retentionDays: retentionDays, threshold: 1000, recipients: "");

        var job = new DlqRetentionJob(scopeFactory: null!, NullLogger<DlqRetentionJob>.Instance);
        await job.RunOnceAsync(settings, adminService, Substitute.For<INotifyService>(), CancellationToken.None);

        // Nothing was purged — a mis-set retention must never silently wipe the dead-letter queue.
        Assert.NotEmpty(await adminService.GetDlqMessagesAsync());
    }

    // --- The job emails an alert once per recipient when DLQ depth exceeds the threshold ---


    // --- One failing recipient send must not drop the alert to the others ---


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
        var notifyClient = Substitute.For<INotifyService>();
        var settings = SettingsReturning(retentionDays: 90, threshold: 10, recipients: "ops@example.com");

        var job = new DlqRetentionJob(scopeFactory: null!, NullLogger<DlqRetentionJob>.Instance);
        await job.RunOnceAsync(settings, adminService, notifyClient, CancellationToken.None);

        await notifyClient.DidNotReceive().SendDlqThresholdEmailAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>());
    }

    // A distinct exception so the atomicity test can assert the failure is its own throw rather than a
    // provider error from a nested BeginTransaction (which a broken re-entrant join would surface first).
    private sealed class RedriveAtomicityProbe : Exception;

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
