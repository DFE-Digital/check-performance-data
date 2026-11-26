using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class MissingQualificationKs4AutumnFlowTests
{
    private const string Ks4File = "MissingQualification_KS4Autumn.json";
    private const string Post16File = "MissingQualification_Post16.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly QuestionFlowConfig Ks4 = Load(Ks4File);
    private static readonly QuestionFlowConfig Post16 = Load(Post16File);

    private static string PupilPageId(string post16PageId) =>
        post16PageId.Replace("select-student-", "select-pupil-", StringComparison.Ordinal);

    [Fact]
    public void The_pages_are_the_16_19_pages_with_pupil_in_the_search_page_ids()
    {
        Assert.Equal(PupilPageId(Post16.FirstPageId), Ks4.FirstPageId);
        Assert.Equal(Post16.Pages.Select(p => PupilPageId(p.Id)), Ks4.Pages.Select(p => p.Id));
        Assert.Equal(Post16.Pages.Select(p => p.Type), Ks4.Pages.Select(p => p.Type));
        Assert.Equal(
            Post16.Pages.Select(p => p.NextPageId is null ? null : PupilPageId(p.NextPageId)),
            Ks4.Pages.Select(p => p.NextPageId));
    }

    [Fact]
    public void The_questions_have_the_ids_types_roles_and_validators_of_the_16_19_flow()
    {
        var expected = Post16.Pages.SelectMany(p => p.Questions)
            .Select(q => (q.Id, q.Type, q.Role, q.Validator, q.Optional)).ToList();
        var actual = Ks4.Pages.SelectMany(p => p.Questions)
            .Select(q => (q.Id, q.Type, q.Role, q.Validator, q.Optional)).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void The_cohort_options_have_the_values_and_meanings_of_the_16_19_flow()
    {
        static IEnumerable<(string, string?, string?)> Options(QuestionFlowConfig flow) =>
            flow.Pages.SelectMany(p => p.Questions).Where(q => q.Options is not null)
                .SelectMany(q => q.Options!).Select(o => (o.Value, o.Meaning?.ToString(), o.NextPageId));

        Assert.Equal(
            Options(Post16).Select(o => (o.Item1, o.Item2, o.Item3 is null ? null : PupilPageId(o.Item3))),
            Options(Ks4));
    }

    [Fact]
    public void Neither_pupil_search_restricts_to_pupils_with_results()
    {
        var searches = Ks4.Pages.Where(p => p.Type == PageType.PupilSearch).ToList();

        Assert.Equal(2, searches.Count);
        Assert.All(searches, p => Assert.False(p.RequireResults));
        Assert.All(searches, p => Assert.Equal(PupilFilter.All, p.PupilFilter));
    }

    [Fact]
    public void The_cohort_count_states_its_own_message_for_a_number_that_is_not_whole()
    {
        var count = Ks4.Pages.SelectMany(p => p.Questions).Single(q => q.Id == "q-cohort-count");

        Assert.False(string.IsNullOrWhiteSpace(count.FormatFailure));
    }

    [Fact]
    public void The_award_date_has_the_rules_of_the_16_19_flow()
    {
        static string Rules(QuestionFlowConfig flow) => JsonSerializer.Serialize(
            flow.Pages.SelectMany(p => p.Questions).Single(q => q.Id == "q-award-date").DateRules);

        Assert.Equal(Rules(Post16), Rules(Ks4));
    }

    [Theory]
    [InlineData("student")]
    [InlineData("ULN")]
    public void No_text_of_the_file_uses_a_16_19_word(string word)
    {
        var text = File.ReadAllText(Locate(Ks4File));

        Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    private static QuestionFlowConfig Load(string fileName)
        => JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(Locate(fileName)), JsonOptions)!;

    private static string Locate(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate " + fileName + " from " + AppContext.BaseDirectory);
    }
}
