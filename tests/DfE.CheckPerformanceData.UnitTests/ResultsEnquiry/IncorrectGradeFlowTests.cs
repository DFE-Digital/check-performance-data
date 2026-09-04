using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

/// <summary>
/// Pins the shipped <c>IncorrectGrade_Post16.json</c> flow. Page and question ids are a
/// serialization contract — they are written into session state and into submitted request
/// documents — so a rename after merge orphans stored data. The copy is pinned because it is what
/// the user reads.
/// </summary>
public sealed class IncorrectGradeFlowTests
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
        ?? throw new Xunit.Sdk.XunitException($"IncorrectGrade_Post16.json has no page '{id}'.");

    private static Question Question(string pageId, string questionId) =>
        Page(pageId).Questions.Single(q => q.Id == questionId);

    [Fact]
    public void The_journey_starts_at_the_cohort_scope_question()
    {
        // NOT the interstitial: whether the "check your second late results file" page is shown is a
        // runtime decision (does the school hold any LR2 result?), so the controller enters it. Making
        // it firstPageId would show it unconditionally.
        Assert.Equal("cohort-scope", Flow.FirstPageId);
    }

    [Fact]
    public void Every_page_id_is_present_exactly_once_and_none_have_been_renamed()
    {
        Assert.Equal(
            [
                "check-late-results", "cohort-scope", "cohort-count",
                "select-student-cohort", "select-student-single",
                "select-result", "grade-details", "additional-info"
            ],
            Flow.Pages.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void Every_next_page_target_resolves_to_a_real_page()
    {
        // A dangling nextPageId strands the user mid-journey with no way forward.
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

    // ── The interstitial ─────────────────────────────────────────────────────

    [Fact]
    public void The_late_results_interstitial_informs_and_continues_into_the_journey()
    {
        // AB#296648: "The guidance informs the user. It does not stop the user continuing."
        var page = Page("check-late-results");

        Assert.Equal(PageType.Content, page.Type);
        Assert.Equal("Check your second late results file before you report an incorrect grade", page.Title);
        Assert.Equal(
            "Nearly all incorrect grades are updated in the second late results file. " +
            "We will email you in late November to let you know it's available.",
            page.Content);
        Assert.Equal("cohort-scope", page.NextPageId);
        Assert.Empty(page.Questions);
    }

    // ── Cohort scope ─────────────────────────────────────────────────────────

    [Fact]
    public void The_cohort_scope_question_is_a_radio_with_the_pinned_copy()
    {
        var page = Page("cohort-scope");
        var question = Question("cohort-scope", "q-cohort-scope");

        Assert.Equal(PageType.Question, page.Type);
        // No page-level title — see Single_question_pages_leave_the_page_title_empty.
        Assert.Null(page.Title);
        Assert.Equal(QuestionType.Radio, question.Type);
        Assert.Equal("Does the incorrect grade affect the whole cohort?", question.Title);
        Assert.Equal("Select one option", question.Hint);
        Assert.Equal("Select if the incorrect grade affects the whole cohort", question.ValidationFailure);
    }

    [Fact]
    public void Yes_asks_for_a_count_and_no_goes_straight_to_one_student()
    {
        // The branch is the whole point of the question: a cohort-wide problem has one cause, so
        // asking for every student would create duplicate enquiries.
        var options = Question("cohort-scope", "q-cohort-scope").Options!;

        Assert.Equal(2, options.Count);

        Assert.Equal("yes", options[0].Value);
        Assert.Equal("Yes", options[0].Label);
        Assert.Equal("Provide an example of one student", options[0].SubLabel);
        Assert.Equal("cohort-count", options[0].NextPageId);

        Assert.Equal("no", options[1].Value);
        Assert.Equal("No", options[1].Label);
        Assert.Equal("Complete a separate enquiry for each student affected", options[1].SubLabel);
        Assert.Equal("select-student-single", options[1].NextPageId);
    }

    [Fact]
    public void Neither_cohort_option_is_conditionally_hidden()
    {
        Assert.All(Question("cohort-scope", "q-cohort-scope").Options!, o => Assert.Null(o.VisibleWhen));
    }

    // ── Cohort count ─────────────────────────────────────────────────────────

    [Fact]
    public void The_cohort_count_is_a_validated_whole_number()
    {
        var page = Page("cohort-count");
        var question = Question("cohort-count", "q-cohort-count");

        Assert.Null(page.Title);
        Assert.Equal(
            "Enter the number of students who have an incorrect grade for this qualification",
            question.Title);
        Assert.Equal("select-student-cohort", page.NextPageId);
        Assert.Equal(QuestionType.FreeText, question.Type);
        Assert.Equal("Enter the number of students", question.Hint);
        // The engine fails OPEN on an unresolved validator name, so this string must match
        // WholeNumberFormatValidator.Name exactly or the format check is silently skipped.
        Assert.Equal("WholeNumber", question.Validator);
        Assert.Equal("Enter how many students have an incorrect grade for this qualification", question.ValidationFailure);
        Assert.Equal("Number of students in affected cohort", question.SummaryTitle);
        Assert.False(question.Optional);
    }

    [Fact]
    public void The_cohort_count_validator_name_resolves_to_the_registered_validator()
    {
        var validator = new WholeNumberFormatValidator();

        Assert.Equal(validator.Name, Question("cohort-count", "q-cohort-count").Validator);
        // Required-vs-malformed must read the same, or the field appears to have two rules.
        Assert.Equal(validator.FailureMessage, Question("cohort-count", "q-cohort-count").ValidationFailure);
    }

    // ── Pupil search: both branches converge ─────────────────────────────────

    [Theory]
    [InlineData("select-student-cohort", "What is the name of one of the students from the affected cohort?")]
    [InlineData("select-student-single", "What is the name of the student with an incorrect grade?")]
    public void Both_student_search_pages_search_all_students_and_lead_to_the_result(string pageId, string title)
    {
        // "A pupil may be missing from the included data and still have a wrong grade", so both
        // populations are searchable — PupilFilter.All, not Included.
        var page = Page(pageId);

        Assert.Equal(PageType.PupilSearch, page.Type);
        Assert.Equal(title, page.Title);
        Assert.Equal(PupilFilter.All, page.PupilFilter);
        Assert.Equal(JourneyPage.PrimaryKey, page.PupilKey);
        Assert.Equal("Enter the name of the student with an incorrect grade", page.ValidationFailure);
        Assert.Equal("select-result", page.NextPageId);
    }

    [Theory]
    [InlineData("select-student-cohort")]
    [InlineData("select-student-single")]
    public void Both_student_search_pages_list_only_students_who_hold_results(string pageId)
    {
        // There is no grade to correct for a student with no result, so they are not a candidate.
        Assert.True(Page(pageId).RequireResults);
    }

    [Theory]
    [InlineData("select-student-cohort")]
    [InlineData("select-student-single")]
    public void Both_student_search_pages_say_why_a_student_may_be_missing(string pageId)
    {
        // The restriction above hides students. Without saying so, a school cannot tell a typo
        // from "this student has no results". FLAGGED: copy needs content sign-off.
        Assert.Equal(
            "Start typing to search by name, ULN, CYPMD ID or date of birth. "
            + "You can only search for students who have results.",
            Page(pageId).Subheading);
    }

    // ── Result search ────────────────────────────────────────────────────────

    [Fact]
    public void The_result_search_page_is_templated_on_the_selected_student()
    {
        var page = Page("select-result");

        Assert.Equal(PageType.ResultSearch, page.Type);
        Assert.Equal("Which of {pupilName}'s results is incorrect?", page.Title);
        Assert.Equal("Enter which of {pupilName}'s results is incorrect", page.ValidationFailure);
        Assert.Equal("grade-details", page.NextPageId);
    }

    [Fact]
    public void The_result_search_browser_title_does_not_leak_the_student_name()
    {
        // The <title> reaches analytics, so a student name in it is a data-protection problem.
        var page = Page("select-result");

        var browserTitle = page.PageTitle ?? JourneyTemplate.Strip(page.Title!);
        Assert.DoesNotContain("{pupilName}", browserTitle);
    }

    // ── Grade details ────────────────────────────────────────────────────────

    [Fact]
    public void The_grade_details_page_asks_for_the_revised_grade()
    {
        var page = Page("grade-details");
        var question = Question("grade-details", "q-revised-grade");

        Assert.Equal(PageType.ResultDetails, page.Type);
        Assert.Equal("Incorrect grade details", page.Title);
        Assert.Equal("additional-info", page.NextPageId);
        Assert.Equal(QuestionType.GradeSelect, question.Type);
        Assert.Equal("What should the revised grade be?", question.Title);
        Assert.Equal("Select the revised grade", question.ValidationFailure);
        Assert.False(question.Optional);
    }

    // ── Additional information ───────────────────────────────────────────────

    [Fact]
    public void Additional_information_is_optional_and_capped()
    {
        // "Most enquiries need no explanation. The field is there for the ones that do."
        var page = Page("additional-info");
        var question = Question("additional-info", "q-additional-info");

        Assert.Null(page.Title);
        Assert.Equal("Additional information", page.PageTitle);
        // "(Optional)" is appended by JourneyViewModelBuilder when Optional is true — putting it in
        // the title too rendered "Additional information (Optional) (Optional)".
        Assert.Equal("Additional information", question.Title);
        Assert.Null(page.NextPageId); // the last page — Continue goes to the summary
        Assert.Equal(QuestionType.TextArea, question.Type);
        Assert.True(question.Optional);
        Assert.Equal(250, question.CharacterLimit);
        Assert.Equal(
            "You can provide us with any additional information to support your query if you need to.",
            question.Hint);
        Assert.Equal("Additional information", question.SummaryTitle);
    }

    // ── Whole-flow invariants ────────────────────────────────────────────────

    [Fact]
    public void No_question_id_is_reused_across_the_flow()
    {
        // Answers are stored in one dictionary keyed by question id, so a duplicate silently
        // overwrites another page's answer.
        var ids = Flow.Pages.SelectMany(p => p.Questions).Select(q => q.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Only_the_additional_information_question_is_optional()
    {
        var optional = Flow.Pages.SelectMany(p => p.Questions).Where(q => q.Optional).Select(q => q.Id);

        Assert.Equal(["q-additional-info"], optional.ToArray());
    }

    [Fact]
    public void Every_question_has_validation_failure_copy_unless_it_is_optional()
    {
        // Without it the engine falls back to "{title} is required", which reads badly for a
        // question whose title is a full sentence.
        foreach (var question in Flow.Pages.SelectMany(p => p.Questions).Where(q => !q.Optional))
            Assert.False(string.IsNullOrWhiteSpace(question.ValidationFailure), $"{question.Id} has no validationFailure.");
    }

    [Fact]
    public void The_config_key_is_the_journey_and_window_type_this_flow_serves()
    {
        // QuestionFlowService resolves "{WhatToChange}_{CheckingWindowType}", so the file name is
        // the lookup key — renaming the file unbinds the journey.
        Assert.Equal(
            "IncorrectGrade_Post16",
            Path.GetFileNameWithoutExtension(LocateFlowFile()));
    }

    private static QuestionFlowConfig Load()
        => JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(LocateFlowFile()), JsonOptions)!;

    private static string LocateFlowFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", "IncorrectGrade_Post16.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate IncorrectGrade_Post16.json from " + AppContext.BaseDirectory);
    }
}
