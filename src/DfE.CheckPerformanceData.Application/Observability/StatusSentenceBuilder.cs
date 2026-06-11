namespace DfE.CheckPerformanceData.Application.Observability;

// Builds the plain-English status sentence that narrates the health strip. The leading clause
// is the fixed copy for the current health band; the dynamic figures (how many requests were
// processed today and how long a typical request takes end-to-end) are appended so a
// stakeholder reads one human sentence rather than a dashboard of numbers.
public sealed class StatusSentenceBuilder
{
    private const string GreenLead = "All systems healthy.";

    private const string AmberLead =
        "One or more queues are backing up. Throughput is slower than usual but nothing has failed.";

    private const string RedLead =
        "Something needs attention: the dead-letter queue is rising or a queue has stalled. Open the queues page to investigate.";

    public string Build(HealthLevel level, int processedToday, TimeSpan typicalEndToEnd)
    {
        var lead = level switch
        {
            HealthLevel.Flowing => GreenLead,
            HealthLevel.BackingUp => AmberLead,
            HealthLevel.NeedsAttention => RedLead,
            _ => GreenLead,
        };

        var requestWord = processedToday == 1 ? "request" : "requests";
        var figures = typicalEndToEnd > TimeSpan.Zero
            ? $" {processedToday} {requestWord} processed today; a typical request reaches Zendesk in {Describe(typicalEndToEnd)}."
            : $" {processedToday} {requestWord} processed today.";

        return lead + figures;
    }

    private static string Describe(TimeSpan span)
    {
        if (span.TotalSeconds < 90)
            return $"{Math.Round(span.TotalSeconds)} seconds";
        if (span.TotalMinutes < 90)
            return $"{Math.Round(span.TotalMinutes)} minutes";
        return $"{Math.Round(span.TotalHours, 1)} hours";
    }
}
