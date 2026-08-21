using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class JourneyControllerTests
{
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IFileStorageService _fileStorageService = Substitute.For<IFileStorageService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IOptionVisibilityService _optionVisibilityService = Substitute.For<IOptionVisibilityService>();
    private readonly IQuestionOptionalityService _optionalityService = Substitute.For<IQuestionOptionalityService>();
    private readonly IOriginCountryLanguageCapture _languageCapture = Substitute.For<IOriginCountryLanguageCapture>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly ICheckYourPupilDataService _pupilDataService = Substitute.For<ICheckYourPupilDataService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService _requestNotificationService =
        Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>();
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly JourneyController _sut;

    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Simple two-page linear config: page-1 (Radio) → page-2 (TextArea) → Summary
    private static readonly QuestionFlowConfig Config = new()
    {
        FirstPageId = "page-1",
        Pages =
        [
            new JourneyPage
            {
                Id = "page-1",
                Questions = [new Question { Id = "q1", Type = QuestionType.Radio, Title = "Q1",
                    Options = [new QuestionOption { Value = "opt1", Label = "Option 1" }] }],
                NextPageId = "page-2"
            },
            new JourneyPage
            {
                Id = "page-2",
                Questions = [new Question { Id = "q2", Type = QuestionType.TextArea, Title = "Q2" }]
            }
        ]
    };

    private static readonly JourneyPage EvidencePage = new()
    {
        Id = "evidence-page",
        Type = PageType.EvidenceUpload,
        Questions = [new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Evidence" }]
    };

    // Evidence page mixing a file upload with a free-text explanation — the shape that
    // triggers the "text lost on upload/remove" bug.
    private static readonly JourneyPage EvidenceWithTextPage = new()
    {
        Id = "evidence-with-text",
        Type = PageType.EvidenceUpload,
        Questions =
        [
            new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Evidence" },
            new Question { Id = "explanation", Type = QuestionType.TextArea, Title = "Explain" }
        ]
    };

    public JourneyControllerTests()
    {

        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Config);
        _flowService.GetPage(Config, "page-1").Returns(Config.Pages[0]);
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]);
        _flowService.GetNextPageId(Config, "page-1", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns("page-2");
        _flowService.GetNextPageId(Config, "page-2", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);

        _optionVisibilityService
            .GetVisibleOptions(Arg.Any<Question>(), Arg.Any<JourneyConditionContext>())
            .Returns(ci => ci.Arg<Question>().Options ?? (IReadOnlyList<QuestionOption>)[]);

        _optionalityService.GetConditionallyOptionalQuestionIds(Arg.Any<JourneyPage>(), Arg.Any<JourneyConditionContext>())
            .Returns(new HashSet<string>());

        // NSubstitute returns "" for string by default; null means "no duplicate" (AB#296081).
        _journeyService.ValidateDuplicateFileName(default!, default!).ReturnsForAnyArgs((string?)null);

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        var viewModelBuilder = new JourneyViewModelBuilder(
            _flowService, _journeyService, _optionVisibilityService, _currentUserService);

        _sut = new JourneyController(_flowService, _journeyService, _fileStorageService,
            _requestService, _pupilDataService, viewModelBuilder, _analytics, _currentUserService,
            _optionVisibilityService, _optionalityService, _languageCapture,
            Substitute.For<DfE.CheckPerformanceData.Application.ResultsEnquiry.IStudentResultsClient>(),
            Substitute.For<DfE.CheckPerformanceData.Application.ResultsEnquiry.IGradeReferenceClient>(),
            _requestNotificationService,
            _checkingExercises,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext },
            TempData = new TempDataDictionary(_httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    // ── Session guard — Page GET ─────────────────────────────────────────────

    [Fact]
    public async Task Page_WhenNoSession_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());  // empty — no WhatToChange, no window, no pupil

        var result = await _sut.Page(WindowId, "page-1");

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public async Task Page_RadioQuestion_UsesFilteredVisibleOptions()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        var visible = new List<QuestionOption> { new() { Value = "opt1", Label = "Option 1" } };
        _optionVisibilityService
            .GetVisibleOptions(Arg.Is<Question>(q => q.Id == "q1"), Arg.Any<JourneyConditionContext>())
            .Returns(visible);

        var result = await _sut.Page(WindowId, "page-1");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PageViewModel>(view.Model);
        var radioModel = model.QuestionModels.Single(qm => qm.Question.Id == "q1");
        Assert.Same(visible, radioModel.VisibleOptions);
    }

    // ── Session guard — Summary GET ──────────────────────────────────────────

    [Fact]
    public async Task Summary_WhenNoSession_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = await _sut.Summary(WindowId);

        AssertRedirectToCheckYourData(result);
    }

    // ── Session guard — Confirmation GET ────────────────────────────────────

    [Fact]
    public void Confirmation_WhenNoReferenceNumber_RedirectsToCheckYourData()
    {
        var state = ValidSession();
        state.ReferenceNumber = null;
        SetupSession(state);

        var result = _sut.Confirmation(WindowId);

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public void Confirmation_WhenNoCheckingWindow_RedirectsToCheckYourData()
    {
        var state = ValidSession();
        state.CheckingWindow = null;
        SetupSession(state);

        var result = _sut.Confirmation(WindowId);

        AssertRedirectToCheckYourData(result);
    }

    // ── Navigation validation ────────────────────────────────────────────────

    [Fact]
    public async Task Page_WhenHistoryEmptyAndRequestingFirstPage_AllowsAccess()
    {
        SetupSession(ValidSession(history: []));

        var result = await _sut.Page(WindowId, "page-1");

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Page_WhenPageAlreadyInHistory_AllowsAccess()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        var result = await _sut.Page(WindowId, "page-1");

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Page_WhenPageIsExpectedNextAfterHistory_AllowsAccess()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        var result = await _sut.Page(WindowId, "page-2");

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Page_WhenSkippingAhead_RedirectsToExpectedNextPage()
    {
        SetupSession(ValidSession(history: []));
        _flowService.GetNavigationGuard(Config, Arg.Any<RequestState>(), "page-2")
            .Returns(new RedirectToJourneyPage("page-1"));

        var result = await _sut.Page(WindowId, "page-2");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("page-1", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Page_WhenJourneyCompleteAndRequestingUnvisitedPage_RedirectsToSummary()
    {
        var extendedConfig = new QuestionFlowConfig
        {
            FirstPageId = "page-1",
            Pages = Config.Pages.Append(new JourneyPage { Id = "page-3", Questions = [] }).ToList()
        };
        SetupSession(ValidSession(history: ["page-1", "page-2"]));
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(extendedConfig);
        _flowService.GetPage(extendedConfig, "page-3").Returns(extendedConfig.Pages[2]);
        _flowService.GetNavigationGuard(extendedConfig, Arg.Any<RequestState>(), "page-3")
            .Returns(new RedirectToJourneySummary());

        var result = await _sut.Page(WindowId, "page-3");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ActionName);
    }

    // ── Summary completeness ─────────────────────────────────────────────────

    [Fact]
    public async Task Summary_WhenHistoryEmpty_RedirectsToFirstPage()
    {
        SetupSession(ValidSession(history: []));

        var result = await _sut.Summary(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("page-1", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Summary_WhenJourneyIncomplete_RedirectsToNextPage()
    {
        // Only page-1 answered; GetNextPageId returns page-2 (not done yet)
        SetupSession(ValidSession(history: ["page-1"]));

        var result = await _sut.Summary(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("page-2", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Summary_WhenJourneyComplete_RendersView()
    {
        // Both pages answered; GetNextPageId after page-2 returns null
        SetupSession(ValidSession(history: ["page-1", "page-2"]));

        var result = await _sut.Summary(WindowId);

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Summary_WhenSingleEditMode_SetsFromEditOnViewModel()
    {
        SetupSession(ValidSession(history: ["page-1", "page-2"]));
        _session.SetSingleEditMode(WindowId);

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.True(vm.FromEdit);
    }

    [Fact]
    public async Task Summary_WhenNotSingleEditMode_FromEditIsFalse()
    {
        SetupSession(ValidSession(history: ["page-1", "page-2"]));

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.False(vm.FromEdit);
    }

    // ── Summary evidence re-validation (PBI 292266) ──────────────────────────

    [Fact]
    public async Task Summary_WhenReachableEvidencePageNoLongerValid_RedirectsToIt()
    {
        // Changing an answer from the Summary (e.g. First language) can revoke a
        // conditionally-waived evidence page without the user passing through it again.
        // Nothing else re-validates before submission, so the Summary must send them back.
        SetupSession(ValidSession(history: ["page-1", "page-2", "evidence-page"]));
        // Unstubbed string-returning calls yield string.Empty under NSubstitute, not null,
        // which the completeness guard would read as "there is a next page".
        _flowService.GetNextPageId(Config, "evidence-page", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        _flowService.GetReachableEvidencePage(Config, Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns(EvidencePage);
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>(),
                Arg.Any<IReadOnlySet<string>?>())
            .Returns(new EvidenceValidationResult { Messages = ["Upload at least one file before continuing"] });

        var result = await _sut.Summary(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("evidence-page", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Summary_WhenReachableEvidencePageStillValid_RendersView()
    {
        SetupSession(ValidSession(history: ["page-1", "page-2", "evidence-page"]));
        // Unstubbed string-returning calls yield string.Empty under NSubstitute, not null,
        // which the completeness guard would read as "there is a next page".
        _flowService.GetNextPageId(Config, "evidence-page", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        _flowService.GetReachableEvidencePage(Config, Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns(EvidencePage);
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>(),
                Arg.Any<IReadOnlySet<string>?>())
            .Returns((EvidenceValidationResult?)null);

        var result = await _sut.Summary(WindowId);

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Summary_WhenInvalidEvidencePageNotYetVisited_DoesNotRedirect()
    {
        // Guards against a redirect loop: GetNavigationGuard bounces an unvisited page back
        // to the Summary once the journey is complete, so the Summary must not bounce the
        // user onto an evidence page they have never reached.
        SetupSession(ValidSession(history: ["page-1", "page-2"]));
        _flowService.GetReachableEvidencePage(Config, Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns(EvidencePage);
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>(),
                Arg.Any<IReadOnlySet<string>?>())
            .Returns(new EvidenceValidationResult { Messages = ["Upload at least one file before continuing"] });

        var result = await _sut.Summary(WindowId);

        Assert.IsType<ViewResult>(result);
    }

    // ── SummaryConfirm ───────────────────────────────────────────────────────

    [Fact]
    public async Task SummaryConfirm_WhenDuplicateRequestException_ReturnsSummaryViewWithConflictError()
    {
        SetupSession(ValidSession(history: ["page-1"]));
        _requestService.ConfirmRequestAsync(WindowId, Arg.Any<RequestState>())
            .Returns(_ => throw new DuplicateRequestException(ConflictType.SelfSubmitted, "", "", "", false));

        var result = await _sut.SummaryConfirm(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Summary", view.ViewName);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Equal(DuplicateRequestMessages.SummaryMessage(true, false, ""), vm.ConflictError);
    }

    [Fact]
    public async Task SummaryConfirm_WhenDuplicateRequestException_DoesNotClearSession()
    {
        var state = ValidSession(history: ["page-1"]);
        SetupSession(state);
        _requestService.ConfirmRequestAsync(WindowId, Arg.Any<RequestState>())
            .Returns(_ => throw new DuplicateRequestException(ConflictType.SelfSubmitted, "", "", "", false));

        await _sut.SummaryConfirm(WindowId);

        var remaining = _session.GetRequestState(WindowId);
        Assert.NotNull(remaining.SelectedPupil);
        Assert.NotNull(remaining.SelectedWhatToChange);
        Assert.NotEmpty(remaining.QuestionHistory);
    }

    [Fact]
    public async Task SummaryConfirm_AfterSuccess_ClearsJourneyButPreservesConfirmationData()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        await _sut.SummaryConfirm(WindowId);

        var remaining = _session.GetRequestState(WindowId);
        Assert.Null(remaining.SelectedPupil);
        Assert.Null(remaining.SelectedWhatToChange);
        Assert.Empty(remaining.QuestionAnswers);
        Assert.Empty(remaining.QuestionHistory);
        Assert.Equal("CYPMD_KS4June_ABC1234", remaining.ReferenceNumber);
        Assert.NotNull(remaining.CheckingWindow);
    }

    [Fact]
    public async Task SummaryConfirm_AfterSuccess_ClearsMatchedPupil()
    {
        var state = ValidSession(history: ["page-1"]);
        state.MatchedPupil = new PupilDto { Id = Guid.NewGuid(), Firstname = "John", Surname = "Doe", Sex = "M", DateOfBirth = "02/02/2010", Age = 16, Cypmd_Id = "CYPMD456", Identifier = "456456" };
        state.MatchedPupilId = state.MatchedPupil.Id.ToString();
        state.MatchedPupilLabel = "Doe, John";
        SetupSession(state);

        await _sut.SummaryConfirm(WindowId);

        var remaining = _session.GetRequestState(WindowId);
        Assert.Null(remaining.MatchedPupil);
        Assert.Null(remaining.MatchedPupilId);
        Assert.Null(remaining.MatchedPupilLabel);
    }

    [Fact]
    public async Task SummaryConfirm_AfterSuccess_EmitsRequestSubmittedEvent()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        await _sut.SummaryConfirm(WindowId);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<RequestSubmittedEvent>(e =>
                e.WhatToChange == "Remove" &&
                e.CheckingWindowType == "KS4June" &&
                e.ReferenceNumber == "CYPMD_KS4June_ABC1234"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SummaryConfirm_WhenDuplicate_EmitsRequestSubmissionFailedEvent()
    {
        SetupSession(ValidSession(history: ["page-1"]));
        _requestService.ConfirmRequestAsync(WindowId, Arg.Any<RequestState>())
            .Returns(_ => throw new DuplicateRequestException(ConflictType.SelfSubmitted, "", "", "", false));

        await _sut.SummaryConfirm(WindowId);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<RequestSubmissionFailedEvent>(e =>
                e.FailureReason == "duplicate_request" && e.WhatToChange == "Remove"),
            Arg.Any<CancellationToken>());
    }

    // ── SummaryConfirm — results enquiry notification failure (AB#296648) ────

    [Fact]
    public async Task SummaryConfirm_WhenEnquiryNotificationFails_StillRedirectsToEnquiryConfirmation()
    {
        var state = ValidSession(history: ["page-1"]);
        state.SelectedWhatToChange = WhatToChange.IncorrectGrade;
        SetupSession(state);
        _requestService.SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>())
            .Returns("CYPMD_KS4June_ABC1234");
        _requestNotificationService.NotifyResultsEnquirySubmittedAsync(Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("notify queue unavailable"));

        var result = await _sut.SummaryConfirm(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(JourneyController.EnquiryConfirmation), redirect.ActionName);
    }

    [Fact]
    public async Task SummaryConfirm_WhenEnquiryNotificationFails_ResetsSessionToWindowAndReference()
    {
        var state = ValidSession(history: ["page-1"]);
        state.SelectedWhatToChange = WhatToChange.IncorrectGrade;
        SetupSession(state);
        _requestService.SubmitResultsEnquiryAsync(WindowId, Arg.Any<RequestState>())
            .Returns("CYPMD_KS4June_ABC1234");
        _requestNotificationService.NotifyResultsEnquirySubmittedAsync(Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("notify queue unavailable"));

        await _sut.SummaryConfirm(WindowId);

        var remaining = _session.GetRequestState(WindowId);
        Assert.Equal("CYPMD_KS4June_ABC1234", remaining.ReferenceNumber);
        Assert.NotNull(remaining.CheckingWindow);
        Assert.Null(remaining.SelectedWhatToChange);
        Assert.Null(remaining.SelectedPupil);
        Assert.Empty(remaining.QuestionAnswers);
        Assert.Empty(remaining.QuestionHistory);
    }

    // ── PagePost — Autocomplete code capture ─────────────────────────────────

    [Fact]
    public async Task PagePost_AutocompleteQuestion_CapturesCodeValueAlongsideDisplayName()
    {
        var page = new JourneyPage
        {
            Id = "country-page",
            Questions = [new Question { Id = "country-originally-from", Type = QuestionType.Autocomplete, Title = "Country" }]
        };
        _flowService.GetPage(Config, "country-page").Returns(page);
        _flowService.GetNextPageId(Config, "country-page", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((string?)null);
        SetupSession(ValidSession(history: ["country-page"]));
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_country_originally_from"] = "France",
            ["q_country_originally_from_code"] = "FR"
        });

        await _sut.PagePost(WindowId, "country-page", fromSummary: false);

        var saved = _session.GetRequestState(WindowId).QuestionAnswers["country-originally-from"];
        Assert.Equal("France", saved.TextValue);
        Assert.Equal("FR", saved.CodeValue);
    }

    // ── PagePost — back navigation changing a branch ─────────────────────────

    [Fact]
    public async Task PagePost_WhenGoingBackAndChangingBranch_TrimsStaleDownstreamHistory()
    {
        SetupReasonBranchPage();
        SetupSession(ValidSession(
            history: ["select-pupil", "reason", "year-group-change-higher-lower"],
            answers: new Dictionary<string, QuestionAnswer>
            {
                ["reason"] = new() { TextValue = "year-group-change" },
                ["higher-lower"] = new() { TextValue = "higher" }
            }));
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_reason"] = "pupil-died"
        });

        var result = await _sut.PagePost(WindowId, "reason", fromSummary: false);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("pupil-died", redirect.RouteValues!["pageId"]);

        // The old branch's pages after "reason" must be dropped, otherwise the navigation
        // guard recomputes the next page from the stale last entry and bounces the user
        // back into the year-group-change branch.
        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(["select-pupil", "reason"], saved.QuestionHistory);
    }

    [Fact]
    public async Task PagePost_WhenGoingBackAndKeepingSameBranch_PreservesDownstreamHistory()
    {
        SetupReasonBranchPage();
        SetupSession(ValidSession(
            history: ["select-pupil", "reason", "year-group-change-higher-lower"],
            answers: new Dictionary<string, QuestionAnswer>
            {
                ["reason"] = new() { TextValue = "year-group-change" },
                ["higher-lower"] = new() { TextValue = "higher" }
            }));
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_reason"] = "year-group-change"
        });

        var result = await _sut.PagePost(WindowId, "reason", fromSummary: false);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("year-group-change-higher-lower", redirect.RouteValues!["pageId"]);

        // Unchanged branch — the already-visited downstream page stays in history so the
        // user keeps their progress rather than being forced to re-walk the branch.
        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(["select-pupil", "reason", "year-group-change-higher-lower"], saved.QuestionHistory);
    }

    // A "reason" Radio page whose answer branches to different next pages, and a GetNextPageId
    // stub that resolves the branch from the current answer value in the answers dictionary.
    private void SetupReasonBranchPage()
    {
        var reasonPage = new JourneyPage
        {
            Id = "reason",
            Questions =
            [
                new Question
                {
                    Id = "reason", Type = QuestionType.Radio, Title = "Why?",
                    Options =
                    [
                        new QuestionOption { Value = "year-group-change", Label = "Year group change", NextPageId = "year-group-change-higher-lower" },
                        new QuestionOption { Value = "pupil-died", Label = "Pupil died", NextPageId = "pupil-died" }
                    ]
                }
            ]
        };
        _flowService.GetPage(Config, "reason").Returns(reasonPage);
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((string?)null);
        _flowService.GetNextPageId(Config, "reason", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns(ci =>
            {
                var answers = ci.Arg<Dictionary<string, QuestionAnswer>>();
                return answers.TryGetValue("reason", out var a) && a.TextValue == "year-group-change"
                    ? "year-group-change-higher-lower"
                    : "pupil-died";
            });
    }

    // ── PagePost — hidden-option server-side gate ────────────────────────────

    [Fact]
    public async Task PagePost_RadioValueForHiddenOption_IsRejectedWithTheQuestionsValidationMessage()
    {
        SetupReasonBranchPage();
        // Simulate the option being filtered out for this user/pupil: the
        // visibility service returns every option EXCEPT year-group-change.
        _optionVisibilityService.GetVisibleOptions(Arg.Any<Question>(), Arg.Any<JourneyConditionContext>())
            .Returns(ci => (IReadOnlyList<QuestionOption>)(ci.Arg<Question>().Options?
                .Where(o => o.Value != "year-group-change").ToList() ?? []));
        _journeyService.IsAnswered(Arg.Any<Question>(), Arg.Any<QuestionAnswer>()).Returns(true);
        SetupSession(ValidSession(history: ["select-pupil", "reason"]));
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_reason"] = "year-group-change"
        });

        var result = await _sut.PagePost(WindowId, "reason", fromSummary: false);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Page", view.ViewName);
        Assert.False(_sut.ModelState.IsValid);
        // The saved journey must not advance into the hidden branch.
        Assert.DoesNotContain("year-group-change",
            _session.GetRequestState(WindowId).QuestionHistory);
    }

    [Fact]
    public async Task PagePost_RadioValueForVisibleOption_StillProceeds()
    {
        SetupReasonBranchPage();
        SetupSession(ValidSession(history: ["select-pupil", "reason"]));
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_reason"] = "pupil-died"
        });

        var result = await _sut.PagePost(WindowId, "reason", fromSummary: false);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
    }

    [Fact]
    public async Task PagePost_WhenInvalid_EmitsValidationErrorWithCodes()
    {
        SetupSession(ValidSession(history: ["page-1"]));
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns("Enter something");
        _journeyService.IsAnswered(Arg.Any<Question>(), Arg.Any<QuestionAnswer>()).Returns(false);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        await _sut.PagePost(WindowId, "page-2", fromSummary: false);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e =>
                e.ErrorCodes.Contains("required") && e.WhatToChange == "Remove" && e.FromSummary == false),
            Arg.Any<CancellationToken>());
    }

    // ── PagePost — cross-field date rules (AB#295246) ────────────────────────

    [Fact]
    public async Task PagePost_DateRuleViolation_AddsTheErrorUnderTheQuestionId()
    {
        // The ModelState key has to be the raw question id: JourneyViewModelBuilder looks the
        // error up by it, and anything else means the message renders nowhere at all.
        SetupDatePage();
        _journeyService.ValidatePageDates(Arg.Any<JourneyPage>(),
                Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<string>())
            .Returns([new DateFieldViolation("date-a", "Dates are the wrong way round")]);
        SetupSession(ValidSession(history: ["date-page"]));
        PostDates(aYear: "2023", bYear: "2024");

        var result = await _sut.PagePost(WindowId, "date-page", fromSummary: false);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Page", view.ViewName);
        Assert.False(_sut.ModelState.IsValid);
        Assert.Equal("Dates are the wrong way round",
            _sut.ModelState["date-a"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task PagePost_DateRuleViolation_DoesNotDisplaceAFormatError()
    {
        // Only the first error per question is rendered. A comparison against a date the user
        // never successfully entered must not replace "must be a real date".
        SetupDatePage();
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns("Enter a real date");
        _journeyService.ValidatePageDates(Arg.Any<JourneyPage>(),
                Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<string>())
            .Returns([new DateFieldViolation("date-a", "Dates are the wrong way round")]);
        SetupSession(ValidSession(history: ["date-page"]));
        PostDates(aYear: "2023", bYear: "2024");

        var result = await _sut.PagePost(WindowId, "date-page", fromSummary: false);

        Assert.IsType<ViewResult>(result);
        Assert.Equal("Enter a real date", _sut.ModelState["date-a"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task PagePost_DateRuleViolation_IsCodedAsInconsistentNotBadDate()
    {
        // A well-formed date in the wrong place is a different failure from an unparseable one,
        // and the analytics taxonomy has to keep them apart.
        SetupDatePage();
        _journeyService.IsAnswered(Arg.Any<Question>(), Arg.Any<QuestionAnswer>()).Returns(true);
        _journeyService.ValidatePageDates(Arg.Any<JourneyPage>(),
                Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<string>())
            .Returns([new DateFieldViolation("date-a", "Dates are the wrong way round")]);
        SetupSession(ValidSession(history: ["date-page"]));
        PostDates(aYear: "2023", bYear: "2024");

        await _sut.PagePost(WindowId, "date-page", fromSummary: false);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e =>
                e.ErrorCodes.Contains("date_inconsistent") && !e.ErrorCodes.Contains("bad_date")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagePost_NoDateRuleViolations_StillAdvances()
    {
        SetupDatePage();
        SetupSession(ValidSession(history: ["date-page"]));
        PostDates(aYear: "2024", bYear: "2023");

        var result = await _sut.PagePost(WindowId, "date-page", fromSummary: false);

        Assert.IsType<RedirectToActionResult>(result);
    }

    // The same four behaviours against a removal journey page. The controller substitutes
    // ValidatePageDates, so what matters here is the wiring (raw question id as ModelState key,
    // first-error-wins, the date_inconsistent code, advance on no violations), not the rule.

    [Fact]
    public async Task PagePost_RemovalDateRuleViolation_AddsTheErrorUnderTheQuestionId()
    {
        SetupRemovalDatePage();
        _journeyService.ValidatePageDates(Arg.Any<JourneyPage>(),
                Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<string>())
            .Returns([new DateFieldViolation(
                RemovalJourneyDateRules.DateRemovedFromRoll,
                "Date Jane Smith was removed from your school roll must be in the past")]);
        SetupSession(ValidSession(history: ["pupil-died"]));
        PostSingleDate(RemovalJourneyDateRules.DateRemovedFromRoll, year: "2027");

        var result = await _sut.PagePost(WindowId, "pupil-died", fromSummary: false);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Page", view.ViewName);
        Assert.False(_sut.ModelState.IsValid);
        Assert.Equal("Date Jane Smith was removed from your school roll must be in the past",
            _sut.ModelState[RemovalJourneyDateRules.DateRemovedFromRoll]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task PagePost_RemovalDateRuleViolation_DoesNotDisplaceAFormatError()
    {
        SetupRemovalDatePage();
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns("Enter a real date");
        _journeyService.ValidatePageDates(Arg.Any<JourneyPage>(),
                Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<string>())
            .Returns([new DateFieldViolation(
                RemovalJourneyDateRules.DateRemovedFromRoll,
                "Date Jane Smith was removed from your school roll must be in the past")]);
        SetupSession(ValidSession(history: ["pupil-died"]));
        PostSingleDate(RemovalJourneyDateRules.DateRemovedFromRoll, year: "2027");

        var result = await _sut.PagePost(WindowId, "pupil-died", fromSummary: false);

        Assert.IsType<ViewResult>(result);
        Assert.Equal("Enter a real date",
            _sut.ModelState[RemovalJourneyDateRules.DateRemovedFromRoll]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task PagePost_RemovalDateRuleViolation_IsCodedAsInconsistentNotBadDate()
    {
        SetupRemovalDatePage();
        _journeyService.IsAnswered(Arg.Any<Question>(), Arg.Any<QuestionAnswer>()).Returns(true);
        _journeyService.ValidatePageDates(Arg.Any<JourneyPage>(),
                Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<string>())
            .Returns([new DateFieldViolation(
                RemovalJourneyDateRules.DateRemovedFromRoll,
                "Date Jane Smith was removed from your school roll must be in the past")]);
        SetupSession(ValidSession(history: ["pupil-died"]));
        PostSingleDate(RemovalJourneyDateRules.DateRemovedFromRoll, year: "2027");

        await _sut.PagePost(WindowId, "pupil-died", fromSummary: false);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e =>
                e.ErrorCodes.Contains("date_inconsistent") && !e.ErrorCodes.Contains("bad_date")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagePost_RemovalDateNoViolations_StillAdvances()
    {
        SetupRemovalDatePage();
        SetupSession(ValidSession(history: ["pupil-died"]));
        PostSingleDate(RemovalJourneyDateRules.DateRemovedFromRoll, year: "2026");

        var result = await _sut.PagePost(WindowId, "pupil-died", fromSummary: false);

        Assert.IsType<RedirectToActionResult>(result);
    }

    private void SetupRemovalDatePage()
    {
        var page = new JourneyPage
        {
            Id = RemovalJourneyDateRules.PupilDiedPageId,
            Questions =
            [
                new Question
                {
                    Id = RemovalJourneyDateRules.DateRemovedFromRoll,
                    Type = QuestionType.Date,
                    Title = "Date the pupil was removed from your roll"
                }
            ],
            NextPageId = "page-2"
        };
        _flowService.GetPage(Config, RemovalJourneyDateRules.PupilDiedPageId).Returns(page);
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((string?)null);
        _flowService.GetNextPageId(Config, RemovalJourneyDateRules.PupilDiedPageId, Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns("page-2");
    }

    private void PostSingleDate(string questionId, string year) =>
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            [$"q_{questionId}_day"] = "4",
            [$"q_{questionId}_month"] = "9",
            [$"q_{questionId}_year"] = year
        });

    private void SetupDatePage()
    {
        var datePage = new JourneyPage
        {
            Id = "date-page",
            Questions =
            [
                new Question { Id = "date-a", Type = QuestionType.Date, Title = "Date A" },
                new Question { Id = "date-b", Type = QuestionType.Date, Title = "Date B" }
            ],
            NextPageId = "page-2"
        };
        _flowService.GetPage(Config, "date-page").Returns(datePage);
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((string?)null);
        _flowService.GetNextPageId(Config, "date-page", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns("page-2");
    }

    private void PostDates(string aYear, string bYear) =>
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_date_a_day"] = "4", ["q_date_a_month"] = "9", ["q_date_a_year"] = aYear,
            ["q_date_b_day"] = "8", ["q_date_b_month"] = "1", ["q_date_b_year"] = bYear
        });

    // ── SaveDraft ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveDraft_WhenSessionNotReady_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public async Task SaveDraft_AfterSaving_EmitsDraftSavedEvent()
    {
        SetupSession(ValidSession());

        await _sut.SaveDraft(WindowId, pageId: null);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<DraftSavedEvent>(e =>
                e.Status == "ReadyToSubmit" &&
                e.WhatToChange == "Remove" &&
                e.ReferenceNumber == "CYPMD_KS4June_ABC1234"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraft_WithoutPageId_CallsServiceAndRedirectsToAmendmentRequests()
    {
        SetupSession(ValidSession());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        await _requestService.Received(1).SaveDraftAsync(WindowId, Arg.Any<RequestState>(), Arg.Any<RequestStatus>());
        AssertRedirectToAmendmentRequests(result);
    }

    [Fact]
    public async Task SaveDraft_WithPageId_CapturesTextAreaAnswerFromFormBeforeSaving()
    {
        SetupSession(ValidSession());
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]); // page-2 has TextArea q2
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_q2"] = "My explanation"
        });

        RequestState? capturedJourney = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s), Arg.Any<RequestStatus>());

        await _sut.SaveDraft(WindowId, pageId: "page-2");

        Assert.NotNull(capturedJourney);
        Assert.True(capturedJourney.QuestionAnswers.ContainsKey("q2"));
        Assert.Equal("My explanation", capturedJourney.QuestionAnswers["q2"].TextValue);
    }

    [Fact]
    public async Task SaveDraft_WithPageId_DoesNotSaveBlankFormAnswers()
    {
        SetupSession(ValidSession());
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_q2"] = "   " // whitespace only
        });

        RequestState? capturedJourney = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s), Arg.Any<RequestStatus>());

        await _sut.SaveDraft(WindowId, pageId: "page-2");

        Assert.NotNull(capturedJourney);
        Assert.False(capturedJourney.QuestionAnswers.ContainsKey("q2"));
    }

    // ── SaveDraft — origin country language capture (PBI 292266 review fix) ──

    private static readonly JourneyPage CountryPage = new()
    {
        Id = "country-page",
        Questions = [new Question { Id = "country-originally-from", Type = QuestionType.Autocomplete, Title = "Country" }]
    };

    [Fact]
    public async Task SaveDraft_WithPageId_InvokesLanguageCapture_WithCapturedAnswers()
    {
        // Save-and-exit re-reads the form the same way PagePost does, so it must run the
        // same backfill — otherwise a re-POST with the autocomplete's hidden _code field
        // empty (the normal state of a re-rendered page) persists a null CodeValue.
        SetupSession(ValidSession(history: ["country-page"]));
        _flowService.GetPage(Config, "country-page").Returns(CountryPage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_country_originally_from"] = "France"
        });

        await _sut.SaveDraft(WindowId, pageId: "country-page");

        await _languageCapture.Received(1).ApplyAsync(
            Arg.Any<RequestState>(),
            Arg.Is<IReadOnlyDictionary<string, QuestionAnswer>>(a =>
                a.ContainsKey("country-originally-from") && a["country-originally-from"].TextValue == "France"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraft_WithPageId_PersistsBackfilledCodeValueAndOriginCountryState()
    {
        // ApplyAsync mutates the answer (backfills CodeValue) and the journey
        // (OriginCountryCode / OriginCountryLanguages) in place — SaveDraft must persist
        // those mutations to session and pass them on to SaveDraftAsync, not the stale
        // pre-capture values it read from the form.
        SetupSession(ValidSession(history: ["country-page"]));
        _flowService.GetPage(Config, "country-page").Returns(CountryPage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_country_originally_from"] = "France"
        });
        _languageCapture.ApplyAsync(Arg.Any<RequestState>(),
            Arg.Do<IReadOnlyDictionary<string, QuestionAnswer>>(a => a["country-originally-from"].CodeValue = "FR"),
            Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var journey = ci.Arg<RequestState>();
                journey.OriginCountryCode = "FR";
                journey.OriginCountryLanguages = ["French"];
                return Task.CompletedTask;
            });

        RequestState? capturedJourney = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s), Arg.Any<RequestStatus>());

        await _sut.SaveDraft(WindowId, pageId: "country-page");

        Assert.NotNull(capturedJourney);
        Assert.Equal("FR", capturedJourney.QuestionAnswers["country-originally-from"].CodeValue);
        Assert.Equal("FR", capturedJourney.OriginCountryCode);
        Assert.Equal(["French"], capturedJourney.OriginCountryLanguages);
    }

    [Fact]
    public async Task SaveDraft_WithPageId_BlankCountryField_DoesNotOverwriteExistingAnswerOrOriginCountryState()
    {
        // A blank re-POST (e.g. the user cleared the field then hit Save and exit) must not
        // clobber the previously saved answer or null out the already-resolved origin
        // country state — matching the existing skip-blank semantics for every other question.
        var state = ValidSession(history: ["country-page"], answers: new Dictionary<string, QuestionAnswer>
        {
            ["country-originally-from"] = new() { TextValue = "France", CodeValue = "FR" }
        });
        state.OriginCountryCode = "FR";
        state.OriginCountryLanguages = ["French"];
        SetupSession(state);
        _flowService.GetPage(Config, "country-page").Returns(CountryPage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_country_originally_from"] = "   " // whitespace only
        });

        RequestState? capturedJourney = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s), Arg.Any<RequestStatus>());

        await _sut.SaveDraft(WindowId, pageId: "country-page");

        Assert.NotNull(capturedJourney);
        Assert.Equal("France", capturedJourney.QuestionAnswers["country-originally-from"].TextValue);
        Assert.Equal("FR", capturedJourney.QuestionAnswers["country-originally-from"].CodeValue);
        Assert.Equal("FR", capturedJourney.OriginCountryCode);
        Assert.Equal(["French"], capturedJourney.OriginCountryLanguages);
    }

    [Fact]
    public async Task SaveDraft_WhenNotBulkEdit_RedirectsToAmendmentRequestsIndex()
    {
        SetupSession(ValidSession());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        AssertRedirectToAmendmentRequests(result);
    }

    [Fact]
    public async Task SaveDraft_WhenBulkEdit_RedirectsToBulkDetailedReview()
    {
        SetupSession(ValidSession());
        _session.SetBulkEditMode(WindowId);

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("AmendmentRequests", redirect.ControllerName);
        Assert.Equal("BulkReviewDetailedPage", redirect.ActionName);
    }

    [Fact]
    public async Task SaveDraft_ClearsSessionAfterSaving()
    {
        SetupSession(ValidSession());

        await _sut.SaveDraft(WindowId, pageId: null);

        var remaining = _session.GetRequestState(WindowId);
        Assert.Null(remaining.SelectedWhatToChange);
        Assert.Null(remaining.CheckingWindow);
        Assert.Null(remaining.ReferenceNumber);
    }

    [Fact]
    public async Task SaveDraft_WithoutPageId_PassesReadyToSubmitStatus()
    {
        SetupSession(ValidSession());
        RequestStatus? capturedStatus = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Any<RequestState>(),
            Arg.Do<RequestStatus>(s => capturedStatus = s));

        await _sut.SaveDraft(WindowId, pageId: null);

        Assert.Equal(RequestStatus.ReadyToSubmit, capturedStatus);
    }

    [Fact]
    public async Task SaveDraft_WithPageId_PassesInProgressStatus()
    {
        SetupSession(ValidSession());
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        RequestStatus? capturedStatus = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Any<RequestState>(),
            Arg.Do<RequestStatus>(s => capturedStatus = s));

        await _sut.SaveDraft(WindowId, pageId: "page-2");

        Assert.Equal(RequestStatus.InProgress, capturedStatus);
    }

    [Fact]
    public async Task SaveDraft_EvidenceUploadPage_WithFiles_PassesReadyToSubmitStatus()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new() { FileValues = [new FileAnswer { StoredFileName = "file.pdf", OriginalFileName = "evidence.pdf", PageCount = 1, FileSizeBytes = 1024 }] }
        };
        SetupSession(ValidSession(answers: answers));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        RequestStatus? capturedStatus = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Any<RequestState>(),
            Arg.Do<RequestStatus>(s => capturedStatus = s));

        await _sut.SaveDraft(WindowId, pageId: "evidence-page");

        Assert.Equal(RequestStatus.ReadyToSubmit, capturedStatus);
    }

    [Fact]
    public async Task SaveDraft_EvidenceUploadPage_WithoutFiles_PassesInProgressStatus()
    {
        SetupSession(ValidSession());
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<string>?>())
            .Returns(new EvidenceValidationResult { Messages = ["Upload at least one file before continuing"] });
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        RequestStatus? capturedStatus = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Any<RequestState>(),
            Arg.Do<RequestStatus>(s => capturedStatus = s));

        await _sut.SaveDraft(WindowId, pageId: "evidence-page");

        Assert.Equal(RequestStatus.InProgress, capturedStatus);
    }

    [Fact]
    public async Task SaveDraft_EvidenceUploadPage_WhenReadyToSubmit_AppendsPageToHistory()
    {
        // Mirrors "Save and continue": a completed (ReadyToSubmit) evidence page must be
        // recorded in history so that resuming the request lands on the Summary rather than
        // being bounced back to the evidence page by the Summary navigation guard.
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new() { FileValues = [new FileAnswer { StoredFileName = "file.pdf", OriginalFileName = "evidence.pdf", PageCount = 1, FileSizeBytes = 1024 }] }
        };
        SetupSession(ValidSession(history: ["select-pupil", "select-match-pupil"], answers: answers));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        RequestState? capturedJourney = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s), Arg.Any<RequestStatus>());

        await _sut.SaveDraft(WindowId, pageId: "evidence-page");

        Assert.Equal(["select-pupil", "select-match-pupil", "evidence-page"], capturedJourney!.QuestionHistory);
    }

    [Fact]
    public async Task SaveDraft_EvidenceUploadPage_WhenInProgress_DoesNotAppendPageToHistory()
    {
        // An InProgress (invalid) evidence page must NOT be added to history so the request
        // resumes on the evidence page rather than skipping past it to the Summary.
        SetupSession(ValidSession(history: ["select-pupil", "select-match-pupil"]));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<string>?>())
            .Returns(new EvidenceValidationResult { Messages = ["Upload at least one file before continuing"] });
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        RequestState? capturedJourney = null;
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s), Arg.Any<RequestStatus>());

        await _sut.SaveDraft(WindowId, pageId: "evidence-page");

        Assert.Equal(["select-pupil", "select-match-pupil"], capturedJourney!.QuestionHistory);
    }

    // ── UploadFile / RemoveFile — preserve text answers ──────────────────────

    [Fact]
    public async Task UploadFile_PreservesSubmittedTextAnswers()
    {
        // The evidence text box lives in the same page as the upload control. Submitting an
        // upload (even one that fails validation, e.g. no file selected) must not discard text
        // the user has already typed.
        SetupSession(ValidSession());
        _flowService.GetPage(Config, "evidence-with-text").Returns(EvidenceWithTextPage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_explanation"] = "My evidence explanation"
        });

        await _sut.UploadFile(WindowId, "evidence-with-text", "evidence", fromSummary: false, fileUpload: null);

        var saved = _session.GetRequestState(WindowId).QuestionAnswers;
        Assert.Equal("My evidence explanation", saved["explanation"].TextValue);
    }

    [Fact]
    public async Task Page_WhenUploadErrorInTempData_SurfacesItInViewModel()
    {
        // The "Upload file" button posts to UploadFile, which on a rejected file stores the
        // message in TempData and redirects (PRG) to Page GET. Page GET must surface that
        // message — otherwise a rejected upload (e.g. a non-PDF) silently shows nothing.
        SetupSession(ValidSession(history: ["evidence-page"]));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        const string message = "Evidence must be in a PDF format.";
        _sut.TempData["UploadError"] = message;

        var result = await _sut.Page(WindowId, "evidence-page");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PageViewModel>(view.Model);
        Assert.Equal(message, model.UploadError);
    }

    [Fact]
    public async Task RemoveFile_PreservesSubmittedTextAnswers()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new() { FileValues = [new FileAnswer { StoredFileName = "stored.pdf", OriginalFileName = "e.pdf", PageCount = 1, FileSizeBytes = 10 }] }
        };
        SetupSession(ValidSession(answers: answers));
        _flowService.GetPage(Config, "evidence-with-text").Returns(EvidenceWithTextPage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_explanation"] = "Keep me"
        });

        await _sut.RemoveFile(WindowId, "evidence-with-text", "evidence", fromSummary: false, storedFileName: "stored.pdf");

        var saved = _session.GetRequestState(WindowId).QuestionAnswers;
        Assert.Equal("Keep me", saved["explanation"].TextValue);
        Assert.Empty(saved["evidence"].FileValues!);
    }

    // ── PagePost — commit a pending file selection on Continue ───────────────

    [Fact]
    public async Task PagePost_WithPendingFile_CommitsItAndAdvances()
    {
        // The reported defect: a user selects a file in Browse, types text, and clicks
        // Continue without clicking "Upload file". Continue must now commit the selected
        // file rather than reporting "upload a file".
        SetupSession(ValidSession(history: ["evidence-page"]));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _flowService.GetNextPageId(Config, "evidence-page", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        _fileStorageService.SaveAsync(WindowId, Arg.Any<byte[]>()).Returns("stored-123.pdf");
        // NSubstitute returns string.Empty (not null) by default — the real service returns null
        // for "no error", so stub it to match.
        _journeyService.ValidateFileUpload(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<FileAnswer>>())
            .Returns((string?)null);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-page", fromSummary: false,
            fileUpload: FakePdfFile(ValidPdfBytes(), "evidence.pdf"));

        await _fileStorageService.Received(1).SaveAsync(WindowId, Arg.Any<byte[]>());
        var saved = _session.GetRequestState(WindowId).QuestionAnswers["evidence"].FileValues!;
        Assert.Single(saved);
        Assert.Equal("evidence.pdf", saved[0].OriginalFileName);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(JourneyController.Summary), redirect.ActionName);
    }

    [Fact]
    public async Task PagePost_WithInvalidPendingFile_ReRendersAndDoesNotAdvance()
    {
        // A pending file that is not a readable PDF must surface an error and keep the user
        // on the page — the same validation the "Upload file" button applies.
        SetupSession(ValidSession(history: ["evidence-page"]));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-page", fromSummary: false,
            fileUpload: FakePdfFile(Encoding.UTF8.GetBytes("not a pdf"), "junk.pdf"));

        await _fileStorageService.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("EvidenceUpload", view.ViewName);
    }

    [Fact]
    public async Task PagePost_WithDuplicatePendingFile_ReRendersAndDoesNotStore()
    {
        // AB#296081 review finding: the duplicate check lives inside CommitUploadedFileAsync
        // and therefore covers the Continue-with-staged-file path (Browse → Continue without
        // clicking "Upload file"), but nothing pinned that. This guards against a future
        // "keep validation in the action" refactor moving the check into UploadFile only —
        // which would let a staged duplicate slip through Continue and store two files with
        // the same name.
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new()
            {
                FileValues = [new FileAnswer { StoredFileName = "s1", OriginalFileName = "evidence.pdf", PageCount = 1, FileSizeBytes = 10 }]
            }
        };
        SetupSession(ValidSession(history: ["evidence-page"], answers: answers));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _journeyService.ValidateDuplicateFileName("evidence.pdf", Arg.Any<IReadOnlyList<FileAnswer>>())
            .Returns("The file name has already been used. Upload a file with a different name.");
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-page", fromSummary: false,
            fileUpload: FakePdfFile(ValidPdfBytes(), "evidence.pdf"));

        await _fileStorageService.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceUploadAttemptedEvent>(e => e.Outcome == "failed" && e.FailureReason == "duplicate_name"),
            Arg.Any<CancellationToken>());
        var saved = _session.GetRequestState(WindowId).QuestionAnswers["evidence"].FileValues!;
        Assert.Single(saved);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("EvidenceUpload", view.ViewName);
    }

    [Fact]
    public async Task PagePost_NoPendingFileButFileAlreadyUploaded_Advances()
    {
        // Regression: existing uploaded files still satisfy the page without a pending file.
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new() { FileValues = [new FileAnswer { StoredFileName = "f.pdf", OriginalFileName = "e.pdf", PageCount = 1, FileSizeBytes = 10 }] }
        };
        SetupSession(ValidSession(history: ["evidence-page"], answers: answers));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _flowService.GetNextPageId(Config, "evidence-page", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-page", fromSummary: false, fileUpload: null);

        await _fileStorageService.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(JourneyController.Summary), redirect.ActionName);
    }

    [Fact]
    public async Task PagePost_NoPendingFileAndNothingUploaded_ShowsUploadError()
    {
        // Regression: the original "upload at least one file" guard still fires when there is
        // genuinely no file (neither pending nor previously uploaded).
        SetupSession(ValidSession(history: ["evidence-page"]));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-page", fromSummary: false, fileUpload: null);

        await _fileStorageService.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("EvidenceUpload", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("evidence"));
    }

    [Fact]
    public async Task PagePost_EvidenceMandatory_WhenNotConditionallyOptional_ErrorsOnEmpty()
    {
        // Fixture default: _optionalityService returns an empty conditionally-optional set,
        // so both the FileUpload and TextArea questions on the shared evidence page stay
        // mandatory (PBI 292266 — the common case, no EAL auto-reject predicted).
        SetupSession(ValidSession(history: ["evidence-with-text"]));
        _flowService.GetPage(Config, "evidence-with-text").Returns(EvidenceWithTextPage);
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns("Enter something");
        _journeyService.IsAnswered(Arg.Any<Question>(), Arg.Any<QuestionAnswer>()).Returns(false);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-with-text", fromSummary: false, fileUpload: null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("EvidenceUpload", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("evidence"));
        Assert.True(_sut.ModelState.ContainsKey("explanation"));
    }

    [Fact]
    public async Task PagePost_EvidenceOptional_WhenConditionallyOptional_ContinuesOnEmpty()
    {
        // When the EalWouldBeAutoRejected condition (approximated via the mocked
        // optionality service here) says the request would be auto-rejected, both
        // evidence questions become optional and an empty submission proceeds.
        _optionalityService.GetConditionallyOptionalQuestionIds(EvidenceWithTextPage, Arg.Any<JourneyConditionContext>())
            .Returns(new HashSet<string> { "evidence", "explanation" });
        SetupSession(ValidSession(history: ["evidence-with-text"]));
        _flowService.GetPage(Config, "evidence-with-text").Returns(EvidenceWithTextPage);
        _flowService.GetNextPageId(Config, "evidence-with-text", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>());

        var result = await _sut.PagePost(WindowId, "evidence-with-text", fromSummary: false, fileUpload: null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(_sut.ModelState.IsValid);
    }

    [Fact]
    public async Task PagePost_InvokesLanguageCapture_WithPostedAnswers()
    {
        // The capture hook runs after every successful page validation (not just the
        // country details page) — OriginCountryLanguageCapture itself is a no-op unless
        // the posted answers contain the country question, and that's exercised in
        // OriginCountryLanguageCaptureTests. Here we just confirm PagePost wires it in.
        SetupSession(ValidSession(history: ["page-1"]));
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((string?)null);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_q2"] = "My explanation"
        });

        var result = await _sut.PagePost(WindowId, "page-2", fromSummary: false);

        Assert.IsType<RedirectToActionResult>(result);
        await _languageCapture.Received(1).ApplyAsync(Arg.Any<RequestState>(),
            Arg.Any<IReadOnlyDictionary<string, QuestionAnswer>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagePost_StrayFileOnPageWithoutFileUpload_IsIgnored()
    {
        // A page with no FileUpload question must ignore any stray file field and behave normally.
        SetupSession(ValidSession(history: ["page-2"]));
        _journeyService.ValidateAnswer(Arg.Any<Question>(), Arg.Any<QuestionAnswer>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns((string?)null);
        _httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["q_q2"] = "My explanation"
        });

        var result = await _sut.PagePost(WindowId, "page-2", fromSummary: false,
            fileUpload: FakePdfFile(ValidPdfBytes(), "evidence.pdf"));

        await _fileStorageService.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
        Assert.IsType<RedirectToActionResult>(result);
    }

    // ── DownloadEvidence ─────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadEvidence_WhenFileInSession_ReturnsFileResult()
    {
        var storedName = Guid.NewGuid().ToString();
        const string originalName = "evidence.pdf";
        var bytes = new byte[] { 1, 2, 3 };

        var state = ValidSession(answers: new Dictionary<string, QuestionAnswer>
        {
            ["q1"] = new() { FileValues = [new FileAnswer { StoredFileName = storedName, OriginalFileName = originalName, PageCount = 2, FileSizeBytes = 1024 }] }
        });
        SetupSession(state);
        _fileStorageService.GetAsync(WindowId, storedName).Returns(bytes);

        var result = await _sut.DownloadEvidence(WindowId, storedName);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(originalName, file.FileDownloadName);
    }

    [Fact]
    public async Task DownloadEvidence_WhenFileNotInSession_ReturnsNotFound()
    {
        SetupSession(ValidSession(answers: new Dictionary<string, QuestionAnswer>()));

        var result = await _sut.DownloadEvidence(WindowId, "unknown-file");

        Assert.IsType<NotFoundResult>(result);
        await _fileStorageService.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DownloadEvidence_WhenNoSession_ReturnsNotFound()
    {
        SetupSession(new RequestState());  // empty — no WhatToChange, no window, no pupil

        var result = await _sut.DownloadEvidence(WindowId, "any-file");

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Evidence analytics ───────────────────────────────────────────────────

    [Fact]
    public async Task UploadFile_WhenNoFile_EmitsFailedNoFile()
    {
        SetupSession(ValidSession());

        await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, fileUpload: null);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceUploadAttemptedEvent>(e => e.Outcome == "failed" && e.FailureReason == "no_file"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadFile_WhenTooLarge_EmitsFailedTooLarge()
    {
        SetupSession(ValidSession());
        var file = new FormFile(new MemoryStream([1]), 0, 11L * 1024 * 1024, "fileUpload", "big.pdf");

        await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, file);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceUploadAttemptedEvent>(e => e.Outcome == "failed" && e.FailureReason == "too_large"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadFile_WhenNotPdf_EmitsFailedNotAPdf()
    {
        SetupSession(ValidSession());
        var bytes = new byte[] { 1, 2, 3 };
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "fileUpload", "notpdf.pdf");

        await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, file);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceUploadAttemptedEvent>(e => e.Outcome == "failed" && e.FailureReason == "not_a_pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadFile_WhenValid_EmitsSuccessWithMetrics()
    {
        SetupSession(ValidSession());
        var bytes = MinimalPdf();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "fileUpload", "evidence.pdf");
        _fileStorageService.SaveAsync(WindowId, Arg.Any<byte[]>()).Returns("stored-guid");
        // NSubstitute returns "" for string by default; null means "no upload error".
        _journeyService.ValidateFileUpload(default!, 0, default!).ReturnsForAnyArgs((string?)null);

        await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, file);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceUploadAttemptedEvent>(e =>
                e.Outcome == "success" && e.PageCount == 1 && e.FileSizeBytes == bytes.Length),
            Arg.Any<CancellationToken>());
    }

    // AB#296081: a file whose name is already used anywhere in the request must be
    // rejected at the Upload action — before any bytes are stored — with the exact
    // ticket wording, and recorded in analytics as duplicate_name (never the file name).

    [Fact]
    public async Task UploadFile_WhenFileNameAlreadyUsed_SetsErrorAndDoesNotStore()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new()
            {
                FileValues = [new FileAnswer { StoredFileName = "s1", OriginalFileName = "evidence.pdf", PageCount = 1, FileSizeBytes = 10 }]
            }
        };
        SetupSession(ValidSession(answers: answers));
        var bytes = MinimalPdf();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "fileUpload", "evidence.pdf");
        _journeyService.ValidateDuplicateFileName("evidence.pdf", Arg.Any<IReadOnlyList<FileAnswer>>())
            .Returns("The file name has already been used. Upload a file with a different name.");

        var result = await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, file);

        Assert.Equal(
            "The file name has already been used. Upload a file with a different name.",
            _sut.TempData["UploadError"]);
        await _fileStorageService.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceUploadAttemptedEvent>(e => e.Outcome == "failed" && e.FailureReason == "duplicate_name"),
            Arg.Any<CancellationToken>());
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
    }

    [Fact]
    public async Task UploadFile_ChecksNamesAcrossEveryQuestionInTheRequest()
    {
        // Uniqueness is per amendment request (ticket scope A), not per question: the
        // already-used name lives under a DIFFERENT question id than the one uploaded to,
        // and must still reach the validator.
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["other-question"] = new()
            {
                FileValues = [new FileAnswer { StoredFileName = "s1", OriginalFileName = "evidence.pdf", PageCount = 1, FileSizeBytes = 10 }]
            }
        };
        SetupSession(ValidSession(answers: answers));
        var bytes = MinimalPdf();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "fileUpload", "new.pdf");

        await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, file);

        _journeyService.Received(1).ValidateDuplicateFileName("new.pdf",
            Arg.Is<IReadOnlyList<FileAnswer>>(files => files.Any(f => f.OriginalFileName == "evidence.pdf")));
    }

    [Fact]
    public async Task UploadFile_WhenFileNameIsNew_StoresTheFile()
    {
        SetupSession(ValidSession());
        var bytes = MinimalPdf();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "fileUpload", "new.pdf");
        _fileStorageService.SaveAsync(WindowId, Arg.Any<byte[]>()).Returns("stored-guid");
        _journeyService.ValidateFileUpload(default!, 0, default!).ReturnsForAnyArgs((string?)null);

        await _sut.UploadFile(WindowId, "evidence-page", "evidence", fromSummary: false, file);

        await _fileStorageService.Received(1).SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>());
    }

    [Fact]
    public async Task RemoveFile_AfterRemove_EmitsEvidenceFileRemoved()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new()
            {
                FileValues =
                [
                    new FileAnswer { StoredFileName = "keep", OriginalFileName = "a.pdf", PageCount = 1, FileSizeBytes = 10 },
                    new FileAnswer { StoredFileName = "drop", OriginalFileName = "b.pdf", PageCount = 1, FileSizeBytes = 10 }
                ]
            }
        };
        SetupSession(ValidSession(answers: answers));

        await _sut.RemoveFile(WindowId, "evidence-page", "evidence", fromSummary: false, storedFileName: "drop");

        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceFileRemovedEvent>(e => e.FilesBefore == 2 && e.FilesAfter == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagePost_EvidencePage_WhenValid_EmitsEvidenceContinue()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new()
            {
                FileValues = [new FileAnswer { StoredFileName = "f", OriginalFileName = "e.pdf", PageCount = 2, FileSizeBytes = 100 }]
            }
        };
        SetupSession(ValidSession(history: ["evidence-page"], answers: answers));
        _flowService.GetPage(Config, "evidence-page").Returns(EvidencePage);
        _flowService.GetNextPageId(Config, "evidence-page", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);

        await _sut.PagePost(WindowId, "evidence-page", fromSummary: false);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<EvidenceContinueEvent>(e => e.FileCount == 1 && e.PageCount == 2 && e.EvidenceTextLength == 0),
            Arg.Any<CancellationToken>());
    }

    private static byte[] MinimalPdf()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        return builder.Build();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetupSession(RequestState state)
    {
        var json = JsonSerializer.Serialize(state);
        _session.Set($"request_{WindowId}", Encoding.UTF8.GetBytes(json));
    }

    private static RequestState ValidSession(
        List<string>? history = null,
        Dictionary<string, QuestionAnswer>? answers = null)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = new CheckingWindowDto
            {
                Id = Guid.NewGuid(),
                Title = "Test Window",
                KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(13)
            },
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
            QuestionHistory = history ?? [],
            QuestionAnswers = answers ?? new(),
            SelectedPupil = new PupilDto
            {
                Id = Guid.NewGuid(),
                Firstname = "Jane",
                Surname = "Smith",
                Sex = "F",
                DateOfBirth = "01/01/2010",
                Age = 16,
                Cypmd_Id = "CYPMD123",
                Identifier = "123123"
            }
        };
        return state;
    }

    // A minimal real one-page PDF, so PdfPageCounter (a static parser, not mockable) accepts it.
    private static byte[] ValidPdfBytes()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        return builder.Build();
    }

    private static IFormFile FakePdfFile(byte[] bytes, string fileName)
    {
        var file = Substitute.For<IFormFile>();
        file.Length.Returns(bytes.LongLength);
        file.FileName.Returns(fileName);
        file.When(f => f.CopyToAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()))
            .Do(ci => ((Stream)ci[0]).Write(bytes, 0, bytes.Length));
        return file;
    }

    private static void AssertRedirectToCheckYourData(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

    private static void AssertRedirectToAmendmentRequests(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("AmendmentRequests", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

    // ── Infrastructure ───────────────────────────────────────────────────────

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool TryGetValue(string key, out byte[] value) =>
            _store.TryGetValue(key, out value!);

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

    // ── #318: the closed-exercise gate ───────────────────────────────────────

    [Fact]
    public async Task Page_WhenTheJourneysExerciseHasClosed_RedirectsToCheckYourPupilDataWithAMessage()
    {
        SetupSession(ValidSession(history: ["page-1"]));
        _checkingExercises.Close();

        var result = await _sut.Page(WindowId, "page-1");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal(
            ClosedExerciseGuard.MessageFor(CheckingExerciseType.PupilData),
            _sut.TempData[ClosedExerciseGuard.TempDataKey]);
    }

    [Fact]
    public async Task PagePost_WhenTheJourneysExerciseHasClosed_SavesNothing()
    {
        // The whole point of the gate: a tab left open across the closing date still posts.
        SetupSession(ValidSession(history: ["page-1"]));
        _checkingExercises.Close();

        var result = await _sut.PagePost(WindowId, "page-1", fromSummary: false, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(_session.GetRequestState(WindowId).QuestionAnswers);
    }

    [Fact]
    public async Task SummaryConfirm_WhenTheJourneysExerciseHasClosed_SubmitsNothing()
    {
        SetupSession(ValidSession(history: ["page-1", "page-2"]));
        _checkingExercises.Close();

        var result = await _sut.SummaryConfirm(WindowId);

        Assert.IsType<RedirectToActionResult>(result);
        await _requestService.DidNotReceiveWithAnyArgs().ConfirmRequestAsync(default, default!);
    }

    [Fact]
    public async Task SaveDraft_WhenTheJourneysExerciseHasClosed_SavesNothing()
    {
        SetupSession(ValidSession(history: ["page-1"]));
        _checkingExercises.Close();

        var result = await _sut.SaveDraft(WindowId, "page-1");

        Assert.IsType<RedirectToActionResult>(result);
        await _requestService.DidNotReceiveWithAnyArgs().SaveDraftAsync(default, default!, default);
    }

    [Fact]
    public async Task DownloadEvidence_WhenTheJourneysExerciseHasClosed_RedirectsRatherThan404s()
    {
        // AC: no gated path returns 404. This action used to answer NotFound for any unready
        // session, which would have made a closed exercise look like a broken link.
        SetupSession(ValidSession(history: ["page-1"]));
        _checkingExercises.Close();

        var result = await _sut.DownloadEvidence(WindowId, Guid.NewGuid().ToString());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
    }

    [Fact]
    public async Task Page_WhenOnlyTheOtherExerciseIsOpen_IsStillRejected()
    {
        // A pupil-data journey on a window whose results-enquiry exercise is still running. The
        // gate follows the journey's own exercise, derived from SelectedWhatToChange.
        SetupSession(ValidSession(history: ["page-1"]));
        _checkingExercises.IsOpen(default!, default)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseType>(1) == CheckingExerciseType.ResultsEnquiry);

        var result = await _sut.Page(WindowId, "page-1");

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Page_WhenTheSessionIsNotStarted_RedirectsWithoutAClosedExerciseMessage()
    {
        // No journey means nothing to explain — the banner must not claim a deadline has passed
        // to someone who never started.
        SetupSession(new RequestState());

        var result = await _sut.Page(WindowId, "page-1");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(_sut.TempData.ContainsKey(ClosedExerciseGuard.TempDataKey));
    }

}
