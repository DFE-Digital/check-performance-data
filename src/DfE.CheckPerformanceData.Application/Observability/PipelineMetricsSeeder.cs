using DfE.CheckPerformanceData.Application.Queue;

namespace DfE.CheckPerformanceData.Application.Observability;

// The dev-only "Seed messages" generator. It fabricates a couple of months of synthetic pipeline
// metric events — full journeys of Submitted → RulesEvaluated (+ decision) → TicketCreated, with a
// minority dead-lettered — so a fresh development environment's charts and counters look full
// without anyone having to drive thousands of requests by hand. It is pure: it takes the clock and
// the randomness as arguments, so the output is deterministic under test and the seeding endpoint
// owns the side effect of writing the events to the sink. It is gated to non-production dev tooling
// by its only caller and never runs against real data.
public static class PipelineMetricsSeeder
{
    // Build the events for `days` days back from `nowUtc`, with a random number of submissions per
    // day in [minPerDay, maxPerDay]. Each submission is a full journey across the stages; roughly one
    // in ten is dead-lettered instead of reaching a ticket. Every timestamp is backdated and UTC.
    public static IReadOnlyList<QueueMetricEvent> Generate(
        DateTime nowUtc, int days, int minPerDay, int maxPerDay, Random random)
    {
        if (days < 1) days = 1;
        if (minPerDay < 0) minPerDay = 0;
        if (maxPerDay < minPerDay) maxPerDay = minPerDay;

        var events = new List<QueueMetricEvent>();

        for (var d = 0; d < days; d++)
        {
            var perDay = random.Next(minPerDay, maxPerDay + 1);
            for (var i = 0; i < perDay; i++)
            {
                // A submission time somewhere within the chosen day in the past, always backdated.
                var submittedAt = nowUtc.AddDays(-d).AddSeconds(-random.Next(0, 86_400));
                AppendSubmission(events, submittedAt, random);
            }
        }

        return events;
    }

    private static void AppendSubmission(List<QueueMetricEvent> events, DateTime submittedAt, Random random)
    {
        var reference = $"SEED-{submittedAt:yyyyMMdd}-{Guid.NewGuid():N}"[..24];
        var messageId = Guid.NewGuid();

        // Submitted: the journey's first step, on the rules-engine queue.
        events.Add(new QueueMetricEvent(
            QueueOptions.RulesEngineQueue, MetricStages.Submitted, reference, messageId,
            DecisionStatus: null, RulesVersion: null, LatencyMs: 0, RecordedAtUtc: submittedAt));

        // The engine picks it up after a queue wait and spends some processing latency.
        var rulesAt = submittedAt.AddMilliseconds(random.Next(200, 8_000));
        var rulesLatency = random.Next(300, 5_000);

        // Roughly one in ten fails and is dead-lettered (no decision, no ticket); the rest are
        // decided and reach a Zendesk ticket.
        var roll = random.Next(100);
        if (roll < 10)
        {
            events.Add(new QueueMetricEvent(
                QueueOptions.RulesEngineQueue, MetricStages.DeadLettered, reference, messageId,
                DecisionStatus: null, RulesVersion: null, LatencyMs: rulesLatency, RecordedAtUtc: rulesAt));
            return;
        }

        // Weighted decision mix: mostly approved, then scrutiny, then a few rejected — a realistic
        // spread so the decision charts and counters look sensible.
        var decision = roll < 75 ? "AutoApproved" : roll < 90 ? "Scrutiny" : "AutoRejected";

        events.Add(new QueueMetricEvent(
            QueueOptions.RulesEngineQueue, MetricStages.RulesEvaluated, reference, messageId,
            DecisionStatus: decision, RulesVersion: null, LatencyMs: rulesLatency, RecordedAtUtc: rulesAt));

        var ticketAt = rulesAt.AddMilliseconds(random.Next(500, 20_000));
        events.Add(new QueueMetricEvent(
            QueueOptions.ZendeskQueue, MetricStages.TicketCreated, reference, messageId,
            DecisionStatus: null, RulesVersion: null, LatencyMs: random.Next(100, 2_000), RecordedAtUtc: ticketAt));
    }
}
