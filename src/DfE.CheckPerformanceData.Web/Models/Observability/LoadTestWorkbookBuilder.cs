using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Kind = DfE.CheckPerformanceData.Web.Models.Observability.ExcelChartSheet.ChartKind;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// One measured level of the self load-test: the rate driven, how many completed, the wall-clock
// throughput, and the per-step averages over that batch.
public sealed class LoadTestRow
{
    public int Rate { get; set; }
    public int Completed { get; set; }
    public double ThroughputPerSec { get; set; }
    public double ElapsedMs { get; set; }
    public double? RulesQueueMs { get; set; }
    public double? RulesEngineMs { get; set; }
    public double? ZendeskQueueMs { get; set; }
    public double? TicketMs { get; set; }
}

// Builds the load-test export: an .xlsx with a Throughput-by-rate line chart (the one that shows the
// optimum messages/second) and a Step-times-by-rate line chart, each with its data table. Built on
// the shared ExcelChartSheet writer so the chart plumbing has a single source of truth.
public static class LoadTestWorkbookBuilder
{
    public static byte[] Build(IReadOnlyList<LoadTestRow> rows)
    {
        rows ??= [];
        var rates = rows.Select(r => r.Rate.ToString()).ToList();

        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

            // 1) Throughput by rate — the headline "optimum messages/second" chart.
            ExcelChartSheet.AddSheet(wbPart, sheets, ref sheetId, "Throughput", "Rate (messages)",
                new[] { "Throughput (msg/s)" },
                rates,
                new List<IReadOnlyList<double>> { rows.Select(r => r.ThroughputPerSec).ToList() },
                Kind.Line, "Throughput by rate");

            // 2) Step times by rate — how each pipeline step's latency moves as the load climbs.
            ExcelChartSheet.AddSheet(wbPart, sheets, ref sheetId, "Step times", "Rate (messages)",
                new[] { "Rules queue (ms)", "Rules engine (ms)", "Zendesk queue (ms)", "Ticket (ms)" },
                rates,
                new List<IReadOnlyList<double>>
                {
                    rows.Select(r => r.RulesQueueMs ?? 0).ToList(),
                    rows.Select(r => r.RulesEngineMs ?? 0).ToList(),
                    rows.Select(r => r.ZendeskQueueMs ?? 0).ToList(),
                    rows.Select(r => r.TicketMs ?? 0).ToList(),
                },
                Kind.Line, "Step times by rate");

            wbPart.Workbook.Save();
        }

        return ms.ToArray();
    }
}
