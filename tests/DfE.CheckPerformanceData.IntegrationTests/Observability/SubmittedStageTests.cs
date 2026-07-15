using System.Text.Json;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// A submitted request must begin its journey timeline at the Submitted stage, recorded at web
// enqueue time — not at the first consumer ack. Real Postgres: the submission flow enqueues a
// queue row AND writes a Submitted metric row, and the journey query returns it first.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SubmittedStageTests
{
    private readonly PostgresFixture _fixture;

    public SubmittedStageTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SubmitRequest_RecordsASubmittedStage_AndTheJourneyStartsWithIt()
    {
        await ResetAsync();

        await using var context = _fixture.CreateContext();
        var queueService = new PostgresQueueService(context);
        var recorder = new SubmittedMetricRecorder(new DbMetricsSink(context));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dev:ToolsEnabled"] = "true",
            })
            .Build();

        var controller = new DevPipelineController(configuration, context, queueService, submittedMetrics: recorder);

        var result = await controller.SubmitRequest(outcome: null, windowId: null, urn: null, CancellationToken.None);

        // The action returns the generated reference; the Submitted metric must carry it.
        var json = Assert.IsType<JsonResult>(result);
        var reference = JsonSerializer
            .Deserialize<JsonElement>(JsonSerializer.Serialize(json.Value))
            .GetProperty("referenceNumber").GetString();
        Assert.False(string.IsNullOrEmpty(reference));

        await using (var verify = _fixture.CreateContext())
        {
            var metric = Assert.Single(verify.QueueMetricEvents
                .Where(e => e.ReferenceNumber == reference).ToList());
            Assert.Equal(MetricStages.Submitted, metric.Stage);
            Assert.Equal(QueueOptions.RulesEngineQueue, metric.QueueName);

            // The metric's message id is the enqueued queue row, tying the first step to the
            // actual message rather than a synthetic id.
            Assert.Single(verify.QueueMessages.Where(m => m.Id == metric.MessageId).ToList());
        }

        // Once a later stage is recorded, the journey returns Submitted first.
        await using (var seed = _fixture.CreateContext())
        {
            seed.QueueMetricEvents.Add(new DfE.CheckPerformance.Persistence.Entities.QueueMetricEvent
            {
                QueueName = QueueOptions.RulesEngineQueue,
                Stage = MetricStages.RulesEvaluated,
                ReferenceNumber = reference!,
                MessageId = Guid.NewGuid(),
                DecisionStatus = "AutoApproved",
                LatencyMs = 250,
                RecordedAtUtc = DateTime.UtcNow.AddSeconds(5),
            });
            await seed.SaveChangesAsync();
        }

        await using var queryContext = _fixture.CreateContext();
        var journey = await new MetricsQueryService(queryContext).GetJourneyAsync(reference!);

        Assert.Equal(2, journey.Count);
        Assert.Equal(MetricStages.Submitted, journey[0].Stage);
        Assert.Equal(MetricStages.RulesEvaluated, journey[1].Stage);
    }

    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE queue_metrics_events RESTART IDENTITY CASCADE;");
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE queue_messages RESTART IDENTITY CASCADE;");
    }
}
