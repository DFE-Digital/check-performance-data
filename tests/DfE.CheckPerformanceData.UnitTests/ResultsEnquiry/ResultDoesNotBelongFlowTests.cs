using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

/// <summary>
/// Pins the shipped <c>ResultDoesNotBelong_Post16.json</c> flow (AB#298704). Page and question ids
/// are a serialization contract — they are written into session state and into submitted request
/// documents — so a rename after merge orphans stored data. The copy is pinned because it is what
/// the user reads.
/// </summary>
public sealed class ResultDoesNotBelongFlowTests
{
    // Mirrors FileSystemQuestionFlowClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly QuestionFlowConfig Flow = Load();

    private static JourneyPage Page(string id) =>
        Flow.Pages.SingleOrDefault(p => p.Id == id)
        ?? throw new Xunit.Sdk.XunitException($"ResultDoesNotBelong_Post16.json has no page '{id}'.");

    private static Question Question(string pageId, string questionId) =>
        Page(pageId).Questions.Single(q => q.Id == questionId);

    [Fact]
    public void First_page_is_the_student_search() => Assert.Equal("select-student", Flow.FirstPageId);

    [Fact]
    public void Student_search_restricts_to_students_with_results()
    {
        // The stray result is HELD against the student's record (spec: "from those held against that
        // student's record"), so unlike missing-qualification (AB#297004 made that search unfiltered)
        // a student with no results cannot be this journey's subject.
        Assert.True(Page("select-student").RequireResults);
    }

    [Fact]
    public void The_journey_runs_student_then_result_then_additional_info()
    {
        Assert.Equal("select-result", Page("select-student").NextPageId);
        Assert.Equal("additional-info", Page("select-result").NextPageId);
        Assert.Null(Page("additional-info").NextPageId); // falls through to the summary
    }

    [Fact]
    public void The_result_page_carries_the_more_than_one_result_inset()
    {
        Assert.Equal(
            "If more than one result does not belong to the student, provide the QAN and grade for each result on the next page.",
            Page("select-result").Content);
    }

    [Fact]
    public void Additional_info_is_optional_with_the_higher_character_limit()
    {
        // Figma f06: 1,000 characters — deliberately above the 250 of the other enquiry types, to fit
        // a QAN + grade line per further stray result.
        var q = Question("additional-info", "q-additional-info");
        Assert.True(q.Optional);
        Assert.Equal(1000, q.CharacterLimit);
        Assert.Equal(QuestionType.TextArea, q.Type);
    }

    [Fact]
    public void There_is_no_cohort_page_and_no_late_results_interstitial()
    {
        Assert.DoesNotContain(Flow.Pages, p => p.Id == "cohort-scope");
        Assert.DoesNotContain(Flow.Pages, p => p.Id == "check-late-results");
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
        => Assert.Equal("ResultDoesNotBelong_Post16", Path.GetFileNameWithoutExtension(LocateFlowFile()));

    private static QuestionFlowConfig Load()
        => JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(LocateFlowFile()), JsonOptions)!;

    private static string LocateFlowFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", "ResultDoesNotBelong_Post16.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate ResultDoesNotBelong_Post16.json from " + AppContext.BaseDirectory);
    }
}
