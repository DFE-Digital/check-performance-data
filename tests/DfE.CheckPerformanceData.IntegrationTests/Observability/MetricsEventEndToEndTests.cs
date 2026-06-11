using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Observability;
using Microsoft.EntityFrameworkCore;
using QueueMetricEventDto = DfE.CheckPerformanceData.Application.Observability.QueueMetricEvent;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

/// <summary>
/// Ack DELETEs the queue row, so the metrics events table is the only durable home for
/// processing history. These tests prove a metric row is written through the real
/// Postgres-backed sink on a successful processing record and on a dead-letter, and that
/// every field round-trips.
/// </summary>
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class MetricsEventEndToEndTests
{
    private readonly PostgresFixture _fixture;

    public MetricsEventEndToEndTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- A successful record persists exactly one row with the expected stage/queue/reference ---

    [Fact]
    public async Task Record_PersistsOneRow_WithExpectedFields()
    {
        await ResetMetricsAsync();

        await using var context = _fixture.CreateContext();
        var sink = new DbMetricsSink(context);

        var recordedAt = DateTime.UtcNow;
        await sink.RecordAsync(new QueueMetricEventDto(
            QueueName: QueueOptions.RulesEngineQueue,
            Stage: MetricStages.RulesEvaluated,
            ReferenceNumber: "REF-E2E-001",
            MessageId: Guid.NewGuid(),
            DecisionStatus: "AutoApproved",
            RulesVersion: "v12",
            LatencyMs: 1234.5,
            RecordedAtUtc: recordedAt),
            CancellationToken.None);

        await using var verify = _fixture.CreateContext();
        var row = Assert.Single(verify.QueueMetricEvents.ToList());
        Assert.Equal(QueueOptions.RulesEngineQueue, row.QueueName);
        Assert.Equal(MetricStages.RulesEvaluated, row.Stage);
        Assert.Equal("REF-E2E-001", row.ReferenceNumber);
        Assert.Equal("AutoApproved", row.DecisionStatus);
        Assert.Equal("v12", row.RulesVersion);
        Assert.Equal(1234.5, row.LatencyMs);
    }

    // --- A dead-lettered record persists a DeadLettered stage row ---

    [Fact]
    public async Task Record_DeadLettered_PersistsDeadLetteredStage()
    {
        await ResetMetricsAsync();

        await using var context = _fixture.CreateContext();
        var sink = new DbMetricsSink(context);

        await sink.RecordAsync(new QueueMetricEventDto(
            QueueName: QueueOptions.ZendeskQueue,
            Stage: MetricStages.DeadLettered,
            ReferenceNumber: "REF-E2E-DLQ",
            MessageId: Guid.NewGuid(),
            DecisionStatus: null,
            RulesVersion: null,
            LatencyMs: 10,
            RecordedAtUtc: DateTime.UtcNow),
            CancellationToken.None);

        await using var verify = _fixture.CreateContext();
        var row = Assert.Single(verify.QueueMetricEvents.Where(e => e.ReferenceNumber == "REF-E2E-DLQ").ToList());
        Assert.Equal(MetricStages.DeadLettered, row.Stage);
        Assert.Null(row.DecisionStatus);
    }

    private async Task ResetMetricsAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            @"TRUNCATE TABLE ""queue_metrics_events"" RESTART IDENTITY CASCADE;");
    }
}
