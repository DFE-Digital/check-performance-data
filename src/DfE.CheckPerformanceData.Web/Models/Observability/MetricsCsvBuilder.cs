using System.Globalization;
using System.Text;
using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The inputs the CSV export carries: the same series the dashboard charts render plus the
// headline-tile figures, so the export is the accurate data behind the view rather than an image.
public sealed class MetricsCsvData
{
    public string RangeLabel { get; set; } = string.Empty;
    public string GranularityLabel { get; set; } = string.Empty;
    public int ProcessedToday { get; set; }
    public TimeSpan TypicalEndToEnd { get; set; }
    public IReadOnlyList<ThroughputBucket> Throughput { get; set; } = [];
    public IReadOnlyList<DecisionMixEntry> DecisionMix { get; set; } = [];
    public IReadOnlyList<StageDwell> Dwell { get; set; } = [];
}

// Turns the dashboard's data into a single CSV document with labelled sections (headline tiles,
// throughput series, decision mix, per-stage dwell). Free-text fields (labels, decisions, stages)
// are RFC-4180 quoted with internal quotes doubled, so a value carrying a comma or quote can never
// break the row shape; numbers and ISO-8601 UTC timestamps are written bare. The export is built
// from the same series the charts render, so it is the accurate data behind the view, not an image.
public static class MetricsCsvBuilder
{
    public static string Build(MetricsCsvData data)
    {
        var sb = new StringBuilder();

        sb.Append("Key,Value\r\n");
        sb.Append("Range,").Append(Quote(data.RangeLabel)).Append("\r\n");
        sb.Append("Granularity,").Append(Quote(data.GranularityLabel)).Append("\r\n");
        sb.Append("Processed (24h),")
            .Append(data.ProcessedToday.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("Typical end-to-end (ms),")
            .Append(((long)data.TypicalEndToEnd.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
            .Append("\r\n");

        sb.Append("\r\nThroughput,Bucket (UTC),Count\r\n");
        foreach (var bucket in data.Throughput)
        {
            sb.Append("Throughput,")
                .Append(Iso(bucket.BucketStartUtc)).Append(',')
                .Append(bucket.Count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }

        sb.Append("\r\nDecision mix,Decision,Count\r\n");
        foreach (var entry in data.DecisionMix)
        {
            sb.Append("Decision mix,")
                .Append(Quote(entry.DecisionStatus)).Append(',')
                .Append(entry.Count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }

        sb.Append("\r\nStage dwell,Stage,Average latency (ms)\r\n");
        foreach (var stage in data.Dwell)
        {
            sb.Append("Stage dwell,")
                .Append(Quote(stage.Stage)).Append(',')
                .Append(stage.AverageLatencyMs.ToString("0.###", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Iso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Quote(string value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}
