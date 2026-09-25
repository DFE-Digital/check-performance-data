using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// The "16 to 19 Feb" window has the October files and late results 2 imported by the seed, and
/// its revised files are left for an admin to add. These pin that the revised files replace the four
/// earlier files, and that their schemas read every column and are their own datasets.
/// </summary>
public sealed class SeedPost16FebruarySamplesTests
{
    private static readonly IReadOnlyDictionary<string, byte[]> Files = SeedPost16FebruarySamples.Files();
    private static readonly IReadOnlyDictionary<string, byte[]> October = SeedPost16OctoberSamples.Files();

    [Fact]
    public void The_February_samples_are_the_two_revised_files()
        => Assert.Equal(["results/16to19_INC_REV.csv", "results/16to19_NONINC_REV.csv"], Files.Keys.Order());

    [Theory]
    [InlineData(SeedPost16FebruarySamples.IncludedRevisedFile, "results/16to19_INC.csv")]
    [InlineData(SeedPost16FebruarySamples.NonIncludedRevisedFile, "results/16to19_NONINC.csv")]
    public void A_revised_file_has_the_same_columns_as_the_file_it_replaces(string revised, string original)
        => Assert.Equal(Header(October[original]), Header(Files[revised]));

    [Theory]
    [InlineData("results-included-revised_schema.json", SeedPost16FebruarySamples.IncludedRevisedFile)]
    [InlineData("results-non-included-revised_schema.json", SeedPost16FebruarySamples.NonIncludedRevisedFile)]
    public void Every_column_is_read_by_the_revised_schema(string schema, string file)
    {
        using var doc = Parse(schema);
        var properties = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.All(Header(Files[file]), column => Assert.Contains(column, properties));
    }

    [Theory]
    [InlineData("results-included_schema.json", "results-included-revised_schema.json")]
    [InlineData("results-non-included_schema.json", "results-non-included-revised_schema.json")]
    public void A_revised_schema_has_the_original_properties_but_is_its_own_dataset(string original, string revised)
    {
        using var first = Parse(original);
        using var second = Parse(revised);

        Assert.Equal(
            first.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
            second.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.NotEqual(Text(first, "x-ingress", "collection"), Text(second, "x-ingress", "collection"));
        Assert.NotEqual(Text(first, "x-display", "section"), Text(second, "x-display", "section"));
        Assert.NotEqual(Text(first, "x-download", "fileName"), Text(second, "x-download", "fileName"));
    }

    [Fact]
    public void The_revised_files_hold_one_row_for_every_earlier_result()
    {
        var earlier = SeedStudentResults.All.Concat(SeedStudentResults.LateResults2)
            .Select(r => (r.CypmdId, r.Qan, r.Session)).ToHashSet();
        var revised = SeedStudentResults.Revised.Select(r => (r.CypmdId, r.Qan, r.Session)).ToList();

        Assert.Equal(revised.Count, revised.Distinct().Count());
        Assert.Equal(earlier.Order(), revised.Order());
        Assert.Equal(revised.Count, Rows(Files[SeedPost16FebruarySamples.IncludedRevisedFile]).Count
            + Rows(Files[SeedPost16FebruarySamples.NonIncludedRevisedFile]).Count);
    }

    [Fact]
    public void A_late_amendment_replaces_the_grade_it_corrects()
    {
        string GradeOf(string cypmd, string qan) =>
            Assert.Single(SeedStudentResults.Revised, r => r.CypmdId == cypmd && r.Qan == qan && r.Session == "S2024").Grade;

        Assert.Equal("7", GradeOf("500001", "60148366")); // late results 1: 6, late results 2: 7
        Assert.Equal("3", GradeOf("500003", "60146084")); // included: 2, late results 2: 3
        Assert.Equal("D", GradeOf("500002", "60172186")); // included: M, late results 1: D
        Assert.Equal("B", GradeOf("500001", "60149589")); // included: A, changed only in the revised file
    }

    [Fact]
    public void Each_student_is_in_the_revised_file_for_their_inclusion()
    {
        var included = Rows(October["students/included.csv"]).Select(r => r["CYPMD_ID"]).ToHashSet();
        var nonIncluded = Rows(October["students/nonincluded.csv"]).Select(r => r["CYPMD_ID"]).ToHashSet();

        Assert.All(Rows(Files[SeedPost16FebruarySamples.IncludedRevisedFile]), r => Assert.Contains(r["CYPMD_ID"], included));
        Assert.All(Rows(Files[SeedPost16FebruarySamples.NonIncludedRevisedFile]), r => Assert.Contains(r["CYPMD_ID"], nonIncluded));
        Assert.Contains(Rows(Files[SeedPost16FebruarySamples.NonIncludedRevisedFile]), r => r["CYPMD_ID"] == "500202");
        Assert.All(SeedStudentResults.Revised, r => Assert.Contains(r.SourceFile,
            new[] { ResultsFileTags.Post16IncludedRevised, ResultsFileTags.Post16NonIncludedRevised }));
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
