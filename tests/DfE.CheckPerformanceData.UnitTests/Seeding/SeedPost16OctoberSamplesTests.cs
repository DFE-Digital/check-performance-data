using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// The "16 to 19 Oct" window is left for an admin to import. These pin that what they import gives
/// the journey what it reads: the results columns are all declared by the schema the admin pairs
/// them with, every file carries the LAESTAB that splits it per school, and every result names a
/// student in the student files. (Ingress drops a CSV column the schema does not declare, so the
/// generated student files may carry extra columns.)
/// </summary>
public sealed class SeedPost16OctoberSamplesTests
{
    private static readonly IReadOnlyDictionary<string, byte[]> Files = SeedPost16OctoberSamples.Files();

    [Fact]
    public void The_October_files_are_the_two_student_files_and_three_results_files()
        => Assert.Equal(
            [
                "results/16to19_INC.csv", "results/16to19_LR1.csv", "results/16to19_NONINC.csv",
                "students/included.csv", "students/nonincluded.csv"
            ],
            Files.Keys.Order());

    [Theory]
    [InlineData("results/16to19_INC.csv", "results-included_schema.json")]
    [InlineData("results/16to19_NONINC.csv", "results-non-included_schema.json")]
    [InlineData("results/16to19_LR1.csv", "results-late_schema.json")]
    public void Every_results_column_is_read_by_the_schema_the_admin_pairs_it_with(string file, string schema)
    {
        // Declared by name, or named as an x-ingress.source (GNUMBER feeds QAN in a late file).
        var read = Properties(schema).Concat(SourceColumns(schema)).ToHashSet();

        Assert.All(Header(file), column => Assert.Contains(column, read));
    }

    [Theory]
    [InlineData("results/16to19_INC.csv")]
    [InlineData("results/16to19_NONINC.csv")]
    public void The_results_files_are_in_the_supplier_shape(string file)
        // The 16-18 results data specification, fields 1-32 in column order, headed by field
        // reference. No journey column (QAN, SESSION...) is supplied: the schema derives them.
        => Assert.Equal(
            ["ULN", "CYPMD_ID", "SURNAME", "FORENAMES", "SEX", "DOB", "AGE", "EXAMNO", "GNUMBER", "AB_Code",
             "EXAMYEAR", "SEASON", "exam_date", "Short_Qual_Desc", "SubjectDescription", "GRADE", "POINTS_1618",
             "CAPPED_PTS", "ANCN", "ADFECN", "BRDSUBNO", "MAPPING", "Level3QualificationCategory",
             "EMQualificationCategory", "R_INCL", "R_INCL_EM", "QUAL_KS4", "UKPRN", "URN", "LAESTAB", "cypmd_pk"],
            Header(file));

    [Theory]
    [InlineData("results-included_schema.json", "Capped points for English and Maths progress")]
    [InlineData("results-non-included_schema.json", "Capped points for English and Math progress")]
    public void The_results_download_has_the_specifications_headings_in_its_column_order(
        string schema, string cappedHeading)
    {
        // The data specification's "column heading provided in file" is the heading of the CSV a
        // school downloads, in column order A-AD. The values derived for the journey (QAN,
        // QUAL_NAME, SYLLABUS, SESSION) repeat GNUMBER, BRDSUBNO and the rest, so they are neither
        // downloaded nor shown on the Results tab.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder, schema)));
        var exported = doc.RootElement.GetProperty("properties").EnumerateObject()
            .Select(p => (Csv: p.Value.GetProperty("x-csv"), Name: p.Name))
            .Where(p => p.Csv.GetProperty("columns").TryGetProperty("default", out _))
            .OrderBy(p => ColumnNumber(p.Csv.GetProperty("columns").GetProperty("default").GetString()!))
            .Select(p => p.Csv.GetProperty("heading").GetString())
            .ToList();

        Assert.Equal(
            ["ULN", "CYPMD ID", "Surname", "Forename", "Sex", "Date of birth", "Age", "Exam", "Qualification number",
             "Awarding Organisation", "Year", "Season", "Exam date", "Qualification type", "Subject name", "Grade",
             "Point score", cappedHeading, "NCN", "ADFECN", "Syllabus code",
             "LEAP/LDCS code", "Qualification category", "English and maths qualification category",
             "Exam inclusion status - attainment measures", "English and maths included",
             "Qualifications taken during KS4", "UKPRN", "URN", "DfE number"],
            exported);


        foreach (var derived in new[] { "QAN", "QUAL_NAME", "SYLLABUS", "SESSION" })
        {
            var property = doc.RootElement.GetProperty("properties").GetProperty(derived);
            Assert.False(property.GetProperty("x-display").GetProperty("visible").GetBoolean(), derived);
        }
    }

    private static int ColumnNumber(string letters) => letters.Aggregate(0, (n, c) => n * 26 + (c - 'A' + 1));

