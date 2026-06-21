using DfE.CheckPerformanceData.Application.Observability;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Kind = DfE.CheckPerformanceData.Web.Models.Observability.ExcelChartSheet.ChartKind;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The inputs the Excel export carries: the same four chart series the dashboard renders plus the
// headline-tile figures, so each workbook tab holds the accurate data behind the view AND an
// embedded native chart matching the on-screen one.
public sealed class MetricsWorkbookData
{
    public string RangeLabel { get; set; } = string.Empty;
    public string GranularityLabel { get; set; } = string.Empty;
    public int ProcessedToday { get; set; }
    public TimeSpan TypicalEndToEnd { get; set; }
    public IReadOnlyList<ThroughputBucket> Throughput { get; set; } = [];
    public IReadOnlyList<DecisionMixEntry> DecisionMix { get; set; } = [];
    public IReadOnlyList<DecisionMixBucket> DecisionMixOverTime { get; set; } = [];
    public IReadOnlyList<StageDwell> Dwell { get; set; } = [];
}

// Builds a single .xlsx with four tabs — one per dashboard chart (Throughput, Decision mix,
// Decision mix over time, Time at each stage). Each tab carries the data as a table and an embedded
// native chart of the matching type, via the shared ExcelChartSheet writer (the chart plumbing lives
// in one place, used by both this and the load-test export).
public static class MetricsWorkbookBuilder
{
    public static byte[] Build(MetricsWorkbookData data)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

            // 1) Throughput — a line over time.
            ExcelChartSheet.AddSheet(wbPart, sheets, ref sheetId, "Throughput", "Bucket (UTC)",
                new[] { "Count" },
                data.Throughput.Select(b => ExcelChartSheet.Iso(b.BucketStartUtc)).ToList(),
                new List<IReadOnlyList<double>> { data.Throughput.Select(b => (double)b.Count).ToList() },
                Kind.Line, $"Throughput ({data.RangeLabel}, {data.GranularityLabel})");

            // 2) Decision mix — a pie of the totals.
            ExcelChartSheet.AddSheet(wbPart, sheets, ref sheetId, "Decision mix", "Decision",
                new[] { "Count" },
                data.DecisionMix.Select(d => Label(d.DecisionStatus)).ToList(),
                new List<IReadOnlyList<double>> { data.DecisionMix.Select(d => (double)d.Count).ToList() },
                Kind.Pie, $"Decision mix ({data.RangeLabel})");

            // 3) Decision mix over time — one line per decision. The long (bucket, decision, count)
            //    series is pivoted to wide (a column per decision) so each becomes a chart series.
            var pivot = PivotOverTime(data.DecisionMixOverTime);
            ExcelChartSheet.AddSheet(wbPart, sheets, ref sheetId, "Decision mix over time", "Bucket (UTC)",
                pivot.Decisions, pivot.Buckets.Select(ExcelChartSheet.Iso).ToList(), pivot.Columns,
                Kind.Line, $"Decision mix over time ({data.RangeLabel}, {data.GranularityLabel})");

            // 4) Time at each stage — a bar of average dwell per stage.
            ExcelChartSheet.AddSheet(wbPart, sheets, ref sheetId, "Time at each stage", "Stage",
                new[] { "Average latency (ms)" },
                data.Dwell.Select(s => Label(s.Stage)).ToList(),
                new List<IReadOnlyList<double>> { data.Dwell.Select(s => s.AverageLatencyMs).ToList() },
                Kind.Bar, $"Time at each stage ({data.RangeLabel})");

            wbPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    // --- Pivot the long decision-mix-over-time into wide (a value column per decision) -----------

    private sealed record OverTimePivot(
        IReadOnlyList<DateTime> Buckets,
        IReadOnlyList<string> Decisions,
        IReadOnlyList<IReadOnlyList<double>> Columns);

    private static OverTimePivot PivotOverTime(IReadOnlyList<DecisionMixBucket> series)
    {
        var buckets = series.Select(s => s.BucketStartUtc).Distinct().OrderBy(b => b).ToList();
        var decisions = series.Select(s => s.DecisionStatus).Distinct().OrderBy(d => d).ToList();
        var bucketIndex = buckets.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);

        var columns = new List<IReadOnlyList<double>>();
        foreach (var decision in decisions)
        {
            var col = new double[buckets.Count];
            foreach (var point in series.Where(s => s.DecisionStatus == decision))
                col[bucketIndex[point.BucketStartUtc]] = point.Count;
            columns.Add(col);
        }

        return new OverTimePivot(buckets, decisions.Select(Label).ToList(), columns);
    }

    private static string Label(string value) => value switch
    {
        "AutoApproved" => "Auto-approved",
        "AutoRejected" => "Auto-rejected",
        "Scrutiny" => "Scrutiny",
        _ => value ?? string.Empty,
    };
}
