using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Observability;
using Microsoft.EntityFrameworkCore;
using MetricEntity = DfE.CheckPerformance.Persistence.Entities.QueueMetricEvent;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// The demo-traffic purge removes synthetic demo rows (DEV-/SEED-/demo-fail-/uat-dlq-/e2e-dlq-)
// from queue_metrics_events, queue_dead_letters and queue_messages while leaving real submissions
// untouched. Real Postgres so the ILIKE prefix / payload matching is exercised against the engine.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class DemoTrafficPurgerTests
{
    private readonly PostgresFixture _fixture;

    public DemoTrafficPurgerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Purge_RemovesEveryDemoPrefix_FromAllThreeTables_AndKeepsRealSubmissions()
    {
        await ResetAsync();

        var anchor = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        // Metric events: one per demo prefix, plus a real submission.
        await SeedMetricsAsync(
            Metric("DEV-aaa", anchor),
            Metric("SEED-20260619-bbb", anchor),
            Metric("demo-fail-scrutiny-ccc", anchor),
            Metric("uat-dlq-ddd", anchor),
            Metric("e2e-dlq-eee", anchor),
            Metric("load-fff", anchor),               // self load-test traffic
            Metric("2026-REAL-001", anchor)); // a genuine submission reference

        // Dead-letters and queue messages carry the reference inside their JSON payload.
        await SeedQueueRowsAsync(
            ("DEV-aaa", true), ("uat-dlq-ddd", true), ("2026-REAL-001", true),
            ("SEED-20260619-bbb", false), ("2026-REAL-002", false));

        var result = await new DemoTrafficPurger(_fixture.CreateContext()).PurgeAsync(CancellationToken.None);

        await using var ctx = _fixture.CreateContext();

        // Every demo metric event is gone; the real one survives.
        var metricRefs = await ctx.QueueMetricEvents.Select(e => e.ReferenceNumber).ToListAsync();
        Assert.Equal(new[] { "2026-REAL-001" }, metricRefs);
        Assert.Equal(6, result.MetricEvents);

        // Demo dead-letters gone, real one survives.
        var dlqPayloads = await ctx.DeadLetters.Select(d => d.Payload).ToListAsync();
        Assert.All(dlqPayloads, p => Assert.DoesNotContain("DEV-", p));
        Assert.All(dlqPayloads, p => Assert.DoesNotContain("uat-dlq-", p));
        Assert.Contains(dlqPayloads, p => p.Contains("2026-REAL-001"));
        Assert.Equal(2, result.DeadLetters);

        // Demo queue messages gone, real one survives.
        var queuePayloads = await ctx.QueueMessages.Select(m => m.Payload).ToListAsync();
        Assert.All(queuePayloads, p => Assert.DoesNotContain("SEED-", p));
        Assert.Contains(queuePayloads, p => p.Contains("2026-REAL-002"));
        Assert.Equal(1, result.QueueMessages);
    }

    [Fact]
    public async Task Purge_OnAnAlreadyCleanDatabase_RemovesNothing()
    {
        await ResetAsync();

        var anchor = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        await SeedMetricsAsync(Metric("2026-REAL-100", anchor));

        var result = await new DemoTrafficPurger(_fixture.CreateContext()).PurgeAsync(CancellationToken.None);

        Assert.Equal(0, result.Total);
        await using var ctx = _fixture.CreateContext();
        Assert.Equal(1, await ctx.QueueMetricEvents.CountAsync());
    }

    private static MetricEntity Metric(string reference, DateTime recordedAtUtc) => new()
    {
        QueueName = "rules-engine",
        Stage = "Submitted",
        ReferenceNumber = reference,
        MessageId = Guid.NewGuid(),
        RecordedAtUtc = recordedAtUtc,
    };

    private async Task SeedMetricsAsync(params MetricEntity[] events)
    {
        await using var context = _fixture.CreateContext();
        context.QueueMetricEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    // Seeds dead-letter and/or queue-message rows whose JSON payload embeds the reference, mirroring
    // how the demo seed path enqueues `{"Reference":"…"}`.
    private async Task SeedQueueRowsAsync(params (string Reference, bool DeadLetter)[] rows)
    {
        await using var context = _fixture.CreateContext();
        foreach (var (reference, deadLetter) in rows)
        {
            var payload = $"{{\"Reference\":\"{reference}\"}}";
            if (deadLetter)
            {
                context.DeadLetters.Add(new DeadLetterEntity
                {
                    Id = Guid.NewGuid(),
                    QueueName = "rules-engine",
                    Payload = payload,
                    Reason = "test",
                    PayloadHash = reference,
                    Attempts = 1,
                    EnqueuedAtUtc = DateTime.UtcNow,
                    DeadLetteredAtUtc = DateTime.UtcNow,
                });
            }
            else
            {
                context.QueueMessages.Add(new QueueMessageEntity
                {
                    Id = Guid.NewGuid(),
                    QueueName = "rules-engine",
                    Payload = payload,
                    Attempts = 0,
                    EnqueuedAtUtc = DateTime.UtcNow,
                    VisibleAfterUtc = DateTime.UtcNow,
                    Status = "Pending",
                });
            }
        }
        await context.SaveChangesAsync();
    }

    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE queue_metrics_events RESTART IDENTITY CASCADE;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM queue_dead_letters;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM queue_messages;");
    }
}
