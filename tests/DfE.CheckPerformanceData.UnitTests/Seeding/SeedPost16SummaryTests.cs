using System.Globalization;
using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// Every 16-19 window has a summary data share, and each step of the year replaces its file. These
/// pin that each step's file matches its schema, has one row per school, and that the values carry
/// over or change as the step says.
/// </summary>
public sealed class SeedPost16SummaryTests
{
    private static readonly Guid Window = DevDataSeeder.Post16OctoberCheckingWindowId;

    [Fact]
    public void The_steps_fill_the_summary_slots_in_order()
        => Assert.Equal(SeedCheckingWindows.SummaryDatasets, SeedPost16Summary.Steps.Select(s => s.Slot));

    [Fact]
    public void Each_step_has_its_own_schema()
        => Assert.Equal(
            ["summary-autumn_schema.json", "summary-november-va_schema.json", "summary-november-va-revised_schema.json",
             "summary-retention_schema.json"],
            SeedPost16Summary.Steps.Select(s => s.Schema));

    // The section is the heading schools see, so it names the step. The slot name is shorter
    // only where it has to be: a slot name is at most 50 characters.
    [Fact]
    public void Each_schema_names_its_step_and_is_its_own_dataset()
    {
        var schemas = SeedPost16Summary.Steps.Select(s => Parse(s.Schema)).ToList();

        Assert.Equal(
            ["Summary", "Summary with value added", "Summary with value added: revised",
             "Summary with value added: revised including retention"],
            schemas.Select(d => Text(d, "x-display", "section")));
        Assert.Equal(schemas.Select(d => Text(d, "x-display", "section")), schemas.Select(d => Text(d, "x-download", "label")));
        Assert.Equal(4, schemas.Select(d => Text(d, "x-ingress", "collection")).Distinct().Count());
        Assert.Equal(4, schemas.Select(d => Text(d, "x-download", "fileName")).Distinct().Count());
        Assert.All(schemas, d => Assert.Equal("vertical", Text(d, "x-display", "layout")));
        schemas.ForEach(d => d.Dispose());
    }

    [Fact]
    public void The_February_schema_has_the_November_columns()
    {
        using var november = Parse(SeedPost16Summary.November.Schema);
        using var february = Parse(SeedPost16Summary.February.Schema);

        Assert.Equal(
            november.RootElement.GetProperty("properties").GetRawText(),
            february.RootElement.GetProperty("properties").GetRawText());
    }

    public static TheoryData<int> StepIndexes => new() { 0, 1, 2, 3 };

    [Theory]
    [MemberData(nameof(StepIndexes))]
    public void Each_file_has_every_column_of_its_schema_in_order(int index)
    {
        var step = SeedPost16Summary.Steps[index];
        using var doc = Parse(step.Schema);

        Assert.Equal(
            doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name),
            Header(SeedPost16Summary.Csv(step, Window)));
    }

    [Theory]
    [MemberData(nameof(StepIndexes))]
    public void Each_file_has_one_row_per_school_and_a_value_in_every_column_but_the_campus(int index)
    {
        var rows = Rows(SeedPost16Summary.Csv(SeedPost16Summary.Steps[index], Window));
        var schools = SeedPupilData.Post16Pupils(Window).Select(p => p.Laestab).Distinct();

        Assert.Equal(schools, rows.Select(r => r["LAESTAB"]));
        Assert.All(rows, r => Assert.All(r.Where(p => p.Key != "CampID"), p => Assert.NotEqual(string.Empty, p.Value)));
    }

    [Fact]
    public void A_value_carries_from_October_to_November_and_from_February_to_March_but_the_revision_changes_it()
    {
        var october = Rows(SeedPost16Summary.Csv(SeedPost16Summary.October, Window))[0];
        var november = Rows(SeedPost16Summary.Csv(SeedPost16Summary.November, Window))[0];
        var february = Rows(SeedPost16Summary.Csv(SeedPost16Summary.February, Window))[0];
        var march = Rows(SeedPost16Summary.Csv(SeedPost16Summary.March, Window))[0];

        Assert.All(october.Keys, k => Assert.Equal(october[k], november[k]));
        Assert.All(february.Keys, k => Assert.Equal(february[k], march[k]));
        Assert.NotEqual(november["TALLPUP_1618"], february["TALLPUP_1618"]);
    }

    [Fact]
    public void Each_value_added_score_sits_inside_its_confidence_interval()
    {
        var row = Rows(SeedPost16Summary.Csv(SeedPost16Summary.November, Window))[0];

        Assert.All(row.Keys.Where(k => k.StartsWith("VA_INS_", StringComparison.Ordinal)), k =>
        {
            var measure = k["VA_".Length..];
            var score = decimal.Parse(row[k], CultureInfo.InvariantCulture);
            Assert.True(decimal.Parse(row["LCI_" + measure], CultureInfo.InvariantCulture) < score);
            Assert.True(decimal.Parse(row["UCI_" + measure], CultureInfo.InvariantCulture) > score);
        });
    }

    private static string[] Header(byte[] file) =>
        Encoding.UTF8.GetString(file).Split('\n')[0].Trim().Split(',');

    // The generated values hold no commas or quotes, so a plain split is enough here.
    private static List<Dictionary<string, string>> Rows(byte[] file)
    {
        var lines = Encoding.UTF8.GetString(file).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToList();
        var header = lines[0].Split(',');
        return lines.Skip(1)
            .Select(l => header.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second))
            .ToList();
    }

    private static string? Text(JsonDocument doc, string section, string name) =>
        doc.RootElement.GetProperty(section).GetProperty(name).GetString();

    private static JsonDocument Parse(string schema) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder, schema)));

    private static string SchemaFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..",
        "src", "DfE.CheckPerformanceData.Web", "Data", "Ingress", "post16"));

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
