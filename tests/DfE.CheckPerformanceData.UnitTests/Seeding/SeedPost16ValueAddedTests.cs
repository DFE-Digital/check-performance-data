using System.Globalization;
using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// Every 16-19 window's pupil data exercise shares the value added data from November, and each
/// later step replaces its file. These pin that each step's file is in the supplier's shape, that
/// each schema reads every column and is its own dataset, and that the revisions change the scores.
/// </summary>
public sealed class SeedPost16ValueAddedTests
{
    private static readonly Guid Window = DevDataSeeder.Post16NovemberCheckingWindowId;

    // The data specification's field references, in column order A-Q.
    private static readonly string[] SpecifiedColumns =
    [
        "Forename", "Surname", "Sex", "Qualification_code", "Qualification_name", "Subject_code", "Subject_name",
        "Size", "Cohort", "Prior_attainment", "Estimated_points", "Actual_points", "Value_Added_score", "QUAL_ID",
        "Disadvantaged_status", "CYPMD_ID", "Laestab"
    ];

    [Fact]
    public void The_steps_fill_the_value_added_slots_in_order()
        => Assert.Equal(SeedCheckingWindows.ValueAddedDatasets, SeedPost16ValueAdded.Steps.Select(s => s.Slot));

    [Fact]
    public void Each_schema_names_its_step_and_is_its_own_dataset()
    {
        var schemas = SeedPost16ValueAdded.Steps.Select(s => Parse(s.Schema)).ToList();

        Assert.Equal(
            ["Value added", "Value added: revised", "Value added: revised including retention"],
            schemas.Select(d => Text(d, "x-display", "section")));
        Assert.Equal(3, schemas.Select(d => Text(d, "x-ingress", "collection")).Distinct().Count());
        Assert.Equal(3, schemas.Select(d => Text(d, "x-download", "fileName")).Distinct().Count());
        Assert.All(schemas, d => Assert.Equal("Laestab", Text(d, "x-ingress", "institutionKey")));
        // The three files have the same columns.
        Assert.Single(schemas.Select(d => d.RootElement.GetProperty("properties").GetRawText()).Distinct());
        schemas.ForEach(d => d.Dispose());
    }

    [Fact]
    public void The_schema_reads_every_specified_column_in_order_and_shows_the_student_and_subject()
    {
        using var doc = Parse(SeedPost16ValueAdded.November.Schema);
        var properties = doc.RootElement.GetProperty("properties").EnumerateObject().ToList();

        Assert.Equal(SpecifiedColumns, properties.Select(p => p.Name));
        Assert.Equal(
            Enumerable.Range(0, 17).Select(i => ((char)('A' + i)).ToString()),
            properties.Select(p => p.Value.GetProperty("x-csv").GetProperty("columns").GetProperty("default").GetString()));
        // A SQL length would fail the whole run on one long value.
        Assert.All(properties, p => Assert.False(p.Value.TryGetProperty("maxLength", out _), p.Name));

        var shown = properties
            .Where(p => p.Value.GetProperty("x-display").GetProperty("visible").GetBoolean())
            .OrderBy(p => p.Value.GetProperty("x-display").GetProperty("order").GetInt32())
            .Select(p => p.Value.GetProperty("x-display").GetProperty("label").GetString());
        Assert.Equal(["Last name", "First name", "Sex", "CYPMD ID", "Subject"], shown);
    }

    public static TheoryData<int> StepIndexes => new() { 0, 1, 2 };

    [Theory]
    [MemberData(nameof(StepIndexes))]
    public void Each_file_has_every_specified_column_and_a_row_per_included_student_per_qualification(int index)
    {
        var file = SeedPost16ValueAdded.Csv(SeedPost16ValueAdded.Steps[index], Window);
        var included = SeedPupilData.Post16Pupils(Window).Count(p => p.Included);

        Assert.Equal(SpecifiedColumns, Header(file));
        // Every third student has two qualifications.
        Assert.Equal(included + (included + 2) / 3, Rows(file).Count);
    }

    [Fact]
    public void The_value_added_score_is_actual_less_estimated_points()
        => Assert.All(Rows(SeedPost16ValueAdded.Csv(SeedPost16ValueAdded.November, Window)), r =>
            Assert.Equal(
                Number(r["Actual_points"]) - Number(r["Estimated_points"]),
                Number(r["Value_Added_score"])));

    [Fact]
    public void Each_revision_changes_some_scores_but_keeps_the_rows()
    {
        var files = SeedPost16ValueAdded.Steps.Select(s => Rows(SeedPost16ValueAdded.Csv(s, Window))).ToList();

        for (var i = 1; i < files.Count; i++)
        {
            Assert.Equal(files[i - 1].Select(r => r["CYPMD_ID"] + r["QUAL_ID"]), files[i].Select(r => r["CYPMD_ID"] + r["QUAL_ID"]));
            Assert.Contains(files[i].Zip(files[i - 1]), p => p.First["Value_Added_score"] != p.Second["Value_Added_score"]);
        }
    }

    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

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
