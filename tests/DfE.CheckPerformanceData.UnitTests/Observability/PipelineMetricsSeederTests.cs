using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The dev-only "Seed messages" generator: it fabricates a couple of months of synthetic pipeline
// metric events so a fresh development environment's charts look full. It is pure (no DB, no clock
// of its own) so the distribution is asserted deterministically with a seeded Random.
public sealed class PipelineMetricsSeederTests
{
    private static readonly DateTime Now = new(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Generate_ProducesEventsSpreadAcrossTheRequestedWindow_AllBackdated()
    {
        var events = PipelineMetricsSeeder.Generate(Now, days: 30, minPerDay: 10, maxPerDay: 40, new Random(12345));

        Assert.NotEmpty(events);
        // Every event is in the past and inside the window (with a day of slack for sub-day offsets).
        var earliest = Now - TimeSpan.FromDays(31);
        Assert.All(events, e =>
        {
            Assert.True(e.RecordedAtUtc <= Now, "event is backdated");
            Assert.True(e.RecordedAtUtc >= earliest, "event is within the window");
            Assert.Equal(DateTimeKind.Utc, e.RecordedAtUtc.Kind);
        });

        // The events span most of the 30 days, not one clump.
        var distinctDays = events.Select(e => e.RecordedAtUtc.Date).Distinct().Count();
        Assert.True(distinctDays >= 20, $"expected events across many days, saw {distinctDays}");
    }

    [Fact]
    public void Generate_MessageCountIsWithinThePerDayRange()
    {
        var events = PipelineMetricsSeeder.Generate(Now, days: 20, minPerDay: 5, maxPerDay: 15, new Random(7));

        var references = events.Select(e => e.ReferenceNumber).Distinct().Count();
        Assert.InRange(references, 20 * 5, 20 * 15);
    }

    [Fact]
    public void Generate_EachSubmissionHasAFullJourneyAcrossTheStages()
    {
        var events = PipelineMetricsSeeder.Generate(Now, days: 15, minPerDay: 20, maxPerDay: 30, new Random(99));

        var byRef = events.GroupBy(e => e.ReferenceNumber).ToList();
        Assert.All(byRef, g =>
        {
            // Every submission starts with a Submitted event...
            Assert.Contains(g, e => e.Stage == MetricStages.Submitted);
            // ...and either reaches a ticket (with a decision) or is dead-lettered.
            var decided = g.Any(e => e.Stage == MetricStages.RulesEvaluated);
            var deadLettered = g.Any(e => e.Stage == MetricStages.DeadLettered);
            Assert.True(decided ^ deadLettered, "a submission is decided XOR dead-lettered");
        });

        // The decided submissions carry one of the three decisions on their RulesEvaluated event,
        // and their ticket is recorded against the Zendesk queue (so throughput counts it).
        var rules = events.Where(e => e.Stage == MetricStages.RulesEvaluated).ToList();
        Assert.All(rules, e => Assert.Contains(e.DecisionStatus,
            new[] { "AutoApproved", "AutoRejected", "Scrutiny" }));
        var tickets = events.Where(e => e.Stage == MetricStages.TicketCreated).ToList();
        Assert.All(tickets, e => Assert.Equal(QueueOptions.ZendeskQueue, e.QueueName));

        // The mix exercises every outcome, including dead-letters, so all surfaces fill.
        Assert.Contains(events, e => e.Stage == MetricStages.DeadLettered);
        Assert.Contains(events, e => e.DecisionStatus == "AutoApproved");
        Assert.Contains(events, e => e.DecisionStatus == "Scrutiny");
        Assert.Contains(events, e => e.DecisionStatus == "AutoRejected");
    }
}
