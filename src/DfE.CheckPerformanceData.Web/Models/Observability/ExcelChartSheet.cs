using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The shared Open XML chart-sheet writer behind both the dashboard export (MetricsWorkbookBuilder)
// and the load-test export (LoadTestWorkbookBuilder): adds one worksheet carrying a category column
// plus one or more value columns, and embeds a native chart (line / bar / pie) referencing those
// cells, with a right-hand legend that reserves its own space and rotated category labels so long
// labels never overlap. The whole package stays schema-valid OOXML (asserted by OpenXmlValidator in
// the workbook tests). Kept in one place so the chart plumbing has a single source of truth.
public static class ExcelChartSheet
{
    public enum ChartKind { Line, Pie, Bar }

    // Adds a worksheet named `name` with the data table, and (when there is at least one row) an
    // embedded chart of `kind` titled `title`. Advances sheetId.
    public static void AddSheet(
        WorkbookPart wbPart, Sheets sheets, ref uint sheetId, string name,
        string categoryHeader, IReadOnlyList<string> valueHeaders,
        IReadOnlyList<string> categories, IReadOnlyList<IReadOnlyList<double>> valueColumns,
        ChartKind kind, string title)
    {
        var wsPart = wbPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();

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

        if (rowCount > 0)
            AddChart(wsPart, name, kind, title, valueHeaders, categories, valueColumns);
    }

    // ISO-8601 UTC timestamp for a category label (invariant), so chart axes read consistently.
    public static string Iso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

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
                // Rotate the category-axis labels -45° so long labels never overlap each other. txPr
                // must come before crossAx per the CT_CatAx schema order.
                new C.TextProperties(
                    new A.BodyProperties { Rotation = -2700000, Vertical = A.TextVerticalValues.Horizontal },
                    new A.ListStyle(),
                    new A.Paragraph(new A.ParagraphProperties(new A.DefaultRunProperties()))),
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
            // Legend on the right, overlay off, so it reserves its own space rather than sitting on
            // top of the bottom category-axis labels.
            new C.Legend(
                new C.LegendPosition { Val = C.LegendPositionValues.Right },
                new C.Overlay { Val = false }),
            new C.PlotVisibleOnly { Val = true });

        chartPart.ChartSpace = new C.ChartSpace(chart);
        chartPart.ChartSpace.PrependChild(new C.EditingLanguage { Val = "en-GB" });

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
    };

    private static string Num(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

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
