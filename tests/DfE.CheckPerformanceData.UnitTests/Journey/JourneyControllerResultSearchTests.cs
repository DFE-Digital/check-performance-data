using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
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

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: the ResultSearch page — "Which of {pupil}'s results is incorrect?".
//
// The load-bearing behaviour is that the posted value is NOT trusted. The browser sends a composite
// key; the server re-resolves it against the results the selected pupil actually holds and rejects
// anything else as if nothing was chosen. Same fail-closed spirit as the hidden-radio-option
// rejection in PBI 292525.
public sealed class JourneyControllerResultSearchTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private const string Laestab = "860/4070";
    private const string CypmdId = "500001";
    private const string BusStudsKey = "6037116X|S2024|16to19_MAIN";
    private const string FrenchKey = "60181576|S2024|16to19_LR1";

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly IJourneyViewModelBuilder _vmBuilder = Substitute.For<IJourneyViewModelBuilder>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IOptionVisibilityService _optionVisibility = Substitute.For<IOptionVisibilityService>();
    private readonly IQuestionOptionalityService _optionality = Substitute.For<IQuestionOptionalityService>();
    private readonly IOriginCountryLanguageCapture _originCapture = Substitute.For<IOriginCountryLanguageCapture>();
    private readonly IStudentResultsClient _results = Substitute.For<IStudentResultsClient>();
    private readonly IGradeReferenceClient _gradeReference = Substitute.For<IGradeReferenceClient>();
    private readonly DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService _notifications =
        Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>();
    private readonly FakeSession _session = new();
    private readonly JourneyController _sut;

    private static readonly JourneyPage SelectResultPage = new()
    {
        Id = "select-result",
        Type = PageType.ResultSearch,
        Title = "Which of {pupilName}'s results is incorrect?",
        ValidationFailure = "Enter which of {pupilName}'s results is incorrect",
        NextPageId = "grade-details"
    };

    private static readonly JourneyPage GradeDetailsPage = new()
    {
        Id = "grade-details",
        Type = PageType.ResultDetails,
        Title = "Incorrect grade details"
    };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages = [SelectResultPage, GradeDetailsPage]
    };

    private static StudentResultRecord Result(string qan, string qualName, string session, string source, string grade) => new()
    {
        CypmdId = CypmdId, Qan = qan, QualificationName = qualName,
        SyllabusCode = "1BS0", Session = session, Grade = grade, SourceFile = source
    };

    private static readonly StudentResultRecord BusStuds =
        Result("6037116X", "GCSE (9-1) Bus. Studs:Single", "S2024", ResultsFileTags.Post16Main, "5");

    private static readonly StudentResultRecord French =
        Result("60181576", "GCSE (9-1) French", "S2024", ResultsFileTags.Post16LateResults1, "6");

    public JourneyControllerResultSearchTests()
    {
        _currentUser.OrganisationLaestab.Returns(Laestab);
        _currentUser.OrganisationUrn.Returns("142313");
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(Flow);
        _flowService.GetPage(Flow, "select-result").Returns(SelectResultPage);
        _flowService.GetPage(Flow, "grade-details").Returns(GradeDetailsPage);
        _flowService.GetNavigationGuard(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>(), Arg.Any<string>())
            .Returns((JourneyNavigation?)null);
        _results.GetResultsAsync(WindowId, Laestab, CypmdId, Arg.Any<CancellationToken>())
            .Returns([BusStuds, French]);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new JourneyController(
            _flowService, _journeyService, _fileStorage, _requestService, _pupilData, _vmBuilder,
            _analytics, _currentUser, _optionVisibility, _optionality, _originCapture, _results,
            _gradeReference, _notifications, NullLogger<JourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static PupilDto Pupil() => new()
    {
        Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M",
        DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = CypmdId, Identifier = "9900000001"
    };

    private RequestState ReadyJourney(Action<RequestState>? tweak = null)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.IncorrectGrade,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(),
            QuestionHistory = ["select-student-single", "select-result"]
        };
        tweak?.Invoke(state);
        _session.SetRequestState(WindowId, state);
        return state;
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Page_get_hands_a_result_search_page_to_its_own_route()
    {
        // The POST payload differs from a normal question page, so ResultSearch has its own
        // action — exactly as PupilSearch does.
        ReadyJourney();

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.Page(WindowId, "select-result"));

        Assert.Equal(nameof(JourneyController.ResultSearchPage), redirect.ActionName);
        Assert.Equal("select-result", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Get_renders_the_result_search_view()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(await _sut.ResultSearchPage(WindowId, "select-result"));

        Assert.Equal("ResultSearch", view.ViewName);
    }

    [Fact]
    public async Task Get_without_a_started_journey_goes_back_to_the_pupil_data_page()
    {
        _session.SetRequestState(WindowId, new RequestState());

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.ResultSearchPage(WindowId, "select-result"));

        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
    }

    [Fact]
    public async Task Get_for_a_page_that_is_not_a_result_search_is_not_found()
    {
        ReadyJourney();

        Assert.IsType<NotFoundResult>(await _sut.ResultSearchPage(WindowId, "grade-details"));
    }

    [Fact]
    public async Task Get_honours_an_out_of_sequence_navigation_guard()
    {
        // Deep-linking to the result page before choosing a pupil must bounce, like every other page.
        ReadyJourney();
        _flowService.GetNavigationGuard(Flow, Arg.Any<RequestState>(), "select-result")
            .Returns(new RedirectToJourneyPage("select-student-single"));

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.ResultSearchPage(WindowId, "select-result"));

        Assert.Equal("select-student-single", redirect.RouteValues!["pageId"]);
    }

    // ── POST: happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Post_stores_the_result_the_server_resolved_not_the_one_the_browser_described()
    {
        ReadyJourney();

        await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey);

        var stored = _session.GetRequestState(WindowId).SelectedResult;
        Assert.NotNull(stored);
        Assert.Equal("6037116X", stored.Qan);
        Assert.Equal("S2024", stored.Session);
        Assert.Equal("5", stored.Grade);
        Assert.Equal("GCSE (9-1) Bus. Studs:Single", stored.QualificationName);
    }

    [Fact]
    public async Task Post_continues_to_the_next_page()
    {
        ReadyJourney();

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("grade-details", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Post_scopes_the_lookup_to_the_session_pupil_and_the_signed_in_school()
    {
        ReadyJourney();

        await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey);

        await _results.Received(1).GetResultsAsync(WindowId, Laestab, CypmdId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_records_the_page_in_the_history()
    {
        ReadyJourney(s => s.QuestionHistory = ["select-student-single"]);

        await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey);

        Assert.Contains("select-result", _session.GetRequestState(WindowId).QuestionHistory);
    }

    // ── POST: fail closed ────────────────────────────────────────────────────

    [Fact]
    public async Task Post_with_no_selection_shows_the_templated_validation_message()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(
            await _sut.ResultSearchPost(WindowId, "select-result", null));

        Assert.Equal("ResultSearch", view.ViewName);
        Assert.Equal(
            "Enter which of Billy B's results is incorrect",
            _sut.ModelState["selectedResultKey"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_with_no_selection_reports_a_coded_validation_error()
    {
        ReadyJourney();

        await _sut.ResultSearchPost(WindowId, "select-result", "");

        await _analytics.Received(1).TrackSafeAsync(Arg.Is<ValidationErrorEvent>(e =>
            e.ErrorCodes.Contains("no_selection") && e.ErrorFields.Contains("selectedResultKey")));
    }

    [Fact]
    public async Task Post_with_a_key_the_pupil_does_not_hold_is_treated_as_unanswered()
    {
        // A forged or stale key must not put a result into the enquiry that this pupil never sat.
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(
            await _sut.ResultSearchPost(WindowId, "select-result", "99999999|S2024|16to19_MAIN"));

        Assert.Equal("ResultSearch", view.ViewName);
        Assert.Null(_session.GetRequestState(WindowId).SelectedResult);
        Assert.Equal(
            "Enter which of Billy B's results is incorrect",
            _sut.ModelState["selectedResultKey"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_with_a_key_belonging_to_a_different_pupil_is_rejected()
    {
        // The lookup is already pupil-scoped, so a key from another pupil's list simply will not
        // resolve. Pinned because it is the property that makes the scoping meaningful.
        ReadyJourney();
        _results.GetResultsAsync(WindowId, Laestab, CypmdId, Arg.Any<CancellationToken>()).Returns([French]);

        Assert.IsType<ViewResult>(await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey));
        Assert.Null(_session.GetRequestState(WindowId).SelectedResult);
    }

    [Fact]
    public async Task Post_without_a_pupil_in_session_is_treated_as_unanswered()
    {
        ReadyJourney(s => { s.SelectedPupil = null; s.SelectedPupilId = null; });

        Assert.IsType<ViewResult>(await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey));
        await _results.DidNotReceiveWithAnyArgs().GetResultsAsync(default, default!, default!);
    }

    [Fact]
    public async Task Post_does_not_navigate_when_the_selection_is_rejected()
    {
        ReadyJourney();

        var result = await _sut.ResultSearchPost(WindowId, "select-result", "bogus");

        Assert.IsNotType<RedirectToActionResult>(result);
    }

    // ── POST: changing the result clears the grade ───────────────────────────

    [Fact]
    public async Task Choosing_a_different_result_clears_a_previously_chosen_revised_grade()
    {
        // A revised grade belongs to one result. Carrying "5" over from the Business Studies result
        // to the French one would submit a grade the user never chose for that qualification.
        ReadyJourney(s =>
        {
            s.SelectedResult = BusStuds;
            s.QuestionAnswers["q-revised-grade"] = new QuestionAnswer { TextValue = "7" };
        });

        await _sut.ResultSearchPost(WindowId, "select-result", FrenchKey);

        var state = _session.GetRequestState(WindowId);
        Assert.Equal("60181576", state.SelectedResult!.Qan);
        Assert.DoesNotContain("q-revised-grade", state.QuestionAnswers.Keys);
    }

    [Fact]
    public async Task Reselecting_the_same_result_keeps_the_revised_grade()
    {
        // Coming back through the page and confirming the same result is not a change, so retyping
        // the grade would be busywork.
        ReadyJourney(s =>
        {
            s.SelectedResult = BusStuds;
            s.QuestionAnswers["q-revised-grade"] = new QuestionAnswer { TextValue = "7" };
        });

        await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey);

        Assert.Equal("7", _session.GetRequestState(WindowId).QuestionAnswers["q-revised-grade"].TextValue);
    }

    [Fact]
    public async Task Choosing_a_different_result_leaves_other_answers_alone()
    {
        // Only the grade is tied to the result. The cohort answers are not.
        ReadyJourney(s =>
        {
            s.SelectedResult = BusStuds;
            s.QuestionAnswers["q-cohort-scope"] = new QuestionAnswer { TextValue = "yes" };
            s.QuestionAnswers["q-cohort-count"] = new QuestionAnswer { TextValue = "10" };
            s.QuestionAnswers["q-revised-grade"] = new QuestionAnswer { TextValue = "7" };
        });

        await _sut.ResultSearchPost(WindowId, "select-result", FrenchKey);

        var answers = _session.GetRequestState(WindowId).QuestionAnswers;
        Assert.Equal("yes", answers["q-cohort-scope"].TextValue);
        Assert.Equal("10", answers["q-cohort-count"].TextValue);
    }

    [Fact]
    public async Task Post_for_a_page_that_is_not_a_result_search_is_not_found()
    {
        ReadyJourney();

        Assert.IsType<NotFoundResult>(await _sut.ResultSearchPost(WindowId, "grade-details", BusStudsKey));
    }

    [Fact]
    public async Task Post_with_no_next_page_goes_to_the_summary()
    {
        var lastPage = new JourneyPage
        {
            Id = "select-result",
            Type = PageType.ResultSearch,
            Title = "Which of {pupilName}'s results is incorrect?",
            NextPageId = null
        };
        var flow = new QuestionFlowConfig { FirstPageId = "select-result", Pages = [lastPage] };
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        _flowService.GetPage(flow, "select-result").Returns(lastPage);
        ReadyJourney();

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _sut.ResultSearchPost(WindowId, "select-result", BusStudsKey));

        Assert.Equal(nameof(JourneyController.Summary), redirect.ActionName);
    }

    // ── Choosing a pupil must not discard the cohort answers ─────────────────

    [Fact]
    public async Task Choosing_the_student_keeps_answers_given_before_that_page()
    {
        // On the amendment journeys the pupil page is first, so selecting a pupil discarded every
        // answer. The enquiry journey asks about the cohort BEFORE the student, and those answers are
        // not about the student — wiping them lost the scope and count, and the summary then silently
        // presented a cohort-wide enquiry as a single-pupil one. Caught by walking the journey; no
        // unit test existed that could have.
        var cohortScope = new JourneyPage
        {
            Id = "cohort-scope",
            Questions =
            [
                new Question { Id = "q-cohort-scope", Type = QuestionType.Radio, Title = "Whole cohort?" }
            ]
        };
        var cohortCount = new JourneyPage
        {
            Id = "cohort-count",
            Questions =
            [
                new Question { Id = "q-cohort-count", Type = QuestionType.FreeText, Title = "How many?" }
            ]
        };
        var studentPage = new JourneyPage
        {
            Id = "select-student-cohort",
            Type = PageType.PupilSearch,
            PupilKey = JourneyPage.PrimaryKey,
            PupilFilter = PupilFilter.All,
            NextPageId = "select-result"
        };
        var flow = new QuestionFlowConfig
        {
            FirstPageId = "cohort-scope",
            Pages = [cohortScope, cohortCount, studentPage, SelectResultPage, GradeDetailsPage]
        };
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        foreach (var page in flow.Pages)
            _flowService.GetPage(flow, page.Id).Returns(page);

        var pupil = Pupil();
        _pupilData.GetPupilAsync(WindowId, pupil.Id).Returns(pupil);
        _requestService.HasSubmittedRequestAsync(WindowId, pupil.Id, Arg.Any<long>())
            .Returns(new DuplicateCheckResult.NoConflict());
        _journeyService.GenerateEnquiryReference().Returns("CYPMD_16to19_RE_ABCDEF1");

        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.IncorrectGrade,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            QuestionAnswers = new Dictionary<string, QuestionAnswer>
            {
                ["q-cohort-scope"] = new() { TextValue = "yes" },
                ["q-cohort-count"] = new() { TextValue = "10" }
            },
            QuestionHistory = ["cohort-scope", "cohort-count"]
        });

        await _sut.PupilSearchPost(WindowId, "select-student-cohort", pupil.Id.ToString(), "Alice, Smith");

        var state = _session.GetRequestState(WindowId);
        Assert.Equal("yes", state.QuestionAnswers["q-cohort-scope"].TextValue);
        Assert.Equal("10", state.QuestionAnswers["q-cohort-count"].TextValue);
        Assert.Equal(["cohort-scope", "cohort-count", "select-student-cohort"], state.QuestionHistory);
        Assert.Equal("CYPMD_16to19_RE_ABCDEF1", state.ReferenceNumber);
    }

    [Fact]
    public async Task Choosing_a_student_discards_answers_given_after_that_page()
    {
        // The behaviour the wipe existed for: a grade chosen for the previous student's result must
        // not survive, and neither must the result itself.
        var studentPage = new JourneyPage
        {
            Id = "select-student-single",
            Type = PageType.PupilSearch,
            PupilKey = JourneyPage.PrimaryKey,
            PupilFilter = PupilFilter.All,
            NextPageId = "select-result"
        };
        var flow = new QuestionFlowConfig
        {
            FirstPageId = "select-student-single",
            Pages = [studentPage, SelectResultPage, GradeDetailsPage]
        };
        _flowService.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(flow);
        foreach (var page in flow.Pages)
            _flowService.GetPage(flow, page.Id).Returns(page);

        var pupil = Pupil();
        _pupilData.GetPupilAsync(WindowId, pupil.Id).Returns(pupil);
        _requestService.HasSubmittedRequestAsync(WindowId, pupil.Id, Arg.Any<long>())
            .Returns(new DuplicateCheckResult.NoConflict());

        ReadyJourney(s =>
        {
            s.QuestionHistory = ["select-student-single", "select-result", "grade-details"];
            s.SelectedResult = BusStuds;
            s.QuestionAnswers["q-revised-grade"] = new QuestionAnswer { TextValue = "7" };
        });

        await _sut.PupilSearchPost(WindowId, "select-student-single", pupil.Id.ToString(), "Alice, Smith");

        var state = _session.GetRequestState(WindowId);
        Assert.Empty(state.QuestionAnswers);
        Assert.Null(state.SelectedResult);
        Assert.Equal(["select-student-single"], state.QuestionHistory);
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
