using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// The "16 to 19 Mar" window has every earlier file imported by the seed, then the included revised
/// with retention file, which replaces included revised. These pin that the file replaces it row for
/// row, and that its schema reads every column and is its own dataset.
/// </summary>
public sealed class SeedPost16MarchSamplesTests
{
    private static readonly IReadOnlyDictionary<string, byte[]> Files = SeedPost16MarchSamples.Files();
    private static byte[] Retention => Files[SeedPost16MarchSamples.IncludedRevisedWithRetentionFile];

    [Fact]
    public void The_only_March_sample_is_the_included_revised_with_retention_file()
        => Assert.Equal(["results/16to19_INC_REV_RET.csv"], Files.Keys);

    [Fact]
    public void It_has_the_same_columns_as_the_included_revised_file()
        => Assert.Equal(
            Header(SeedPost16FebruarySamples.Files()[SeedPost16FebruarySamples.IncludedRevisedFile]),
            Header(Retention));

    [Fact]
    public void Every_column_is_read_by_its_schema()
    {
        using var doc = Parse(SeedPost16MarchSamples.IncludedRevisedWithRetentionSchema);
        var properties = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.All(Header(Retention), column => Assert.Contains(column, properties));
    }

    [Fact]
    public void Its_schema_has_the_included_revised_properties_but_is_its_own_dataset()
    {
        using var first = Parse("results-included-revised_schema.json");
        using var second = Parse(SeedPost16MarchSamples.IncludedRevisedWithRetentionSchema);

        Assert.Equal(
            first.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
            second.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.NotEqual(Text(first, "x-ingress", "collection"), Text(second, "x-ingress", "collection"));
        Assert.NotEqual(Text(first, "x-display", "section"), Text(second, "x-display", "section"));
        Assert.NotEqual(Text(first, "x-download", "fileName"), Text(second, "x-download", "fileName"));
    }

    [Fact]
    public void It_holds_one_row_for_every_included_revised_result_and_changes_one_grade()
    {
        var revised = SeedStudentResults.Revised.Where(r => r.SourceFile == ResultsFileTags.Post16IncludedRevised).ToList();
        var retention = SeedStudentResults.IncludedRevisedWithRetention;

        Assert.Equal(revised.Select(r => (r.CypmdId, r.Qan, r.Session)).Order(),
            retention.Select(r => (r.CypmdId, r.Qan, r.Session)).Order());
        Assert.Equal(retention.Count, Rows(Retention).Count);
        Assert.All(retention, r => Assert.Equal(ResultsFileTags.Post16IncludedRevisedWithRetention, r.SourceFile));

        var changed = retention.Where(r => r.Grade != revised.Single(v =>
            (v.CypmdId, v.Qan, v.Session) == (r.CypmdId, r.Qan, r.Session)).Grade).ToList();
        var only = Assert.Single(changed);
        Assert.Equal(SeedStudentResults.RetentionOnlyGrade, ((only.CypmdId, only.Qan, only.Session), only.Grade));
        Assert.Equal("4", only.Grade); // Charlie Smith's Maths: 3 in the revised file
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
