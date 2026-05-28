using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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
    private readonly IWebHostEnvironment _env = Substitute.For<IWebHostEnvironment>();
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
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

    public JourneyControllerTests()
    {
        _env.EnvironmentName.Returns("Production");

        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Config);
        _flowService.GetPage(Config, "page-1").Returns(Config.Pages[0]);
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]);
        _flowService.GetNextPageId(Config, "page-1", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns("page-2");
        _flowService.GetNextPageId(Config, "page-2", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new JourneyController(_flowService, _journeyService, _fileStorageService, _requestService, _env)
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
    public async Task Page_WhenPupilNotSelected_RedirectsToCheckYourData()
    {
        var state = ValidSession();
        state.SelectedPupil = null;
        SetupSession(state);

        var result = await _sut.Page(WindowId, "page-1");

        AssertRedirectToCheckYourData(result);
    }

    // ── Session guard — Summary GET ──────────────────────────────────────────

    [Fact]
    public async Task Summary_WhenNoSession_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = await _sut.Summary(WindowId);

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public async Task Summary_WhenPupilNotSelected_RedirectsToCheckYourData()
    {
        var state = ValidSession();
        state.SelectedPupil = null;
        SetupSession(state);

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

    // ── SaveDraft ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveDraft_WhenSessionNotReady_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public async Task SaveDraft_WithoutPageId_CallsServiceAndRedirectsToCheckYourData()
    {
        SetupSession(ValidSession());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        await _requestService.Received(1).SaveDraftAsync(WindowId, Arg.Any<RequestState>());
        AssertRedirectToCheckYourData(result);
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
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s));

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
        await _requestService.SaveDraftAsync(WindowId, Arg.Do<RequestState>(s => capturedJourney = s));

        await _sut.SaveDraft(WindowId, pageId: "page-2");

        Assert.NotNull(capturedJourney);
        Assert.False(capturedJourney.QuestionAnswers.ContainsKey("q2"));
    }

    [Fact]
    public async Task SaveDraft_AlwaysRedirectsToCheckYourData()
    {
        SetupSession(ValidSession());

        var result = await _sut.SaveDraft(WindowId, pageId: null);

        AssertRedirectToCheckYourData(result);
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

    private static void AssertRedirectToCheckYourData(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
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
