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
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#297848: the QualificationSearch page — "Provide the missing qualification details for
// {pupilName}". Mirrors JourneyControllerResultSearchTests: the posted AO/QAN pair is NOT trusted,
// the server re-resolves it against the reference lookup and rejects anything that doesn't resolve
// (or whose AO doesn't match) as if nothing was chosen.
public sealed class JourneyControllerQualificationSearchTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F02");
    private const string CypmdId = "500001";

    private const string Sample = """
        {
          "60146084": { "qan": "60146084", "qualificationTitle": "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
                        "awardingOrganisation": "AQA", "grades": ["1","2","3"],
                        "syllabusCodes": [ { "code": "8300F", "title": "Mathematics Foundation Tier" },
                                           { "code": "8300H", "title": "Mathematics Higher Tier" } ] },
          "6016041X": { "qan": "6016041X", "qualificationTitle": "Active IQ Level 2 Diploma",
                        "awardingOrganisation": "Active IQ", "grades": ["D","M","P"], "syllabusCodes": [] }
        }
        """;

    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly ICheckYourPupilDataService _pupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly JourneyViewModelBuilder _vmBuilder;
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IOptionVisibilityService _optionVisibility = Substitute.For<IOptionVisibilityService>();
    private readonly IQuestionOptionalityService _optionality = Substitute.For<IQuestionOptionalityService>();
    private readonly IOriginCountryLanguageCapture _originCapture = Substitute.For<IOriginCountryLanguageCapture>();
    private readonly IStudentResultsClient _results = Substitute.For<IStudentResultsClient>();
    private readonly IGradeReferenceClient _gradeReference = Substitute.For<IGradeReferenceClient>();
    private readonly IQualificationReferenceClient _qualificationReference = Substitute.For<IQualificationReferenceClient>();
    private readonly DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService _notifications =
        Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>();
    private readonly FakeSession _session = new();
    private readonly JourneyController _sut;

    private static readonly JourneyPage SelectQualificationPage = new()
    {
        Id = "select-qualification",
        Type = PageType.QualificationSearch,
        Title = "Provide the missing qualification details for {pupilName}",
        ValidationFailure = "Select the Qualification Number (QAN)",
        NextPageId = "qualification-details"
    };

    private static readonly JourneyPage DetailsPage = new()
    {
        Id = "qualification-details",
        Type = PageType.QualificationDetails,
        Title = "Provide the missing qualification details for {pupilName}",
        // The two answers a qualification change clears. Present so the completeness guard can find
        // the page that owes them, exactly as the shipped flow config does.
        Questions =
        [
            new Question { Id = "q-syllabus-code", Type = QuestionType.SyllabusSelect, Title = "Select syllabus code" },
            new Question { Id = "q-missing-grade", Type = QuestionType.GradeSelect, Title = "Select the missing grade {pupilName} achieved" }
        ]
    };

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "cohort-scope",
        Pages = [SelectQualificationPage, DetailsPage]
    };

    public JourneyControllerQualificationSearchTests()
    {
        _currentUser.OrganisationLaestab.Returns("860/4070");
        _currentUser.OrganisationUrn.Returns("142313");
        _flowService.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16).Returns(Flow);
        _flowService.GetPage(Flow, "select-qualification").Returns(SelectQualificationPage);
        _flowService.GetPage(Flow, "qualification-details").Returns(DetailsPage);
        _flowService.GetNavigationGuard(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>(), Arg.Any<string>())
            .Returns((JourneyNavigation?)null);
        _qualificationReference.GetLookupAsync(Arg.Any<CancellationToken>())
            .Returns(QualificationReferenceLookup.Parse(Sample));

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _vmBuilder = new JourneyViewModelBuilder(_flowService, _journeyService, _optionVisibility, _currentUser);

        _sut = new JourneyController(
            _flowService, _journeyService, _fileStorage, _requestService, _pupilData, _vmBuilder,
            _analytics, _currentUser, _optionVisibility, _optionality, _originCapture, _results,
            _gradeReference, _qualificationReference, _notifications, OpenCheckingExercises.AlwaysOpen(),
            NullLogger<JourneyController>.Instance)
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
            SelectedWhatToChange = WhatToChange.MissingQualification,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(),
            QuestionHistory = ["select-student-single", "select-qualification"]
        };
        tweak?.Invoke(state);
        _session.SetRequestState(WindowId, state);
        return state;
    }

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_renders_the_search_view_with_AOs_and_the_pupil_card()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(await _sut.QualificationSearchPage(WindowId, "select-qualification"));

        Assert.Equal("QualificationSearch", view.ViewName);
        var model = Assert.IsType<QualificationSearchViewModel>(view.Model);
        Assert.Equal(["Active IQ", "AQA"], model.AwardingOrganisations);
        Assert.Equal(CypmdId, model.CypmdId);
    }

    [Fact]
    public async Task Get_on_a_non_qualification_page_is_NotFound()
    {
        ReadyJourney();

        Assert.IsType<NotFoundResult>(await _sut.QualificationSearchPage(WindowId, "qualification-details"));
    }

    // ── POST: fail closed ────────────────────────────────────────────────────

    [Fact]
    public async Task Post_with_no_AO_rejects_with_the_AO_message()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(
            await _sut.QualificationSearchPost(WindowId, "select-qualification", null, "60146084"));

        Assert.Equal("QualificationSearch", view.ViewName);
        Assert.Equal(
            "Select the Awarding Organisation (AO) name",
            _sut.ModelState["selectedAo"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_with_no_QAN_rejects_with_the_QAN_message()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(
            await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", null));

        Assert.Equal("QualificationSearch", view.ViewName);
        Assert.Equal(
            "Select the Qualification Number (QAN)",
            _sut.ModelState["selectedQan"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_with_a_QAN_outside_the_reference_fails_closed()
    {
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(
            await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", "00000000"));

        Assert.Equal("QualificationSearch", view.ViewName);
        Assert.Null(_session.GetRequestState(WindowId).SelectedQualification);
        Assert.Equal(
            "Select the Qualification Number (QAN)",
            _sut.ModelState["selectedQan"]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Post_with_a_QAN_that_belongs_to_a_different_AO_fails_closed()
    {
        // The QAN dropdown is filtered by AO client-side only; the pairing must hold server-side
        // or a tampered form records an AO the qualification does not belong to.
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(
            await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", "6016041X"));

        Assert.Equal("QualificationSearch", view.ViewName);
        Assert.Null(_session.GetRequestState(WindowId).SelectedQualification);
    }

    // ── POST: happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Post_with_a_valid_pair_stores_the_resolved_qualification_and_redirects_next()
    {
        ReadyJourney();

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", "60146084"));

        var stored = _session.GetRequestState(WindowId).SelectedQualification;
        Assert.NotNull(stored);
        Assert.Equal("60146084", stored.Qan);
        Assert.Equal("AQA", stored.AwardingOrganisation);
        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Changing_the_qualification_clears_the_syllabus_and_grade_answers()
    {
        // A syllabus code and grade belong to one qualification; carrying them to another would
        // submit values the new qualification never offered. Re-confirming the SAME QAN keeps them.
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("6016041X");
        ReadyJourney(s =>
        {
            s.SelectedQualification = qualification;
            s.QuestionAnswers["q-syllabus-code"] = new QuestionAnswer { TextValue = "X" };
            s.QuestionAnswers["q-missing-grade"] = new QuestionAnswer { TextValue = "D" };
        });

        await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", "60146084");

        var state = _session.GetRequestState(WindowId);
        Assert.Equal("60146084", state.SelectedQualification!.Qan);
        Assert.DoesNotContain("q-syllabus-code", state.QuestionAnswers.Keys);
        Assert.DoesNotContain("q-missing-grade", state.QuestionAnswers.Keys);
    }

    [Fact]
    public async Task Reselecting_the_same_qualification_keeps_the_syllabus_and_grade_answers()
    {
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("60146084");
        ReadyJourney(s =>
        {
            s.SelectedQualification = qualification;
            s.QuestionAnswers["q-syllabus-code"] = new QuestionAnswer { TextValue = "8300H" };
            s.QuestionAnswers["q-missing-grade"] = new QuestionAnswer { TextValue = "2" };
        });

        await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", "60146084");

        var state = _session.GetRequestState(WindowId);
        Assert.Equal("8300H", state.QuestionAnswers["q-syllabus-code"].TextValue);
        Assert.Equal("2", state.QuestionAnswers["q-missing-grade"].TextValue);
    }

    // ── Arrived from the check-answers Change link (AB#297848) ───────────────

    [Fact]
    public async Task Reconfirming_the_same_qualification_from_the_summary_returns_to_the_summary()
    {
        // The summary's Change link on the AO/QAN rows routes through the generic Page action, which
        // dropped fromSummary — so "Change" then "Continue" marched the user forward through the rest
        // of the journey instead of back to check answers.
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("60146084");
        ReadyJourney(s =>
        {
            s.SelectedQualification = qualification;
            // Reached the summary, so the later pages are in history — the state a Change link
            // actually arrives in.
            s.QuestionHistory = ["select-student-single", "select-qualification", "qualification-details"];
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.QualificationSearchPost(
            WindowId, "select-qualification", "AQA", "60146084", fromSummary: true));

        Assert.Equal(nameof(JourneyController.Summary), redirect.ActionName);

        // Asserting the redirect target alone is NOT enough, and a browser walk proved it: the POST
        // returned Summary while the summary GET immediately 302'd the user back to
        // qualification-details, because trimming the history left QuestionHistory ending at this
        // page and the summary recomputed the "next unanswered page" from it. The history the
        // summary depends on must survive a no-op re-confirmation.
        var history = _session.GetRequestState(WindowId).QuestionHistory;
        Assert.Equal(["select-student-single", "select-qualification", "qualification-details"], history);
    }

    [Fact]
    public async Task Changing_the_qualification_from_the_summary_still_goes_to_the_details_page()
    {
        // Changing it cleared the syllabus code and grade, so those answers are owed again — going
        // straight back to the summary would show a half-empty enquiry the user never revisited.
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("6016041X");
        ReadyJourney(s => s.SelectedQualification = qualification);

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.QualificationSearchPost(
            WindowId, "select-qualification", "AQA", "60146084", fromSummary: true));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task The_generic_page_action_hands_fromSummary_to_the_qualification_search()
    {
        // The Change link points at Page; Page redirects to this page's own action. The flag has to
        // survive that hop or the two tests above can never be reached from the summary at all.
        ReadyJourney();

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _sut.Page(WindowId, "select-qualification", fromSummary: true));

        Assert.Equal(nameof(JourneyController.QualificationSearchPage), redirect.ActionName);
        Assert.Equal(true, redirect.RouteValues!["fromSummary"]);
    }

    [Fact]
    public async Task A_validation_failure_keeps_the_from_summary_context()
    {
        // Losing it on redisplay would strand the user mid-journey after a mistyped post.
        ReadyJourney();

        var view = Assert.IsType<ViewResult>(await _sut.QualificationSearchPost(
            WindowId, "select-qualification", "AQA", "00000000", fromSummary: true));

        Assert.True(Assert.IsType<QualificationSearchViewModel>(view.Model).FromSummary);
    }

    [Fact]
    public async Task The_QAN_rejection_message_comes_from_the_flow_config()
    {
        // The controller hardcoded this string, which made the page's validationFailure dead config —
        // a content edit to MissingQualification_Post16.json changed nothing on screen while the flow
        // test that pins it stayed green.
        _flowService.GetPage(Flow, "select-qualification").Returns(new JourneyPage
        {
            Id = "select-qualification",
            Type = PageType.QualificationSearch,
            Title = "Provide the missing qualification details for {pupilName}",
            ValidationFailure = "Edited in the flow config",
            NextPageId = "qualification-details"
        });
        ReadyJourney();

        await _sut.QualificationSearchPost(WindowId, "select-qualification", "AQA", "00000000");

        Assert.Equal("Edited in the flow config",
            _sut.ModelState["selectedQan"]!.Errors[0].ErrorMessage);
    }

    // ── Submitting a stale summary (AB#297848) ───────────────────────────────

    [Fact]
    public async Task Submitting_after_the_qualification_changed_underneath_is_sent_back_not_persisted()
    {
        // The summary GET's completeness check can be stale by the time Submit is pressed: changing
        // the qualification in another tab (or via the back button) clears the syllabus code and
        // grade. Without a re-check on POST this persisted and emailed an enquiry missing both —
        // SubmitResultsEnquiryAsync only asserts the qualification itself is present.
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("60146084");
        ReadyJourney(s =>
        {
            s.SelectedQualification = qualification;
            s.ReferenceNumber = "CYPMD_16to19_RE_1A2B3C4";
            s.QuestionHistory = ["select-qualification", "qualification-details"];
            // q-syllabus-code and q-missing-grade deliberately absent: the other tab cleared them.
        });

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.Page), redirect.ActionName);
        Assert.Equal("qualification-details", redirect.RouteValues!["pageId"]);
        await _requestService.DidNotReceiveWithAnyArgs()
            .SubmitResultsEnquiryAsync(default, default!, default);
    }

    [Fact]
    public async Task A_complete_enquiry_still_submits()
    {
        // The guard must reject only the incomplete case — a guard that blocks everything would
        // pass the test above while breaking the journey.
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("60146084");
        ReadyJourney(s =>
        {
            s.SelectedQualification = qualification;
            s.ReferenceNumber = "CYPMD_16to19_RE_1A2B3C4";
            s.QuestionHistory = ["select-qualification", "qualification-details"];
            s.QuestionAnswers["q-syllabus-code"] = new QuestionAnswer { TextValue = "8300H" };
            s.QuestionAnswers["q-missing-grade"] = new QuestionAnswer { TextValue = "2" };
        });
        _requestService.SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>())
            .Returns("CYPMD_16to19_RE_1A2B3C4");

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.EnquiryConfirmation), redirect.ActionName);
        await _requestService.Received(1)
            .SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unloadable_flow_config_still_lets_a_started_enquiry_submit()
    {
        // The completeness re-check is an ADDITION to a shipped journey, and submission never needed
        // the flow config before it. If the config cannot be loaded the check is skipped rather than
        // refusing the submission — turning a blob blip into a lost enquiry at the final click would
        // be a worse regression than the stale state this guards, which also needs a second tab.
        _flowService.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16)
            .Returns((QuestionFlowConfig?)null);
        var qualification = QualificationReferenceLookup.Parse(Sample).Find("60146084");
        ReadyJourney(s =>
        {
            s.SelectedQualification = qualification;
            s.ReferenceNumber = "CYPMD_16to19_RE_1A2B3C4";
            s.QuestionHistory = ["select-qualification", "qualification-details"];
        });
        _requestService.SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>())
            .Returns("CYPMD_16to19_RE_1A2B3C4");

        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.SummaryConfirm(WindowId));

        Assert.Equal(nameof(JourneyController.EnquiryConfirmation), redirect.ActionName);
        await _requestService.Received(1)
            .SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<CancellationToken>());
    }

    // ── Reference number generation (AB#297848) ──────────────────────────────

    [Fact]
    public async Task Choosing_the_student_generates_the_enquiry_shaped_reference_not_the_amendment_one()
    {
        // AB#296648's dispatch in PupilSearchPost only checked WhatToChange.IncorrectGrade — a real
        // gap this sibling enquiry exposed. The "RE" segment is how support staff tell an enquiry
        // from an amendment when a school reads a reference aloud.
        var studentPage = new JourneyPage
        {
            Id = "select-student-single",
            Type = PageType.PupilSearch,
            PupilKey = JourneyPage.PrimaryKey,
            PupilFilter = PupilFilter.All,
            NextPageId = "select-qualification"
        };
        var flow = new QuestionFlowConfig
        {
            FirstPageId = "select-student-single",
            Pages = [studentPage, SelectQualificationPage, DetailsPage]
        };
        _flowService.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16).Returns(flow);
        foreach (var page in flow.Pages)
            _flowService.GetPage(flow, page.Id).Returns(page);
        _journeyService.GenerateEnquiryReference().Returns("CYPMD_16to19_RE_ABCDEF1");

        var pupil = Pupil();
        _pupilData.GetPupilAsync(WindowId, pupil.Id).Returns(pupil);
        _requestService.HasSubmittedRequestAsync(WindowId, pupil.Id, Arg.Any<long>())
            .Returns(new DuplicateCheckResult.NoConflict());

        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedWhatToChange = WhatToChange.MissingQualification,
            CheckingWindow = new CheckingWindowDto
            {
                Title = "16 to 19", KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
            },
            QuestionHistory = []
        });

        await _sut.PupilSearchPost(WindowId, "select-student-single", pupil.Id.ToString(), "Alice, Smith");

        Assert.Equal("CYPMD_16to19_RE_ABCDEF1", _session.GetRequestState(WindowId).ReferenceNumber);
        _journeyService.DidNotReceive().GenerateReference(Arg.Any<CheckingWindowType?>());
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
