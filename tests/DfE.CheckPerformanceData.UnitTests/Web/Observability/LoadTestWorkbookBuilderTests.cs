using DfE.CheckPerformanceData.Web.Models.Observability;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Observability;

// The load-test export: a schema-valid .xlsx with a Throughput-by-rate chart and a Step-times chart,
// each backed by its data. OpenXmlValidator is the authoritative "Excel will open it" check.
public sealed class LoadTestWorkbookBuilderTests
{
    private static IReadOnlyList<LoadTestRow> Sample() => new[]
    {
        new LoadTestRow { Rate = 1, Completed = 1, ThroughputPerSec = 1.0, ElapsedMs = 1000, RulesQueueMs = 200, RulesEngineMs = 400, ZendeskQueueMs = 600, TicketMs = 150 },
        new LoadTestRow { Rate = 4, Completed = 4, ThroughputPerSec = 3.5, ElapsedMs = 1140, RulesQueueMs = 260, RulesEngineMs = 420, ZendeskQueueMs = 700, TicketMs = 160 },
        new LoadTestRow { Rate = 20, Completed = 18, ThroughputPerSec = 9.0, ElapsedMs = 2000, RulesQueueMs = 900, RulesEngineMs = 460, ZendeskQueueMs = 1500, TicketMs = 180 },
    };

    [Fact]
    public void Build_ProducesAValidWorkbook_WithThroughputAndStepTimesCharts()
    {
        var bytes = LoadTestWorkbookBuilder.Build(Sample());
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);

        var sheetNames = doc.WorkbookPart!.Workbook.Sheets!
            .Elements<Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(new[] { "Throughput", "Step times" }, sheetNames);

        var chartCount = doc.WorkbookPart.WorksheetParts.Sum(ws => ws.DrawingsPart?.ChartParts.Count() ?? 0);
        Assert.Equal(2, chartCount);

        var validator = new OpenXmlValidator(FileFormatVersions.Office2007);
        var errors = validator.Validate(doc).ToList();
        Assert.True(errors.Count == 0,
            "OOXML validation errors:\n" + string.Join("\n", errors.Select(e => e.Description)));
    }

    [Fact]
    public void Build_NoRows_StillValid()
    {
        var bytes = LoadTestWorkbookBuilder.Build([]);

        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);

        Assert.Equal(2, doc.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Count());
        var validator = new OpenXmlValidator(FileFormatVersions.Office2007);
        Assert.Empty(validator.Validate(doc));
    }
}
