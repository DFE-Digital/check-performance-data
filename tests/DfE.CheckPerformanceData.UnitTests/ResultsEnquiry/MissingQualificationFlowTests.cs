using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

/// <summary>
/// Pins the shipped <c>MissingQualification_Post16.json</c> flow (AB#297848). Page and question ids
/// are a serialization contract — they are written into session state and into submitted request
/// documents — so a rename after merge orphans stored data. The copy is pinned because it is what
/// the user reads.
/// </summary>
public sealed class MissingQualificationFlowTests
{
    // Mirrors QuestionFlowBlobClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly QuestionFlowConfig Flow = Load();

    private static JourneyPage Page(string id) =>
        Flow.Pages.SingleOrDefault(p => p.Id == id)
        ?? throw new Xunit.Sdk.XunitException($"MissingQualification_Post16.json has no page '{id}'.");

    private static Question Question(string pageId, string questionId) =>
        Page(pageId).Questions.Single(q => q.Id == questionId);

    [Fact]
    public void First_page_is_cohort_scope() => Assert.Equal("cohort-scope", Flow.FirstPageId);

    [Fact]
    public void Neither_student_search_restricts_to_students_with_results()
    {
        // AB#297004: the search returns what matches, both populations, unfiltered. A student
        // whose only qualification is the missing one holds NO results, so requireResults would
        // make exactly the students this journey exists for unfindable.
        Assert.False(Page("select-student-cohort").RequireResults);
        Assert.False(Page("select-student-single").RequireResults);
    }

    [Fact]
    public void Cohort_yes_routes_to_count_then_example_student()
    {
        var options = Question("cohort-scope", "q-cohort-scope").Options!;
        var yes = options.Single(o => o.Value == "yes");
        Assert.Equal("cohort-count", yes.NextPageId);
        Assert.Equal("select-student-cohort", Page("cohort-count").NextPageId);
    }

    [Fact]
    public void Cohort_no_routes_straight_to_single_student_search()
    {
        var options = Question("cohort-scope", "q-cohort-scope").Options!;
        var no = options.Single(o => o.Value == "no");
        Assert.Equal("select-student-single", no.NextPageId);
    }

    [Theory]
    [InlineData("select-student-cohort")]
    [InlineData("select-student-single")]
    public void Both_student_searches_continue_to_the_qualification_page(string pageId)
    {
        var page = Page(pageId);
        Assert.Equal("select-qualification", page.NextPageId);
        Assert.Equal(PageType.PupilSearch, page.Type);
    }

    [Fact]
    public void The_qualification_search_page_advances_to_details()
    {
        var page = Page("select-qualification");
        Assert.Equal(PageType.QualificationSearch, page.Type);
        Assert.Equal("qualification-details", page.NextPageId);
    }

    [Fact]
    public void The_details_page_asks_syllabus_date_grade_then_ncn_in_that_order()
    {
        var page = Page("qualification-details");
        Assert.Equal(PageType.QualificationDetails, page.Type);
        Assert.Equal(
            ["q-syllabus-code", "q-award-date", "q-missing-grade", "q-ncn"],
            page.Questions.Select(q => q.Id).ToArray());
        Assert.Equal(
            [QuestionType.SyllabusSelect, QuestionType.Date, QuestionType.GradeSelect, QuestionType.FreeText],
            page.Questions.Select(q => q.Type).ToArray());
        Assert.Equal("additional-info", page.NextPageId);
    }

    [Fact]
    public void Ncn_is_the_only_optional_details_question()
    {
        var page = Page("qualification-details");
        var optional = page.Questions.Where(q => q.Optional).Select(q => q.Id);
        Assert.Equal(["q-ncn"], optional.ToArray());
    }

    [Fact]
    public void There_is_no_late_results_interstitial()
        => Assert.DoesNotContain(Flow.Pages, p => p.Id == "check-late-results");

    [Fact]
    public void Validation_copy_is_pinned()
    {
        Assert.Equal(
            "Select if the missing qualification affects the whole cohort",
            Question("cohort-scope", "q-cohort-scope").ValidationFailure);
        Assert.Equal(
            "Enter the name of the student missing this qualification",
            Page("select-student-cohort").ValidationFailure);
        Assert.Equal(
            "Enter the name of the student missing this qualification",
            Page("select-student-single").ValidationFailure);
        Assert.Equal(
            "Select the Qualification Number (QAN)",
            Page("select-qualification").ValidationFailure);
        Assert.Equal(
            "Select the syllabus code",
            Question("qualification-details", "q-syllabus-code").ValidationFailure);
        Assert.Equal(
            "Provide the award date",
            Question("qualification-details", "q-award-date").ValidationFailure);
        Assert.Equal(
            "Select the missing grade this student achieved",
            Question("qualification-details", "q-missing-grade").ValidationFailure);
    }

    [Fact]
    public void Every_next_page_target_resolves_to_a_real_page()
    {
        var ids = Flow.Pages.Select(p => p.Id).ToHashSet();

        foreach (var page in Flow.Pages)
        {
            if (page.NextPageId is not null)
                Assert.Contains(page.NextPageId, ids);

            foreach (var option in page.Questions.SelectMany(q => q.Options ?? []))
                if (option.NextPageId is not null)
                    Assert.Contains(option.NextPageId, ids);
        }

        Assert.Contains(Flow.FirstPageId, ids);
    }

    [Fact]
    public void No_question_id_is_reused_across_the_flow()
    {
        var ids = Flow.Pages.SelectMany(p => p.Questions).Select(q => q.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void The_config_key_is_the_journey_and_window_type_this_flow_serves()
        => Assert.Equal("MissingQualification_Post16", Path.GetFileNameWithoutExtension(LocateFlowFile()));

    private static QuestionFlowConfig Load()
        => JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(LocateFlowFile()), JsonOptions)!;

    private static string LocateFlowFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", "MissingQualification_Post16.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate MissingQualification_Post16.json from " + AppContext.BaseDirectory);
    }
}
