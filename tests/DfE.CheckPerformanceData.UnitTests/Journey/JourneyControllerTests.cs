using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class JourneyControllerTests
{
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IFileStorageService _fileStorageService = Substitute.For<IFileStorageService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IOptionVisibilityService _optionVisibilityService = Substitute.For<IOptionVisibilityService>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly IWebHostEnvironment _env = Substitute.For<IWebHostEnvironment>();
    private readonly ICheckYourPupilDataService _pupilDataService = Substitute.For<ICheckYourPupilDataService>();
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly JourneyController _sut;
    private readonly JourneyViewModelBuilder _viewModelBuilder;

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
        _env.EnvironmentName.Returns("Production");

        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Config);
        _flowService.GetPage(Config, "page-1").Returns(Config.Pages[0]);
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]);
        _flowService.GetNextPageId(Config, "page-1", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns("page-2");
        _flowService.GetNextPageId(Config, "page-2", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);

        _optionVisibilityService
            .GetVisibleOptions(Arg.Any<Question>(), Arg.Any<JourneyConditionContext>())
            .Returns(ci => ci.Arg<Question>().Options ?? (IReadOnlyList<QuestionOption>)[]);

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _viewModelBuilder = new JourneyViewModelBuilder(
            _flowService, _journeyService, _optionVisibilityService, _currentUserService, _env);

        _sut = new JourneyController(_flowService, _journeyService, _fileStorageService,
            _requestService, _pupilDataService, _viewModelBuilder)
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

    // ── SummaryConfirm ───────────────────────────────────────────────────────

    [Fact]
    public async Task SummaryConfirm_WhenDuplicateRequestException_ReturnsSummaryViewWithConflictError()
    {
        SetupSession(ValidSession(history: ["page-1"]));
        _requestService.ConfirmRequestAsync(WindowId, Arg.Any<RequestState>())
            .Returns<Task>(_ => throw new DuplicateRequestException());

        var result = await _sut.SummaryConfirm(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Summary", view.ViewName);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Equal("A request for this pupil has already been submitted. Select a different pupil.", vm.ConflictError);
    }

    [Fact]
    public async Task SummaryConfirm_WhenDuplicateRequestException_DoesNotClearSession()
    {
        var state = ValidSession(history: ["page-1"]);
        SetupSession(state);
        _requestService.ConfirmRequestAsync(WindowId, Arg.Any<RequestState>())
            .Returns<Task>(_ => throw new DuplicateRequestException());

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
        state.MatchedPupil = new PupilDto { Id = Guid.NewGuid(), Firstname = "John", Surname = "Doe", Sex = "M", DateOfBirth = "02/02/2010", Age = 16, Cypmd_Id = "CYPMD456", Upn = "456456" };
        state.MatchedPupilId = state.MatchedPupil.Id.ToString();
        state.MatchedPupilLabel = "Doe, John";
        SetupSession(state);

        await _sut.SummaryConfirm(WindowId);

        var remaining = _session.GetRequestState(WindowId);
        Assert.Null(remaining.MatchedPupil);
        Assert.Null(remaining.MatchedPupilId);
        Assert.Null(remaining.MatchedPupilLabel);
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

    // ── SaveDraft ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveDraft_WhenSessionNotReady_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        AssertRedirectToCheckYourData(result);
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

    [Fact]
    public async Task SaveDraft_AlwaysRedirectsToAmendmentRequests()
    {
        SetupSession(ValidSession());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        AssertRedirectToAmendmentRequests(result);
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
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>())
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
        _journeyService.ValidateEvidencePage(EvidencePage, Arg.Any<RequestState>(), Arg.Any<string>())
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
        const string message = "'photo.png' could not be read as a PDF. Check the file and try again.";
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
            QuestionAnswers = answers ?? new()
        };
        state.SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
            Firstname = "Jane",
            Surname = "Smith",
            Sex = "F",
            DateOfBirth = "01/01/2010",
            Age = 16,
            Cypmd_Id = "CYPMD123",
            Upn = "123123"
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
}
