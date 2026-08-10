using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using MetricEntity = DfE.CheckPerformance.Persistence.Entities.QueueMetricEvent;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// The always-on board replay re-feeds the same animation engine from recorded
// queue_metrics_events. GetReplayWindowAsync returns the recorded transition events for a
// window, ordered chronologically, so the scrubber can play them back in the order they
// actually happened. Real Postgres so the ordering and window bounds are exercised against
// the real engine.
[Collection(nameof(PostgresCollection))]
public sealed class BoardReplayTests
{
    private const string RulesEngineQueue = "rules-engine";
    private const string ZendeskQueue = "zendesk";

    private readonly PostgresFixture _fixture;

    public BoardReplayTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- The replay window returns the recorded events ordered by recorded-at ---

    [Fact]
    public async Task GetReplayWindow_ReturnsRecordedTransitionsOrderedByRecordedAt()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 2, 1, 14, 0, 0, DateTimeKind.Utc);

        // Seed out of order; expect them back chronologically across the window.
        await SeedMetricsAsync(
            Metric(ZendeskQueue, "TicketCreated", "REPLAY-1", anchor.AddSeconds(30), decision: "AutoApproved"),
            Metric(RulesEngineQueue, "Submitted", "REPLAY-1", anchor.AddSeconds(0)),
            Metric(RulesEngineQueue, "RulesEvaluated", "REPLAY-2", anchor.AddSeconds(15), decision: "AutoRejected"));

        var service = CreateService();
        var window = await service.GetReplayWindowAsync(anchor, anchor.AddMinutes(1));

        Assert.Equal(3, window.Count);
        Assert.Equal("Submitted", window[0].Stage);
        Assert.Equal("RulesEvaluated", window[1].Stage);
        Assert.Equal("TicketCreated", window[2].Stage);
        Assert.True(window[0].RecordedAtUtc <= window[1].RecordedAtUtc);
        Assert.True(window[1].RecordedAtUtc <= window[2].RecordedAtUtc);
    }

    // --- Events outside the window are excluded ---

    [Fact]
    public async Task GetReplayWindow_ExcludesEventsOutsideTheWindow()
    {
        await ResetMetricsAsync();
        var anchor = new DateTime(2026, 2, 2, 9, 0, 0, DateTimeKind.Utc);

        await SeedMetricsAsync(
            Metric(RulesEngineQueue, "Submitted", "BEFORE", anchor.AddMinutes(-5)),
            Metric(RulesEngineQueue, "Submitted", "INSIDE", anchor.AddMinutes(5)),
            Metric(RulesEngineQueue, "Submitted", "AFTER", anchor.AddMinutes(50)));

        var service = CreateService();
        var window = await service.GetReplayWindowAsync(anchor, anchor.AddMinutes(30));

        var single = Assert.Single(window);
        Assert.Equal("INSIDE", single.ReferenceNumber);
    }

    // --- An over-wide range is rejected to prevent abusive aggregation (DoS) ---

    [Fact]
    public async Task GetReplayWindow_RejectsOverWideRange()
    {
        await ResetMetricsAsync();
        var from = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.GetReplayWindowAsync(from, to));
    }

    private IMetricsQueryService CreateService() =>
        new MetricsQueryService(_fixture.CreateContext());

    private static MetricEntity Metric(
        string queue,
        string stage,
        string reference,
        DateTime recordedAtUtc,
        string? decision = null,
        double latencyMs = 0) => new()
    {
        QueueName = queue,
        Stage = stage,
        ReferenceNumber = reference,
        MessageId = Guid.NewGuid(),
        DecisionStatus = decision,
        LatencyMs = latencyMs,
        RecordedAtUtc = recordedAtUtc,
    };

    private async Task SeedMetricsAsync(params MetricEntity[] events)
    {
        await using var context = _fixture.CreateContext();
        context.QueueMetricEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    private async Task ResetMetricsAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE queue_metrics_events RESTART IDENTITY CASCADE;");
    }
}
