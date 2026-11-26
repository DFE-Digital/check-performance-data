using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public class Ks4QualificationReferenceSeedDocumentTests
{
    private const string FileName = "qualification-reference-ks4.json";
    private const int Qualifications = 309;
    private const int AwardingOrganisations = 12;
    private const int QualificationsWithSyllabusCodes = 60;

    private static readonly Lazy<JsonDocument> Doc = new(() =>
        JsonDocument.Parse(File.ReadAllText(FindSeedPath())));

    private static string FindSeedPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "DfE.CheckPerformanceData.Web", "Data", "QualificationReference", FileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {FileName} from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Holds_every_qualification_of_the_ks4_sheet()
    {
        Assert.Equal(Qualifications, Doc.Value.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void Names_the_twelve_awarding_organisations_of_the_ks4_sheet()
    {
        var organisations = Doc.Value.RootElement.EnumerateObject()
            .Select(e => e.Value.GetProperty("awardingOrganisation").GetString())
            .Distinct().ToList();

        Assert.Equal(AwardingOrganisations, organisations.Count);
        Assert.Contains("City & Guilds", organisations);
    }

    [Fact]
    public void Sixty_qualifications_have_ks4_syllabus_codes()
    {
        var covered = Doc.Value.RootElement.EnumerateObject()
            .Count(e => e.Value.GetProperty("syllabusCodes").GetArrayLength() > 0);

        Assert.Equal(QualificationsWithSyllabusCodes, covered);
    }

    [Fact]
    public void Holds_a_gcse_that_the_16_19_list_does_not_hold()
    {
        var gcse = Doc.Value.RootElement.GetProperty("60147891");

        Assert.Equal("Pearson", gcse.GetProperty("awardingOrganisation").GetString());
        Assert.Equal(
            new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Q", "R", "U", "X" },
            gcse.GetProperty("grades").EnumerateArray().Select(g => g.GetString()).ToArray());
    }

    [Fact]
    public void A_qualification_carries_its_ks4_syllabus_codes()
    {
        var codes = Doc.Value.RootElement.GetProperty("60146084").GetProperty("syllabusCodes")
            .EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToArray();

        Assert.Equal(new[] { "8300F", "8300H" }, codes);
    }

    [Fact]
    public void Every_entry_names_its_qan_title_ao_and_at_least_one_grade()
    {
        foreach (var entry in Doc.Value.RootElement.EnumerateObject())
        {
            Assert.Equal(entry.Name, entry.Value.GetProperty("qan").GetString());
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.GetProperty("qualificationTitle").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.GetProperty("awardingOrganisation").GetString()));
            Assert.NotEqual(0, entry.Value.GetProperty("grades").GetArrayLength());
        }
    }

    [Fact]
    public void No_qualification_repeats_a_syllabus_code()
    {
        foreach (var entry in Doc.Value.RootElement.EnumerateObject())
        {
            var codes = entry.Value.GetProperty("syllabusCodes").EnumerateArray()
                .Select(c => c.GetProperty("code").GetString()).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
    }
}
