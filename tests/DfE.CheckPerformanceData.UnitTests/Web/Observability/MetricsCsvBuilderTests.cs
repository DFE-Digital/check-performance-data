using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Observability;

// The dashboard export is the data behind the charts as CSV, not a chart image. The builder is a
// pure projection of the same series the dashboard renders (throughput, decision-mix, per-stage
// dwell and the headline tiles), so the export is accurate by construction. CSV values are quoted
// and internal quotes doubled so a label containing a comma or quote cannot break the row shape.
public sealed class MetricsCsvBuilderTests
{
    private static MetricsCsvData SampleData() => new()
    {
        RangeLabel = "Last 24 hours",
        GranularityLabel = "per hour",
        ProcessedToday = 42,
        TypicalEndToEnd = TimeSpan.FromMilliseconds(1500),
        Throughput =
        [
            new ThroughputBucket(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), 5),
            new ThroughputBucket(new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc), 8),
        ],
        DecisionMix =
        [
            new DecisionMixEntry("AutoApproved", 30),
            new DecisionMixEntry("AutoRejected", 12),
        ],
        Dwell =
        [
            new StageDwell("RulesEvaluated", 200),
            new StageDwell("TicketCreated", 50),
        ],
    };

    [Fact]
    public void Build_EmitsAHeadlineSection_WithTheTileFigures()
    {
        var csv = MetricsCsvBuilder.Build(SampleData());

        Assert.Contains("Processed (24h),42", csv);
        Assert.Contains("Typical end-to-end (ms),1500", csv);
        Assert.Contains("Range,\"Last 24 hours\"", csv);
    }

    [Fact]
    public void Build_EmitsThroughputRows_WithBucketTimestampsAndCounts()
    {
        var csv = MetricsCsvBuilder.Build(SampleData());

        Assert.Contains("Throughput", csv);
        Assert.Contains("2026-01-01T10:00:00Z,5", csv);
        Assert.Contains("2026-01-01T11:00:00Z,8", csv);
    }

    [Fact]
    public void Build_EmitsDecisionMixRows()
    {
        var csv = MetricsCsvBuilder.Build(SampleData());

        Assert.Contains("\"AutoApproved\",30", csv);
        Assert.Contains("\"AutoRejected\",12", csv);
    }

    [Fact]
    public void Build_EmitsPerStageDwellRows()
    {
        var csv = MetricsCsvBuilder.Build(SampleData());

        Assert.Contains("\"RulesEvaluated\",200", csv);
        Assert.Contains("\"TicketCreated\",50", csv);
    }

    [Fact]
    public void Build_QuotesAndEscapesValuesContainingCommasAndQuotes()
    {
        var data = SampleData();
        data.DecisionMix = [new DecisionMixEntry("Needs \"manual\", review", 1)];

        var csv = MetricsCsvBuilder.Build(data);

        // The embedded quotes are doubled and the whole field stays wrapped, so the comma inside
        // the label can never be read as a column separator.
        Assert.Contains("\"Needs \"\"manual\"\", review\",1", csv);
    }
}
