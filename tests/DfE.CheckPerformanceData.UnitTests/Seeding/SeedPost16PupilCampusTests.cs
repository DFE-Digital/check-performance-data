using System.Globalization;
using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

/// <summary>
/// Every 16-19 window has a pupil campus data share, which the October step fills. These pin that
/// the sample file has every column of the data specification, headed by field reference, that its
/// schema reads each one, and that the values hang together.
/// </summary>
public sealed class SeedPost16PupilCampusTests
{
    private static readonly Guid Window = DevDataSeeder.Post16OctoberCheckingWindowId;
    private static readonly byte[] File = SeedPost16PupilCampus.Csv(Window);

    [Fact]
    public void The_October_samples_have_the_campus_file()
        => Assert.Contains(SeedPost16PupilCampus.File, SeedPost16OctoberSamples.Files().Keys);

    [Fact]
    public void The_campus_file_has_every_specified_column_in_order_and_its_schema_reads_each_one()
    {
        string[] specified =
        [
            "CampID", "CYPMD_ID", "SURNAME", "FORENAMES", "Sex", "DOB", "AGE",
            "attendance_year_0", "attendance_year_1", "attendance_year_2", "LAESTAB", "URN", "UKPRN", "ULN",
            "KS4_DISADVANTAGE", "ALEV", "TOTENTS_ALEV", "TOTPTSE_ALEV", "ACAD", "TOTENTS_ACAD", "TOTPTSE_ACAD",
            "TLEV", "TOTENTS_TLEV", "TOTPTSE_TLEV", "AGEN", "TOTENTS_AGEN", "TOTPTSE_AGEN",
            "TechCert", "TOTENTS_TechCert", "TOTPTSE_TechCert", "TLEVEL", "TOTENTS_TLEVEL", "TOTPTSE_TLEVEL",
            "L3_FLAG", "KS4_Year_Calc"
        ];
        using var doc = Parse();

        Assert.Equal(specified, Header(File));
        Assert.Equal(specified, doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    // The download has the specification's columns A-AI, in order, with no gap.
    [Fact]
    public void The_schema_exports_every_column_in_order_A_to_AI()
    {
        using var doc = Parse();
        var columns = doc.RootElement.GetProperty("properties").EnumerateObject()
            .Select(p => p.Value.GetProperty("x-csv").GetProperty("columns").GetProperty("default").GetString())
            .ToList();

        Assert.Equal(35, columns.Count);
        Assert.Equal("A", columns[0]);
        Assert.Equal("AI", columns[^1]);
        Assert.Equal(columns.Count, columns.Distinct().Count());
    }

    [Fact]
    public void The_schema_is_its_own_dataset_split_by_LAESTAB()
    {
        using var doc = Parse();

        Assert.Equal("students-campus", doc.RootElement.GetProperty("x-ingress").GetProperty("collection").GetString());
        Assert.Equal("LAESTAB", doc.RootElement.GetProperty("x-ingress").GetProperty("institutionKey").GetString());
        Assert.Equal("Pupil campus", doc.RootElement.GetProperty("x-display").GetProperty("section").GetString());
    }

    [Fact]
    public void Every_included_student_has_one_row_and_every_school_has_two_campuses()
    {
        var rows = Rows(File);
        var students = SeedPupilData.Post16Pupils(Window).Where(p => p.Included).ToList();

        Assert.Equal(students.Select(s => s.Cypmd_Id), rows.Select(r => r["CYPMD_ID"]));
        Assert.All(rows.GroupBy(r => r["LAESTAB"]), school =>
            Assert.Equal([$"{school.Key}1", SeedPost16PupilCampus.SecondCampus(school.Key)],
                school.Select(r => r["CampID"]).Distinct().Order()));
    }

    // A student outside a programme has no entries or points in it, and the level 3 flag is set
    // only for a student in the academic, applied general or tech level programme.
    [Fact]
    public void The_programme_flags_agree_with_the_entries_and_the_level_3_flag()
    {
        Assert.All(Rows(File), row =>
        {
            foreach (var programme in new[] { "ALEV", "ACAD", "TLEV", "AGEN", "TechCert", "TLEVEL" })
            {
                var entered = row[programme] == "1";
                Assert.Equal(entered, decimal.Parse(row["TOTENTS_" + programme], CultureInfo.InvariantCulture) > 0);
                Assert.Equal(entered, decimal.Parse(row["TOTPTSE_" + programme], CultureInfo.InvariantCulture) > 0);
            }
            Assert.Equal(row["ACAD"] == "1" || row["AGEN"] == "1" || row["TLEV"] == "1", row["L3_FLAG"] == "1");
            if (row["ALEV"] == "1") Assert.Equal("1", row["ACAD"]);
        });
    }

    [Theory]
    [InlineData("01/08/2007", 2023)]
    [InlineData("01/09/2007", 2024)]
    public void The_KS4_year_is_the_summer_of_year_11(string dateOfBirth, int year)
        => Assert.Equal(year, SeedPost16PupilCampus.Ks4Year(dateOfBirth));

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

    private static JsonDocument Parse() =>
        JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(SchemaFolder, SeedPost16PupilCampus.Schema)));

    private static string SchemaFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..",
        "src", "DfE.CheckPerformanceData.Web", "Data", "Ingress", "post16"));

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
