using System.Globalization;
using DfE.CheckPerformanceData.Application.Observability;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

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
// native chart of the matching type, built with the MIT-licensed Open XML SDK (no third-party
// dependency). The chart references the sheet's own cell ranges and also caches the values so it
// renders in viewers that do not recalculate. Numbers and ISO-8601 UTC timestamps are written with
// the invariant culture; text is written as inline strings (no shared-string table needed).
public static class MetricsWorkbookBuilder
{
    private enum ChartKind { Line, Pie, Bar }

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
            AddChartSheet(wbPart, sheets, ref sheetId, "Throughput", "Bucket (UTC)",
                new[] { "Count" },
                data.Throughput.Select(b => Iso(b.BucketStartUtc)).ToList(),
                new List<IReadOnlyList<double>> { data.Throughput.Select(b => (double)b.Count).ToList() },
                ChartKind.Line, $"Throughput ({data.RangeLabel}, {data.GranularityLabel})");

            // 2) Decision mix — a pie of the totals.
            AddChartSheet(wbPart, sheets, ref sheetId, "Decision mix", "Decision",
                new[] { "Count" },
                data.DecisionMix.Select(d => Label(d.DecisionStatus)).ToList(),
                new List<IReadOnlyList<double>> { data.DecisionMix.Select(d => (double)d.Count).ToList() },
                ChartKind.Pie, $"Decision mix ({data.RangeLabel})");

            // 3) Decision mix over time — one line per decision. The long (bucket, decision, count)
            //    series is pivoted to wide (a column per decision) so each becomes a chart series.
            var pivot = PivotOverTime(data.DecisionMixOverTime);
            AddChartSheet(wbPart, sheets, ref sheetId, "Decision mix over time", "Bucket (UTC)",
                pivot.Decisions, pivot.Buckets.Select(Iso).ToList(), pivot.Columns,
                ChartKind.Line, $"Decision mix over time ({data.RangeLabel}, {data.GranularityLabel})");

            // 4) Time at each stage — a bar of average dwell per stage.
            AddChartSheet(wbPart, sheets, ref sheetId, "Time at each stage", "Stage",
                new[] { "Average latency (ms)" },
                data.Dwell.Select(s => Label(s.Stage)).ToList(),
                new List<IReadOnlyList<double>> { data.Dwell.Select(s => s.AverageLatencyMs).ToList() },
                ChartKind.Bar, $"Time at each stage ({data.RangeLabel})");

            wbPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    // --- Worksheet + chart assembly -----------------------------------------------------------

    private static void AddChartSheet(
        WorkbookPart wbPart, Sheets sheets, ref uint sheetId, string name,
        string categoryHeader, IReadOnlyList<string> valueHeaders,
        IReadOnlyList<string> categories, IReadOnlyList<IReadOnlyList<double>> valueColumns,
        ChartKind kind, string title)
    {
        var wsPart = wbPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();

        // Header row: category header in column A, one value header per value column from B onwards.
        var header = new Row { RowIndex = 1U };
        header.Append(TextCell("A1", categoryHeader));
        for (var c = 0; c < valueHeaders.Count; c++)
            header.Append(TextCell($"{ColLetter(c + 1)}1", valueHeaders[c]));
        sheetData.Append(header);

        var rowCount = categories.Count;
        for (var r = 0; r < rowCount; r++)
        {
            var rowIndex = (uint)(r + 2);
            var row = new Row { RowIndex = rowIndex };
            row.Append(TextCell($"A{rowIndex}", categories[r]));
            for (var c = 0; c < valueColumns.Count; c++)
            {
                var v = r < valueColumns[c].Count ? valueColumns[c][r] : 0;
                row.Append(NumberCell($"{ColLetter(c + 1)}{rowIndex}", v));
            }
            sheetData.Append(row);
        }

        wsPart.Worksheet = new Worksheet(sheetData);

        sheets.Append(new Sheet
        {
            Id = wbPart.GetIdOfPart(wsPart),
            SheetId = sheetId,
            Name = name,
        });
        sheetId++;

        // Only embed a chart when there is at least one data row; an empty series would yield an
        // empty chart with no axis range.
        if (rowCount > 0)
            AddChart(wsPart, name, kind, title, valueHeaders, categories, valueColumns);
    }

