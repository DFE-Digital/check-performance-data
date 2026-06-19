using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Observability;

// The Excel export builder: assert the produced .xlsx is schema-valid OOXML (so Excel will open
// it), has one tab per dashboard chart, and embeds a native chart on each. OpenXmlValidator is the
// authoritative check — a malformed package would fail to open, and the validator catches that
// without needing Excel installed.
public sealed class MetricsWorkbookBuilderTests
{
    private static MetricsWorkbookData Sample()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new MetricsWorkbookData
        {
            RangeLabel = "last 24 hours",
            GranularityLabel = "per hour",
            ProcessedToday = 42,
            TypicalEndToEnd = TimeSpan.FromSeconds(6),
            Throughput = new[] { new ThroughputBucket(t0, 3), new ThroughputBucket(t0.AddHours(1), 7) },
            DecisionMix = new[]
            {
                new DecisionMixEntry("AutoApproved", 30),
                new DecisionMixEntry("AutoRejected", 8),
                new DecisionMixEntry("Scrutiny", 4),
            },
            DecisionMixOverTime = new[]
            {
                new DecisionMixBucket(t0, "AutoApproved", 2),
                new DecisionMixBucket(t0, "Scrutiny", 1),
                new DecisionMixBucket(t0.AddHours(1), "AutoApproved", 5),
                new DecisionMixBucket(t0.AddHours(1), "Scrutiny", 0),
            },
            Dwell = new[]
            {
                new StageDwell("Submitted", 0),
                new StageDwell("RulesEvaluated", 1500),
                new StageDwell("TicketCreated", 4000),
            },
        };
    }

    [Fact]
    public void Build_ProducesAValidWorkbook_WithFourChartTabs()
    {
        var bytes = MetricsWorkbookBuilder.Build(Sample());
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);

        // Four worksheets, one per dashboard chart, in order.
        var sheetNames = doc.WorkbookPart!.Workbook.Sheets!
            .Elements<Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(
            new[] { "Throughput", "Decision mix", "Decision mix over time", "Time at each stage" },
            sheetNames);

        // Each worksheet embeds a chart (a ChartPart reached via its DrawingsPart).
        var chartCount = doc.WorkbookPart.WorksheetParts.Sum(ws => ws.DrawingsPart?.ChartParts.Count() ?? 0);
        Assert.Equal(4, chartCount);

        // The whole package is schema-valid OOXML, so Excel will open it.
        var validator = new OpenXmlValidator(FileFormatVersions.Office2007);
        var errors = validator.Validate(doc).ToList();
        Assert.True(errors.Count == 0,
            "OOXML validation errors:\n" + string.Join("\n", errors.Select(e => e.Description)));
    }

    [Fact]
    public void Build_ToleratesEmptySeries_StillValid()
    {
        var bytes = MetricsWorkbookBuilder.Build(new MetricsWorkbookData());

        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);

        // Four (empty) sheets still render; with no data rows there is no embedded chart.
        var sheetNames = doc.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Count();
        Assert.Equal(4, sheetNames);

        var validator = new OpenXmlValidator(FileFormatVersions.Office2007);
        Assert.Empty(validator.Validate(doc));
    }
}
