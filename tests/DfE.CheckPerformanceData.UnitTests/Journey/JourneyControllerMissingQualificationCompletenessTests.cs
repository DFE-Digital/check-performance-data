using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Issue #653 / AB#309176: the summary's completeness re-check (GET and the POST that re-runs it
// before persisting) required a syllabus-code answer on every missing-qualification enquiry, even
// though #577 hides that question for qualifications with no syllabus codes — so Continue looped
// back to the details page forever. These pin the fix: the syllabus answer is owed only when the
// selected qualification has codes to pick, and the qualification/missing-grade prerequisites and
// the coded-qualification behaviour are unchanged.
//
// The real OptionVisibilityService and condition sit in the controller and the view-model builder
// so the JSON visibleWhen rule and the completeness rule are exercised together, as shipped.
public sealed class JourneyControllerMissingQualificationCompletenessTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F99");
    private const string CypmdId = "500001";

    // The common case: only 13 of the 974 QualList QANs carry any syllabus codes, so most
    // enquiries pick a qualification with none.
    private static readonly QualificationReference CodeLess = new()
    {
        Qan = "60146085",
        QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Geography",
        AwardingOrganisation = "AQA",
        Grades = ["1", "2", "3"],
        SyllabusCodes = []
    };

    private static readonly QualificationReference Coded = new()
    {
        Qan = "60146084",
        QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
        AwardingOrganisation = "AQA",
        Grades = ["1", "2", "3"],
        SyllabusCodes = [new SyllabusCode { Code = "8300H", Title = "Mathematics Higher Tier" }]
    };

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService =
        new JourneyValidationService([new DfE.CheckPerformanceData.Application.Journey.Validators.NcnValidator()]);
    private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly JourneyViewModelBuilder _vmBuilder;
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IOptionVisibilityService _optionVisibility =
        new OptionVisibilityService([new HasSyllabusCodesForSelectedQualificationCondition()]);
    private readonly IQuestionOptionalityService _optionality = Substitute.For<IQuestionOptionalityService>();
    private readonly IOriginCountryLanguageCapture _originCapture = Substitute.For<IOriginCountryLanguageCapture>();
    private readonly IResultsClient _results = Substitute.For<IResultsClient>();
    private readonly IQualificationReferenceClient _qualificationReference = Substitute.For<IQualificationReferenceClient>();
    private readonly DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService _notifications =
        Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>();
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly JourneyController _sut;

    // The tail of MissingQualification_Post16.json: the two pages the completeness check can send
    // the user back to, plus the optional free-text page that ends the journey.
    private static readonly JourneyPage QualificationSearchPage = new()
    {
        Id = "select-qualification",
        Type = PageType.QualificationSearch,
        Title = "Provide the missing qualification details for {pupilName}",
        NextPageId = "qualification-details"
    };

    private static readonly JourneyPage DetailsPage = new()
    {
        Id = "qualification-details",
        Type = PageType.QualificationDetails,
        Title = "Provide the missing qualification details for {pupilName}",
        NextPageId = "additional-info",
        Questions =
        [
            // #577: shown only when the selected qualification has codes — the same rule the fix
            // mirrors, read from the config rather than restated.
            new Question
            {
                Id = "q-syllabus-code",
                Type = QuestionType.SyllabusSelect,
                Title = "Select syllabus code",
                ValidationFailure = "Select the syllabus code",
                VisibleWhen = ["HasSyllabusCodesForSelectedQualification"]
            },
            new Question { Id = "q-award-date", Type = QuestionType.Date, Title = "Provide award date",
                ValidationFailure = "Provide the award date" },
            new Question { Id = "q-missing-grade", Type = QuestionType.GradeSelect, Title = "Select the missing grade {pupilName} achieved",
                ValidationFailure = "Select the missing grade this student achieved" },
            new Question { Id = "q-ncn", Type = QuestionType.FreeText, Title = "Provide National Centre Number (NCN) where exam was taken",
                Optional = true, Validator = "Ncn" }
        ]
    };

    private static readonly JourneyPage AdditionalInfoPage = new()
    {
        Id = "additional-info",
        Type = PageType.Question,
        Title = "Provide any additional information",
        Questions = [new Question { Id = "q-additional-info", Type = QuestionType.TextArea, Title = "Additional information", Optional = true }]
    };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages = [QualificationSearchPage, DetailsPage, AdditionalInfoPage]
    };

    public JourneyControllerMissingQualificationCompletenessTests()
    {
        _currentUser.OrganisationLaestab.Returns("860/4070");
        _flowService.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16).Returns(Flow);
        _flowService.GetPage(Flow, "select-qualification").Returns(QualificationSearchPage);
        _flowService.GetPage(Flow, "qualification-details").Returns(DetailsPage);
        _flowService.GetPage(Flow, "additional-info").Returns(AdditionalInfoPage);
        // The last page of history has no successor — the real flow engine answers null. Stubbed
        // explicitly: left to NSubstitute's default the value is not the null the Summary GET
        // expects, and the journey is sent to a blank Page redirect instead of reaching the
        // completeness check.
        _flowService.GetNextPageId(Flow, "additional-info", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        _optionality.GetConditionallyOptionalQuestionIds(Arg.Any<JourneyPage>(), Arg.Any<JourneyConditionContext>())
            .Returns(new HashSet<string>());

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _vmBuilder = new JourneyViewModelBuilder(_flowService, _journeyService, _optionVisibility, _currentUser, _optionality);

        _sut = new JourneyController(
            _flowService, _journeyService, _fileStorage, _requestService, _pupilData, _vmBuilder,
            _analytics, _currentUser, _optionVisibility, _optionality, _originCapture, _results,
            _qualificationReference, _notifications, OpenCheckingExercises.AlwaysOpen(),
            NullLogger<JourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext }
        };
    }

    private static PupilDto Pupil() => new()
    {
        Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M",
        DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = CypmdId, Identifier = "9900000001"
    };

    // A completed journey in session: every page visited and the last one (additional-info)
    // answered or optional, so the only thing that can still hold the summary is an enquiry gap.
    private RequestState ReadyJourney(Action<RequestState>? tweak = null)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.MissingQualification,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(),
            QuestionHistory = ["select-student-single", "select-qualification", "qualification-details", "additional-info"]
        };
        tweak?.Invoke(state);
        _session.SetRequestState(WindowId, state);
        return state;
    }

    private static QuestionAnswer AwardDate() => new() { DateValue = new DateAnswer { Day = 5, Month = 6, Year = 2024 } };
    private static QuestionAnswer Grade(string value) => new() { TextValue = value };

    // ── US1: a qualification with no syllabus codes ───────────────────────────

    [Fact]
    public async Task Summary_get_reaches_the_summary_when_the_qualification_has_no_syllabus_codes()
    {
        // The defect: with no codes the question is hidden, the answer can never exist, and the
        // old rule sent the user back to the details page on every Continue.
        ReadyJourney(s =>
        {
            s.SelectedQualification = CodeLess;
            s.QuestionAnswers["q-award-date"] = AwardDate();
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });

        var model = Assert.IsType<SummaryViewModel>(Assert.IsType<ViewResult>(await _sut.Summary(WindowId)).Model);

        Assert.False(model.MissingQualification!.ShowSyllabusCode);
        Assert.DoesNotContain(model.Lines, line => line.Key == "Syllabus code");
        Assert.Equal("3", model.MissingQualification.GradeAchieved);
    }

    [Fact]
    public async Task Summary_get_reaches_the_summary_when_a_stale_syllabus_answer_survives_on_a_codeless_qualification()
    {
        // Choosing another qualification clears the syllabus and grade answers in production; a
        // stale pair that slips through must neither block the summary nor show in it.
        ReadyJourney(s =>
        {
            s.SelectedQualification = CodeLess;
            s.QuestionAnswers["q-syllabus-code"] = Grade("8300H");
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });

        var model = Assert.IsType<SummaryViewModel>(Assert.IsType<ViewResult>(await _sut.Summary(WindowId)).Model);

        Assert.False(model.MissingQualification!.ShowSyllabusCode);
        Assert.DoesNotContain(model.Lines, line => line.Key == "Syllabus code");
    }

    [Fact]
    public async Task Summary_get_is_held_on_the_details_page_when_the_missing_grade_is_unanswered()
    {
        // The grade prerequisite is unchanged by the fix, code-less or not.
        ReadyJourney(s =>
        {
            s.SelectedQualification = CodeLess;
            s.QuestionAnswers["q-award-date"] = AwardDate();
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Summary(WindowId));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Summary_get_sends_to_the_qualification_search_when_no_qualification_is_chosen()
    {
        // The qualification prerequisite dominates even when a grade answer is present.
        ReadyJourney(s =>
        {
            s.SelectedQualification = null;
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Summary(WindowId));

        Assert.Equal(nameof(JourneyController.QualificationSearchPage), redirect.ActionName);
        Assert.Equal("select-qualification", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Summary_confirm_submits_when_the_qualification_has_no_syllabus_codes()
    {
        // The POST re-runs the same check before persisting and emailing; the old rule refused
        // this submission exactly as the GET looped.
        ReadyJourney(s =>
        {
            s.SelectedQualification = CodeLess;
            s.QuestionAnswers["q-award-date"] = AwardDate();
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });
        _requestService.SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>())
            .Returns("CYPMD_16to19_MQ_ABC1234");

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.EnquiryConfirmation), redirect.ActionName);
        await _requestService.Received(1)
            .SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Summary_confirm_is_held_on_the_details_page_when_the_missing_grade_is_unanswered()
    {
        ReadyJourney(s =>
        {
            s.SelectedQualification = CodeLess;
            s.QuestionAnswers["q-award-date"] = AwardDate();
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
        await _requestService.DidNotReceiveWithAnyArgs()
            .SubmitResultsEnquiryAsync(default, default!, default);
    }

    // ── US2: a qualification with syllabus codes — unchanged behaviour ───────

    [Fact]
    public async Task Summary_get_is_held_on_the_details_page_when_the_syllabus_is_unanswered_and_the_qualification_has_codes()
    {
        // The fix must not loosen the rule for coded qualifications: the question is visible, so
        // its answer is still owed.
        ReadyJourney(s =>
        {
            s.SelectedQualification = Coded;
            s.QuestionAnswers["q-award-date"] = AwardDate();
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Summary(WindowId));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Summary_get_reaches_the_summary_when_the_syllabus_is_answered_and_the_qualification_has_codes()
    {
        ReadyJourney(s =>
        {
            s.SelectedQualification = Coded;
            s.QuestionAnswers["q-syllabus-code"] = Grade("8300H");
            s.QuestionAnswers["q-award-date"] = AwardDate();
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });

        var model = Assert.IsType<SummaryViewModel>(Assert.IsType<ViewResult>(await _sut.Summary(WindowId)).Model);

        Assert.True(model.MissingQualification!.ShowSyllabusCode);
        Assert.Contains(model.Lines, line => line.Key == "Syllabus code" && line.Value == "8300H");
    }

    [Fact]
    public async Task Summary_confirm_is_held_on_the_details_page_when_the_syllabus_is_unanswered_and_the_qualification_has_codes()
    {
        ReadyJourney(s =>
        {
            s.SelectedQualification = Coded;
            s.QuestionAnswers["q-award-date"] = AwardDate();
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
        await _requestService.DidNotReceiveWithAnyArgs()
            .SubmitResultsEnquiryAsync(default, default!, default);
    }

    [Fact]
    public async Task Summary_confirm_submits_when_the_syllabus_is_answered_and_the_qualification_has_codes()
    {
        ReadyJourney(s =>
        {
            s.SelectedQualification = Coded;
            s.QuestionAnswers["q-syllabus-code"] = Grade("8300H");
            s.QuestionAnswers["q-award-date"] = AwardDate();
            s.QuestionAnswers["q-missing-grade"] = Grade("3");
        });
        _requestService.SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>())
            .Returns("CYPMD_16to19_MQ_ABC1234");

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.EnquiryConfirmation), redirect.ActionName);
        await _requestService.Received(1)
            .SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>());
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
        public void Set(string key, byte[] value) => _store[key] = value;
        public void Remove(string key) => _store.Remove(key);
        public void Clear() => _store.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
    }

    private sealed class TestSessionFeature(ISession session) : ISessionFeature
    {
        public ISession Session { get; set; } = session;
    }
}
