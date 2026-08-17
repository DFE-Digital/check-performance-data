using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: the check-answers summary for a results enquiry.
//
// The row set is completely unlike an amendment's — it leads with the establishment and enquiry type
// rather than "what pupil data would you like to change?" — so the summary branches on the journey.
// Row ORDER is pinned because it is what the user reads before confirming, and the AC is that they
// see everything they entered.
public sealed class JourneyViewModelBuilderEnquirySummaryTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly JourneyViewModelBuilder _sut;

    private static readonly Question CohortScope = new()
    {
        Id = "q-cohort-scope", Type = QuestionType.Radio, Title = "Does the incorrect grade affect the whole cohort?",
        SummaryTitle = "Affects the whole cohort",
        Options = [new QuestionOption { Value = "yes", Label = "Yes" }, new QuestionOption { Value = "no", Label = "No" }]
    };

    private static readonly Question CohortCount = new()
    {
        Id = "q-cohort-count", Type = QuestionType.FreeText,
        Title = "Enter the number of students who have an incorrect grade for this qualification",
        SummaryTitle = "Number of students in affected cohort"
    };

    private static readonly Question RevisedGrade = new()
    {
        Id = "q-revised-grade", Type = QuestionType.GradeSelect,
        Title = "What should the revised grade be?", SummaryTitle = "Revised grade"
    };

    private static readonly Question AdditionalInfo = new()
    {
        Id = "q-additional-info", Type = QuestionType.TextArea, Optional = true,
        Title = "Additional information (Optional)", SummaryTitle = "Additional information"
    };

    private static readonly JourneyPage CohortScopePage = new() { Id = "cohort-scope", Questions = [CohortScope] };
    private static readonly JourneyPage CohortCountPage = new() { Id = "cohort-count", Questions = [CohortCount] };
    private static readonly JourneyPage StudentCohortPage = new() { Id = "select-student-cohort", Type = PageType.PupilSearch, PupilKey = "primary" };
    private static readonly JourneyPage StudentSinglePage = new() { Id = "select-student-single", Type = PageType.PupilSearch, PupilKey = "primary" };
    private static readonly JourneyPage SelectResultPage = new() { Id = "select-result", Type = PageType.ResultSearch };
    private static readonly JourneyPage GradeDetailsPage = new() { Id = "grade-details", Type = PageType.ResultDetails, Questions = [RevisedGrade] };
    private static readonly JourneyPage AdditionalInfoPage = new() { Id = "additional-info", Questions = [AdditionalInfo] };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages =
        [
            CohortScopePage, CohortCountPage, StudentCohortPage, StudentSinglePage,
            SelectResultPage, GradeDetailsPage, AdditionalInfoPage
        ]
    };

    public JourneyViewModelBuilderEnquirySummaryTests()
    {
        _currentUser.OrganisationLaestab.Returns("860/4070");
        foreach (var page in Flow.Pages)
            _flowService.GetPage(Flow, page.Id).Returns(page);

        _sut = new JourneyViewModelBuilder(
            _flowService, new JourneyValidationService(),
            Substitute.For<IOptionVisibilityService>(), _currentUser);
    }

    // The Figma fixture from the plan.
    private static readonly StudentResultRecord ArtAndDesign = new()
    {
        CypmdId = "1596410810", Qan = "60180882", QualificationName = "GCSE (9-1) Art&Des : Fine Art",
        SyllabusCode = "1AD0", Session = "S2024", Grade = "9", SourceFile = ResultsFileTags.Post16Main
    };

    private static PupilDto Billy() => new()
    {
        Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M",
        DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = "1596410810", Identifier = "9900000001"
    };

    private RequestState CohortJourney() => new()
    {
        SelectedWhatToChange = WhatToChange.IncorrectGrade,
        CheckingWindow = Window,
        SelectedPupilId = Guid.NewGuid().ToString(),
        SelectedPupil = Billy(),
        SelectedResult = ArtAndDesign,
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["q-cohort-scope"] = new() { TextValue = "yes" },
            ["q-cohort-count"] = new() { TextValue = "10" },
            ["q-revised-grade"] = new() { TextValue = "U" },
            ["q-additional-info"] = new() { TextValue = "The whole class was marked against the wrong paper." }
        },
        QuestionHistory =
        [
            "cohort-scope", "cohort-count", "select-student-cohort", "select-result",
            "grade-details", "additional-info"
        ]
    };

    private RequestState SingleJourney() => new()
    {
        SelectedWhatToChange = WhatToChange.IncorrectGrade,
        CheckingWindow = Window,
        SelectedPupilId = Guid.NewGuid().ToString(),
        SelectedPupil = Billy(),
        SelectedResult = ArtAndDesign,
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["q-cohort-scope"] = new() { TextValue = "no" },
            ["q-revised-grade"] = new() { TextValue = "U" }
        },
        QuestionHistory =
        [
            "cohort-scope", "select-student-single", "select-result", "grade-details", "additional-info"
        ]
    };

    private static CheckingWindowDto Window => new()
    {
        Title = "16 to 19 2026", KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
    };

    private IReadOnlyList<SummaryLine> Lines(RequestState journey) =>
        _sut.BuildSummaryVm(WindowId, journey, Flow).Lines;

    // ── The cohort branch ────────────────────────────────────────────────────

    [Fact]
    public void The_cohort_branch_shows_every_row_in_the_pinned_order()
    {
        var lines = Lines(CohortJourney());

        Assert.Equal(
            [
                "DfE number",
                "Key stage",
                "Enquiry type",
                "Number of students in affected cohort",
                "Name of a student in cohort",
                "CYPMD ID",
                "Qualification number (QAN)",
                "Qualification name and subject",
                "Session",
                "Current grade",
                "Revised grade",
                "Additional information"
            ],
            lines.Select(l => l.Key).ToArray());
    }

    [Fact]
    public void The_cohort_branch_shows_the_expected_values()
    {
        var lines = Lines(CohortJourney()).ToDictionary(l => l.Key, l => l.Value);

        Assert.Equal("860/4070", lines["DfE number"]);
        Assert.Equal("16 to 19", lines["Key stage"]);
        Assert.Equal("Incorrect grade", lines["Enquiry type"]);
        Assert.Equal("10", lines["Number of students in affected cohort"]);
        Assert.Equal("Billy B", lines["Name of a student in cohort"]);
        Assert.Equal("1596410810", lines["CYPMD ID"]);
        Assert.Equal("60180882", lines["Qualification number (QAN)"]);
        Assert.Equal("GCSE (9-1) Art&Des : Fine Art", lines["Qualification name and subject"]);
        Assert.Equal("S2024", lines["Session"]);
        Assert.Equal("9", lines["Current grade"]);
        Assert.Equal("U", lines["Revised grade"]);
        Assert.Equal("The whole class was marked against the wrong paper.", lines["Additional information"]);
    }

    // ── The single-pupil branch ──────────────────────────────────────────────

    [Fact]
    public void The_single_pupil_branch_omits_the_cohort_count_row()
    {
        var lines = Lines(SingleJourney());

        Assert.DoesNotContain("Number of students in affected cohort", lines.Select(l => l.Key));
    }

    [Fact]
    public void The_single_pupil_branch_labels_the_student_row_differently()
    {
        // The label is how the summary conveys the cohort answer, since the yes/no is not a row of
        // its own.
        var lines = Lines(SingleJourney()).ToDictionary(l => l.Key, l => l.Value);

        Assert.Equal("Billy B", lines["Name of student"]);
        Assert.DoesNotContain("Name of a student in cohort", lines.Keys);
    }

    [Fact]
    public void The_single_pupil_branch_keeps_every_other_row_in_order()
    {
        var lines = Lines(SingleJourney());

        Assert.Equal(
            [
                "DfE number", "Key stage", "Enquiry type", "Name of student", "CYPMD ID",
                "Qualification number (QAN)", "Qualification name and subject", "Session",
                "Current grade", "Revised grade", "Additional information"
            ],
            lines.Select(l => l.Key).ToArray());
    }

    [Fact]
    public void An_unanswered_additional_information_row_is_still_shown_as_empty()
    {
        // "Given I have no comments to add, when I continue, then I proceed without entering any" —
        // the row stays so the user can see there is nothing there, and can add something.
        var lines = Lines(SingleJourney()).ToDictionary(l => l.Key, l => l.Value);

        Assert.Equal(string.Empty, lines["Additional information"]);
    }

    // ── Change links ─────────────────────────────────────────────────────────

    [Fact]
    public void Only_the_revised_grade_and_additional_information_can_be_changed()
    {
        // Figma-pinned: identity and result rows have no change link. Changing the student or the
        // result means going back through the journey, because a different result invalidates the
        // grade that was chosen for it.
        var changeable = Lines(CohortJourney()).Where(l => l.HasChange).Select(l => l.Key).ToArray();

        Assert.Equal(["Revised grade", "Additional information"], changeable);
    }

    [Fact]
    public void The_change_links_target_the_pages_that_ask_those_questions()
    {
        var lines = Lines(CohortJourney()).ToDictionary(l => l.Key, l => l);

        Assert.Equal("grade-details", lines["Revised grade"].ChangePageId);
        Assert.True(lines["Revised grade"].IsPageChange);
        Assert.Equal("additional-info", lines["Additional information"].ChangePageId);
        Assert.True(lines["Additional information"].IsPageChange);
    }

    [Fact]
    public void The_change_links_carry_visually_hidden_text()
    {
        // Two links both reading "Change" are indistinguishable to a screen reader without it.
        foreach (var line in Lines(CohortJourney()).Where(l => l.HasChange))
            Assert.False(string.IsNullOrWhiteSpace(line.ChangeHiddenText));
    }

    // ── The amendment summary is untouched ───────────────────────────────────

    [Fact]
    public void An_amendment_journey_still_gets_the_amendment_summary()
    {
        // The live KS4 journeys must be unaffected by this ticket.
        var removal = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = Window,
            SelectedPupil = Billy(),
            QuestionAnswers = new Dictionary<string, QuestionAnswer> { ["q-cohort-scope"] = new() { TextValue = "yes" } },
            QuestionHistory = ["cohort-scope"]
        };

        var lines = _sut.BuildSummaryVm(WindowId, removal, Flow).Lines;

        Assert.Equal("What pupil data would you like to change?", lines[0].Key);
        Assert.DoesNotContain("DfE number", lines.Select(l => l.Key));
    }

    // ── Heading data ─────────────────────────────────────────────────────────

    [Fact]
    public void The_view_model_exposes_the_enquiry_shape_for_the_heading_and_actions()
    {
        var vm = _sut.BuildSummaryVm(WindowId, CohortJourney(), Flow);

        Assert.True(vm.IsResultsEnquiry);
        Assert.Equal("Billy B", vm.PupilName);
    }

    [Fact]
    public void An_amendment_is_not_flagged_as_an_enquiry()
    {
        var removal = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = Window,
            SelectedPupil = Billy(),
            QuestionHistory = ["cohort-scope"]
        };

        Assert.False(_sut.BuildSummaryVm(WindowId, removal, Flow).IsResultsEnquiry);
    }
}
