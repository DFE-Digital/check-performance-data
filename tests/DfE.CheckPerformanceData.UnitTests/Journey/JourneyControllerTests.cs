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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class JourneyControllerTests
{
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyService _journeyService = Substitute.For<IJourneyService>();
    private readonly IFileStorageService _fileStorageService = Substitute.For<IFileStorageService>();
    private readonly IRequestBlobClient _requestBlobClient = Substitute.For<IRequestBlobClient>();
    private readonly IWebHostEnvironment _env = Substitute.For<IWebHostEnvironment>();
    private readonly FakeSession _session = new();
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

        _flowService.GetConfig(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Config);
        _flowService.GetPage(Config, "page-1").Returns(Config.Pages[0]);
        _flowService.GetPage(Config, "page-2").Returns(Config.Pages[1]);
        _flowService.GetNextPageId(Config, "page-1", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns("page-2");
        _flowService.GetNextPageId(Config, "page-2", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new JourneyController(_flowService, _journeyService, _fileStorageService, _requestBlobClient, _env)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    // ── Session guard — Page GET ─────────────────────────────────────────────

    [Fact]
    public void Page_WhenNoSession_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());  // empty — no WhatToChange, no window, no pupil

        var result = _sut.Page(WindowId, "page-1");

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public void Page_WhenPupilNotSelected_RedirectsToCheckYourData()
    {
        var state = ValidSession();
        state.SelectedPupil = null;
        SetupSession(state);

        var result = _sut.Page(WindowId, "page-1");

        AssertRedirectToCheckYourData(result);
    }

    // ── Session guard — Summary GET ──────────────────────────────────────────

    [Fact]
    public void Summary_WhenNoSession_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = _sut.Summary(WindowId);

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public void Summary_WhenPupilNotSelected_RedirectsToCheckYourData()
    {
        var state = ValidSession();
        state.SelectedPupil = null;
        SetupSession(state);

        var result = _sut.Summary(WindowId);

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
    public void Page_WhenHistoryEmptyAndRequestingFirstPage_AllowsAccess()
    {
        SetupSession(ValidSession(history: []));

        var result = _sut.Page(WindowId, "page-1");

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public void Page_WhenPageAlreadyInHistory_AllowsAccess()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        var result = _sut.Page(WindowId, "page-1");

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public void Page_WhenPageIsExpectedNextAfterHistory_AllowsAccess()
    {
        SetupSession(ValidSession(history: ["page-1"]));

        var result = _sut.Page(WindowId, "page-2");

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public void Page_WhenSkippingAhead_RedirectsToExpectedNextPage()
    {
        // History is empty — expected next is page-1; user tries to go to page-2
        SetupSession(ValidSession(history: []));

        var result = _sut.Page(WindowId, "page-2");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("page-1", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public void Page_WhenJourneyCompleteAndRequestingUnvisitedPage_RedirectsToSummary()
    {
        // Journey is complete (history has both pages, GetNextPageId returns null after page-2)
        // User tries to navigate to an arbitrary page that isn't in history
        SetupSession(ValidSession(history: ["page-1", "page-2"]));
        _flowService.GetPage(Config, "page-99").Returns((JourneyPage?)null);

        // We need a page that exists in the config but isn't in history.
        // Since GetNextPageId("page-2") returns null, journey is complete.
        // Visiting any new page should redirect to Summary.
        // Use page-1 with a modified config that has a third page to simulate:
        // Actually, let's use a different approach - add a page-3 temporarily
        var extendedConfig = new QuestionFlowConfig
        {
            FirstPageId = "page-1",
            Pages = Config.Pages.Append(new JourneyPage { Id = "page-3", Questions = [] }).ToList()
        };
        _flowService.GetConfig(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(extendedConfig);
        _flowService.GetPage(extendedConfig, "page-3").Returns(extendedConfig.Pages[2]);
        _flowService.GetPage(extendedConfig, "page-1").Returns(extendedConfig.Pages[0]);
        _flowService.GetPage(extendedConfig, "page-2").Returns(extendedConfig.Pages[1]);
        _flowService.GetNextPageId(extendedConfig, "page-2", Arg.Any<Dictionary<string, QuestionAnswer>>()).Returns((string?)null);

        var result = _sut.Page(WindowId, "page-3");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ActionName);
    }

    // ── Summary completeness ─────────────────────────────────────────────────

    [Fact]
    public void Summary_WhenHistoryEmpty_RedirectsToFirstPage()
    {
        SetupSession(ValidSession(history: []));

        var result = _sut.Summary(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("page-1", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public void Summary_WhenJourneyIncomplete_RedirectsToNextPage()
    {
        // Only page-1 answered; GetNextPageId returns page-2 (not done yet)
        SetupSession(ValidSession(history: ["page-1"]));

        var result = _sut.Summary(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("page-2", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public void Summary_WhenJourneyComplete_RendersView()
    {
        // Both pages answered; GetNextPageId after page-2 returns null
        SetupSession(ValidSession(history: ["page-1", "page-2"]));

        var result = _sut.Summary(WindowId);

        Assert.IsType<ViewResult>(result);
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
            Cypmd_Id = "CYPMD123"
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
