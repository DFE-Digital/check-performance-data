using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// The "16 to 19 Nov" window has the October files imported by the seed, and its late results 2
/// file is left for an admin to add. These pin that the file and its schema give the journey what
/// it reads, and that the two late files stay two datasets.
/// </summary>
public sealed class SeedPost16NovemberSamplesTests
{
    private const string Schema = "results-late-2_schema.json";
    private static readonly IReadOnlyDictionary<string, byte[]> Files = SeedPost16NovemberSamples.Files();

    [Fact]
    public void The_only_November_sample_is_the_late_results_2_file()
        => Assert.Equal(["results/16to19_LR2.csv"], Files.Keys);

    [Fact]
    public void The_seed_imports_every_October_sample_file()
        => Assert.Equal(SeedPost16OctoberSamples.Files().Keys.Order(),
            SeedPost16NovemberSamples.OctoberImport.Select(i => i.File).Order());

    [Fact]
    public void The_late_results_2_file_has_the_same_columns_as_late_results_1()
        => Assert.Equal(Header(SeedPost16OctoberSamples.Files()["results/16to19_LR1.csv"]), Header(LateResults2));

    [Fact]
    public void Every_column_is_read_by_the_late_results_2_schema()
    {
        using var doc = Parse(Schema);
        var properties = doc.RootElement.GetProperty("properties").EnumerateObject().ToList();
        var read = properties.Select(p => p.Name)
            .Concat(properties
                .Where(p => p.Value.TryGetProperty("x-ingress", out var i) && i.TryGetProperty("source", out _))
                .Select(p => p.Value.GetProperty("x-ingress").GetProperty("source").GetString()!))
            .ToHashSet();

        Assert.All(Header(LateResults2), column => Assert.Contains(column, read));
    }

    [Fact]
    public void The_two_late_results_schemas_are_separate_datasets()
    {
        // The Results tab keys a dataset by its collection. One schema for both files would put late
        // results 1 and late results 2 under one key.
        using var first = Parse("results-late_schema.json");
        using var second = Parse(Schema);

        Assert.NotEqual(Text(first, "x-ingress", "collection"), Text(second, "x-ingress", "collection"));
        Assert.NotEqual(Text(first, "x-display", "section"), Text(second, "x-display", "section"));
        Assert.NotEqual(Text(first, "x-download", "fileName"), Text(second, "x-download", "fileName"));
    }

    [Fact]
    public void Each_row_is_an_amendment_only_when_an_earlier_file_holds_that_result()
    {
        var rows = Rows(LateResults2);
        Assert.Equal(SeedStudentResults.LateResults2.Count, rows.Count);

        string TypeOf(string cypmd, string qan) =>
            Assert.Single(rows, r => r["CYPMD_ID"] == cypmd && r["GNUMBER"] == qan)["Late_Result_Type"];

        Assert.Equal("Amendment", TypeOf("500001", "60148366")); // late results 1 English
        Assert.Equal("Amendment", TypeOf("500003", "60146084")); // included Maths
        Assert.Equal("New", TypeOf("500002", "60149589"));
        Assert.Equal("New", TypeOf("500005", "10025480"));
        Assert.Equal("New", TypeOf("500202", "60172186"));
    }

    [Fact]
    public void Two_rows_give_a_result_to_a_student_who_held_none_in_October()
    {
        var october = SeedStudentResults.All.Select(r => r.CypmdId).ToHashSet();

        Assert.Equal(["500005", "500202"],
            SeedStudentResults.LateResults2.Select(r => r.CypmdId).Where(id => !october.Contains(id)).Order());
    }

    [Fact]
    public void Every_row_names_a_student_in_the_student_files_and_is_tagged_LR2()
    {
        var october = SeedPost16OctoberSamples.Files();
        var students = Rows(october["students/included.csv"]).Concat(Rows(october["students/nonincluded.csv"]))
            .Select(r => r["CYPMD_ID"]).ToHashSet();

        Assert.All(Rows(LateResults2), r => Assert.Contains(r["CYPMD_ID"], students));
        Assert.All(SeedStudentResults.LateResults2, r => Assert.Equal(ResultsFileTags.Post16LateResults2, r.SourceFile));
        Assert.DoesNotContain(SeedStudentResults.All, r => r.SourceFile == ResultsFileTags.Post16LateResults2);
    }

    private static byte[] LateResults2 => Files[SeedPost16NovemberSamples.LateResults2File];

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
