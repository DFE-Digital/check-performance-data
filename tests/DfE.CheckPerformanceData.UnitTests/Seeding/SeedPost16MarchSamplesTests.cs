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
    public void The_March_samples_are_the_included_revised_with_retention_file_the_aims_file_the_value_added_file_and_the_summary()
        => Assert.Equal(
            [
                "results/16to19_INC_REV_RET.csv", "students/aims.csv", "students/value-added-revised-retention.csv",
                "summary/summary-value-added-revised-retention.csv"
            ],
            Files.Keys.Order());

    [Fact]
    public void The_aims_file_has_every_specified_column_in_order_and_its_schema_reads_each_one()
    {
        using var doc = Parse(SeedPost16MarchSamples.AimsSchema);
        var properties = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

        // Fields 1-15 of the data specification, by field reference.
        Assert.Equal(
            ["LAESTAB", "URN", "UKPRN", "AimLAESTAB", "ULN", "CYPMD_ID", "SURNAME", "FORENAMES", "SEX", "DOB",
             "AGE", "LearningAimReference", "Subj_Desc", "Aim_Type", "cypmd_pk"],
            Header(Files[SeedPost16MarchSamples.AimsFile]));
        Assert.Equal(Header(Files[SeedPost16MarchSamples.AimsFile]), properties);
    }

    [Fact]
    public void Every_included_student_has_an_aim_and_some_aims_are_recorded_elsewhere()
    {
        var rows = Rows(Files[SeedPost16MarchSamples.AimsFile]);
        var included = SeedPupilData.Post16Pupils(Guid.Empty).Where(p => p.Included).ToList();

        Assert.Equal(included.Select(p => (p.Laestab, p.Cypmd_Id)).Order(),
            rows.Select(r => (r["LAESTAB"], r["CYPMD_ID"])).Distinct().Order());
        Assert.Contains(rows, r => r["AimLAESTAB"] == SeedPost16MarchSamples.PartnerAimLaestab);
        Assert.All(rows, r => Assert.Contains(r["Aim_Type"], new[] { "2", "4", "5" }));
        Assert.All(rows, r => Assert.Equal(r["AimLAESTAB"] + r["CYPMD_ID"] + r["LearningAimReference"], r["cypmd_pk"]));
    }

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