    private static void AddChart(
        WorksheetPart wsPart, string sheetName, ChartKind kind, string title,
        IReadOnlyList<string> valueHeaders, IReadOnlyList<string> categories,
        IReadOnlyList<IReadOnlyList<double>> valueColumns)
    {
        var rowCount = categories.Count;
        var drawingsPart = wsPart.AddNewPart<DrawingsPart>();
        var chartPart = drawingsPart.AddNewPart<ChartPart>();

        var plotArea = new C.PlotArea(new C.Layout());

        const uint catAxisId = 111111111U;
        const uint valAxisId = 222222222U;
        var lastRow = rowCount + 1;
        var catFormula = $"'{sheetName}'!$A$2:$A${lastRow}";

        OpenXmlCompositeElement chartType = kind switch
        {
            ChartKind.Pie => new C.PieChart(new C.VaryColors { Val = true }),
            ChartKind.Bar => new C.BarChart(
                new C.BarDirection { Val = C.BarDirectionValues.Column },
                new C.BarGrouping { Val = C.BarGroupingValues.Clustered }),
            _ => new C.LineChart(
                new C.Grouping { Val = C.GroupingValues.Standard },
                new C.VaryColors { Val = false }),
        };

        for (var s = 0; s < valueColumns.Count; s++)
        {
            var colLetter = ColLetter(s + 1);
            var valFormula = $"'{sheetName}'!${colLetter}$2:${colLetter}${lastRow}";
            var seriesName = s < valueHeaders.Count ? valueHeaders[s] : colLetter;
            var values = valueColumns[s];
            chartType.Append(BuildSeries(kind, (uint)s, seriesName, catFormula, valFormula, values, categories));
        }

        if (kind != ChartKind.Pie)
        {
            chartType.Append(new C.AxisId { Val = catAxisId });
            chartType.Append(new C.AxisId { Val = valAxisId });
        }

        plotArea.Append(chartType);

        if (kind != ChartKind.Pie)
        {
            plotArea.Append(new C.CategoryAxis(
                new C.AxisId { Val = catAxisId },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.Delete { Val = false },
                new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                new C.CrossingAxis { Val = valAxisId },
                new C.Crosses { Val = C.CrossesValues.AutoZero },
                new C.AutoLabeled { Val = true },
                new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
                new C.LabelOffset { Val = 100 }));

            plotArea.Append(new C.ValueAxis(
                new C.AxisId { Val = valAxisId },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.Delete { Val = false },
                new C.AxisPosition { Val = C.AxisPositionValues.Left },
                new C.CrossingAxis { Val = catAxisId },
                new C.Crosses { Val = C.CrossesValues.AutoZero }));
        }

        var chart = new C.Chart(
            new C.Title(new C.ChartText(new C.RichText(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.RunProperties { Language = "en-GB" }, new A.Text(title)))))),
            new C.AutoTitleDeleted { Val = false },
            plotArea,
            new C.Legend(new C.LegendPosition { Val = C.LegendPositionValues.Bottom }),
            new C.PlotVisibleOnly { Val = true });

        chartPart.ChartSpace = new C.ChartSpace(chart);
        chartPart.ChartSpace.PrependChild(new C.EditingLanguage { Val = "en-GB" });

        // Anchor the chart to the right of the data and wire the worksheet's <drawing> to it.
        drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing(
            new Xdr.TwoCellAnchor(
                new Xdr.FromMarker(
                    new Xdr.ColumnId("4"), new Xdr.ColumnOffset("0"),
                    new Xdr.RowId("1"), new Xdr.RowOffset("0")),
                new Xdr.ToMarker(
                    new Xdr.ColumnId("16"), new Xdr.ColumnOffset("0"),
                    new Xdr.RowId("22"), new Xdr.RowOffset("0")),
                new Xdr.GraphicFrame(
                    new Xdr.NonVisualGraphicFrameProperties(
                        new Xdr.NonVisualDrawingProperties { Id = 2U, Name = "Chart" },
                        new Xdr.NonVisualGraphicFrameDrawingProperties()),
                    new Xdr.Transform(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = 0L, Cy = 0L }),
                    new A.Graphic(new A.GraphicData(
                        new C.ChartReference { Id = drawingsPart.GetIdOfPart(chartPart) })
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" })),
                new Xdr.ClientData()));

        wsPart.Worksheet.Append(new Drawing { Id = wsPart.GetIdOfPart(drawingsPart) });
    }

    private static OpenXmlCompositeElement BuildSeries(
        ChartKind kind, uint index, string seriesName, string catFormula, string valFormula,
        IReadOnlyList<double> values, IReadOnlyList<string>? categoryLabels)
    {
        var seriesText = new C.SeriesText(new C.NumericValue(seriesName));
        var cat = new C.CategoryAxisData(BuildStringReference(catFormula, categoryLabels, values.Count));
        var val = new C.Values(BuildNumberReference(valFormula, values));

        return kind switch
        {
            ChartKind.Pie => new C.PieChartSeries(
                new C.Index { Val = index }, new C.Order { Val = index }, seriesText, cat, val),
            ChartKind.Bar => new C.BarChartSeries(
                new C.Index { Val = index }, new C.Order { Val = index }, seriesText, cat, val),
            _ => new C.LineChartSeries(
                new C.Index { Val = index }, new C.Order { Val = index }, seriesText, cat, val,
                new C.Smooth { Val = false }),
        };
    }

    private static C.StringReference BuildStringReference(
        string formula, IReadOnlyList<string>? labels, int count)
    {
        var cache = new C.StringCache(new C.PointCount { Val = (uint)count });
        for (var i = 0; i < count; i++)
        {
            var text = labels is not null && i < labels.Count ? labels[i] : string.Empty;
            cache.Append(new C.StringPoint(new C.NumericValue(text)) { Index = (uint)i });
        }
        return new C.StringReference(new C.Formula(formula), cache);
    }

    private static C.NumberReference BuildNumberReference(string formula, IReadOnlyList<double> values)
    {
        var cache = new C.NumberingCache(new C.FormatCode("General"), new C.PointCount { Val = (uint)values.Count });
        for (var i = 0; i < values.Count; i++)
            cache.Append(new C.NumericPoint(new C.NumericValue(Num(values[i]))) { Index = (uint)i });
        return new C.NumberReference(new C.Formula(formula), cache);
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

    // --- Cells + formatting ---------------------------------------------------------------------

    private static Cell TextCell(string reference, string text) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(text ?? string.Empty)),
    };

    private static Cell NumberCell(string reference, double value) => new()
    {
        CellReference = reference,
        CellValue = new CellValue(Num(value)),
        // No DataType => number is the default cell type.
    };

    private static string Num(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Iso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Label(string value) => value switch
    {
        "AutoApproved" => "Auto-approved",
        "AutoRejected" => "Auto-rejected",
        "Scrutiny" => "Scrutiny",
        _ => value ?? string.Empty,
    };

    private static string ColLetter(int index)
    {
        var letters = string.Empty;
        index++;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            letters = (char)('A' + rem) + letters;
            index = (index - 1) / 26;
        }
        return letters;
    }
}