    [Fact]
    public void The_late_file_is_in_the_supplier_shape_and_marks_the_amendment()
    {
        Assert.Equal(
            ["LAESTAB", "CYPMD_ID", "SURNAME", "FORENAMES", "AB_CODE_NDAQ", "Short_Qual_Desc", "EXAM_YEAR_SEASON",
             "EXAM_DATE", "Discount_Code", "SYLLABUS_TITLE", "GNUMBER", "BRDSUBNO", "GRADE", "Late_Result_Type"],
            Header("results/16to19_LR1.csv"));
        var sport = Assert.Single(Rows("results/16to19_LR1.csv"), r => r["CYPMD_ID"] == "500002" && r["GNUMBER"] == "60172186");
        Assert.Equal("Amendment", sport["Late_Result_Type"]);
        Assert.Contains(Rows("results/16to19_LR1.csv"), r => r["Late_Result_Type"] == "New");
    }

    [Theory]
    [InlineData("students/included.csv", "students-included_schema.json")]
    [InlineData("students/nonincluded.csv", "students-non-included_schema.json")]
    [InlineData("results/16to19_INC.csv", "results-included_schema.json")]
    [InlineData("results/16to19_NONINC.csv", "results-non-included_schema.json")]
    [InlineData("results/16to19_LR1.csv", "results-late_schema.json")]
    public void Every_file_carries_the_student_id_and_LAESTAB_its_schema_declares(string file, string schema)
    {
        var declared = Properties(schema);

        foreach (var column in new[] { "CYPMD_ID", "LAESTAB" })
        {
            Assert.Contains(column, Header(file));
            Assert.Contains(column, declared);
        }
    }

    [Theory]
    [InlineData("results-included_schema.json")]
    [InlineData("results-non-included_schema.json")]
    [InlineData("results-late_schema.json")]
    [InlineData("results-late-2_schema.json")]
    public void Every_results_schema_shows_the_student_and_the_grade_on_the_Results_tab(string schema)
    {
        // A schema with no visible column draws a table with nothing in it. The workbook's
        // non-included results schema was exactly that.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder, schema)));
        var visible = doc.RootElement.GetProperty("properties").EnumerateObject()
            .Where(p => p.Value.TryGetProperty("x-display", out var d) && d.TryGetProperty("visible", out var v) && v.GetBoolean())
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Contains("SURNAME", visible);
        Assert.Contains("GRADE", visible);
    }

    [Theory]
    [InlineData("results-included_schema.json")]
    [InlineData("results-non-included_schema.json")]
    [InlineData("results-late_schema.json")]
    [InlineData("results-late-2_schema.json")]
    public void The_results_schemas_set_no_length_limits(string schema)
    {
        // The specifications' lengths were written for SQL tables. In a CSV import one long value
        // would fail validation and stop the whole run, so no results property has a maxLength.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder, schema)));

        Assert.All(doc.RootElement.GetProperty("properties").EnumerateObject(),
            p => Assert.False(p.Value.TryGetProperty("maxLength", out _), p.Name));
    }

    [Theory]
    [InlineData("results-included_schema.json")]
    [InlineData("results-non-included_schema.json")]
    [InlineData("results-late_schema.json")]
    [InlineData("results-late-2_schema.json")]
    public void The_results_schemas_declare_what_the_journey_reads_and_the_stamped_source(string schema)
    {
        var declared = Properties(schema);

        Assert.All(new[] { "CYPMD_ID", "QAN", "QUAL_NAME", "SYLLABUS", "SESSION", "GRADE", "SOURCE" },
            column => Assert.Contains(column, declared));
    }

    [Fact]
    public void Every_result_names_a_student_in_the_student_files()
    {
        var students = Rows("students/included.csv").Concat(Rows("students/nonincluded.csv"))
            .Select(r => r["CYPMD_ID"]).ToHashSet();

        foreach (var file in Files.Keys.Where(k => k.StartsWith("results/")))
            Assert.All(Rows(file), r => Assert.Contains(r["CYPMD_ID"], students));
    }

    [Fact]
    public void Each_results_file_holds_exactly_its_own_rows()
    {
        foreach (var tag in new[] { ResultsFileTags.Post16Included, ResultsFileTags.Post16NonIncluded, ResultsFileTags.Post16LateResults1 })
            Assert.Equal(SeedStudentResults.All.Count(r => r.SourceFile == tag), Rows($"results/{tag}.csv").Count);
    }

    private static string[] Header(string file) =>
        Encoding.UTF8.GetString(Files[file]).Split('\n')[0].Trim().Split(',');

    // The generated values hold no commas or quotes, so a plain split is enough here.
    private static List<Dictionary<string, string>> Rows(string file)
    {
        var lines = Encoding.UTF8.GetString(Files[file]).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToList();
        var header = lines[0].Split(',');
        return lines.Skip(1)
            .Select(l => header.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second))
            .ToList();
    }

    private static HashSet<string> Properties(string schema)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder, schema)));
        return doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
    }

    private static IEnumerable<string> SourceColumns(string schema)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaFolder, schema)));
        // A source is one column, or a list of columns to join.
        return doc.RootElement.GetProperty("properties").EnumerateObject()
            .Where(p => p.Value.TryGetProperty("x-ingress", out var ingress) && ingress.TryGetProperty("source", out _))
            .Select(p => p.Value.GetProperty("x-ingress").GetProperty("source"))
            .SelectMany(source => source.ValueKind == JsonValueKind.Array
                ? source.EnumerateArray().Select(c => c.GetString()!)
                : [source.GetString()!])
            .ToList();
    }

    private static string SchemaFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..",
        "src", "DfE.CheckPerformanceData.Web", "Data", "Ingress", "post16"));

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
