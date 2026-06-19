using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QueueMetricEventEntity = DfE.CheckPerformance.Persistence.Entities.QueueMetricEvent;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

/// <summary>
/// The metrics events table grows on every ack and dead-letter, so it needs a retention
/// job in the same shape as the dead-letter retention job: a settings-driven window, a
/// two-overload RunOnceAsync that the test drives directly with collaborators.
/// </summary>
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class MetricsRetentionTests
{
    private readonly PostgresFixture _fixture;

    public MetricsRetentionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- The retention job purges only metric rows older than the retention window ---

    [Fact]
    public async Task RetentionJob_PurgesOnlyRowsOlderThanRetention()
    {
        await ResetMetricsAsync();

        await using (var seed = _fixture.CreateContext())
        {
            seed.QueueMetricEvents.Add(NewEvent("OLD", DateTime.UtcNow.AddDays(-120)));
            seed.QueueMetricEvents.Add(NewEvent("FRESH", DateTime.UtcNow.AddDays(-1)));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sink = new DbMetricsSink(context);
        var settings = SettingsReturning(retentionDays: 30);

        var job = new MetricsRetentionJob(scopeFactory: null!, NullLogger<MetricsRetentionJob>.Instance);
        await job.RunOnceAsync(settings, sink, CancellationToken.None);

        await using var verify = _fixture.CreateContext();
        var survivor = Assert.Single(verify.QueueMetricEvents.ToList());
        Assert.Equal("FRESH", survivor.ReferenceNumber);
    }

    private static QueueMetricEventEntity NewEvent(string reference, DateTime recordedAtUtc) => new()
    {
        QueueName = QueueOptions.RulesEngineQueue,
        Stage = MetricStages.RulesEvaluated,
        ReferenceNumber = reference,
        MessageId = Guid.NewGuid(),
        LatencyMs = 1,
        RecordedAtUtc = recordedAtUtc
    };

    private static ISettingService SettingsReturning(int retentionDays)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.MetricsRetentionDays).Returns(retentionDays);
        return settings;
    }

    private async Task ResetMetricsAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            @"TRUNCATE TABLE ""queue_metrics_events"" RESTART IDENTITY CASCADE;");
    }
}
