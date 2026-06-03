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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class PupilSearchJourneyTests
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

    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PrimaryPupilId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MatchPupilId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly JourneyPage PrimarySearchPage = new()
    {
        Id = "select-pupil",
        Type = PageType.PupilSearch,
        Title = "Which pupil do you want to remove?",
        PupilFilter = PupilFilter.Included,
        PupilKey = JourneyPage.PrimaryKey,
        NextPageId = "reason"
    };

    private static readonly JourneyPage MatchSearchPage = new()
    {
        Id = "select-match-pupil",
        Type = PageType.PupilSearch,
        Title = "Which pupil should {pupilName} be merged with?",
        PupilFilter = PupilFilter.All,
        PupilKey = JourneyPage.MatchKey
        // NextPageId intentionally absent → redirects to summary
    };

    private static readonly JourneyPage QuestionPage = new()
    {
        Id = "reason",
        Questions = [new Question { Id = "q1", Type = QuestionType.Radio, Title = "Why?" }]
    };

    private static readonly QuestionFlowConfig MergeConfig = new()
    {
        FirstPageId = "select-pupil",
        Pages = [PrimarySearchPage, MatchSearchPage, QuestionPage]
    };

    private static readonly QuestionFlowConfig RemoveConfig = new()
    {
        FirstPageId = "select-pupil",
        Pages = [PrimarySearchPage, QuestionPage]
    };

    private static readonly PupilDto PrimaryPupil = new()
    {
        Id = PrimaryPupilId,
        Firstname = "Jane",
        Surname = "Smith",
        Sex = "F",
        DateOfBirth = "01/01/2010",
        Age = 16,
        Cypmd_Id = "CYPMD123",
        Upn = "UPN001"
    };

    private static readonly PupilDto MatchPupil = new()
    {
        Id = MatchPupilId,
        Firstname = "John",
        Surname = "Doe",
        Sex = "M",
        DateOfBirth = "02/02/2010",
        Age = 16,
        Cypmd_Id = "CYPMD456",
        Upn = "UPN002"
    };

    public PupilSearchJourneyTests()
    {
        _env.EnvironmentName.Returns("Production");
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(RemoveConfig);
        _flowService.GetPage(RemoveConfig, "select-pupil").Returns(PrimarySearchPage);
        _flowService.GetPage(RemoveConfig, "reason").Returns(QuestionPage);
        _flowService.GetPage(MergeConfig, "select-pupil").Returns(PrimarySearchPage);
        _flowService.GetPage(MergeConfig, "select-match-pupil").Returns(MatchSearchPage);
        _flowService.GetPage(MergeConfig, "reason").Returns(QuestionPage);
        _journeyService.GenerateReference(Arg.Any<CheckingWindowType?>()).Returns("CYPMD_KS4June_TEST01");

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        var viewModelBuilder = new JourneyViewModelBuilder(
            _flowService, _journeyService, _optionVisibilityService, _currentUserService, _env);

        _sut = new JourneyController(_flowService, _journeyService, _fileStorageService,
            _requestService, _pupilDataService, viewModelBuilder)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext },
            TempData = new TempDataDictionary(_httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    // ── PupilSearchPage GET — session guard ─────────────────────────────────

    [Fact]
    public async Task PupilSearchPage_WhenNoSession_RedirectsToCheckYourData()
    {
        SetupSession(new RequestState());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        AssertRedirectToCheckYourData(result);
    }

    [Fact]
    public async Task PupilSearchPage_WhenOnlyWhatToChangeAndWindowSet_AllowsAccess()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        Assert.IsType<ViewResult>(result);
    }

    // ── PupilSearchPage GET — view model ────────────────────────────────────

    [Fact]
    public async Task PupilSearchPage_BuildsViewModelWithCorrectTitle()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Equal("Which pupil do you want to remove?", vm.Title);
    }

    [Fact]
    public async Task PupilSearchPage_BuildsViewModelWithCorrectFilter()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Equal(PupilFilter.Included, vm.Filter);
    }

    [Fact]
    public async Task PupilSearchPage_WhenMatchPage_InterpolatesPupilName()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);

        var state = SessionWithPupil(history: ["select-pupil"]);
        SetupSession(state);

        var result = await _sut.PupilSearchPage(WindowId, "select-match-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Equal("Which pupil should Jane Smith be merged with?", vm.Title);
    }

    [Fact]
    public async Task PupilSearchPage_WhenMatchPage_SetsExcludePupilIdFromSelectedPupil()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);

        var state = SessionWithPupil(history: ["select-pupil"]);
        SetupSession(state);

        var result = await _sut.PupilSearchPage(WindowId, "select-match-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Equal(PrimaryPupilId, vm.ExcludePupilId);
    }

    [Fact]
    public async Task PupilSearchPage_WhenPrimaryPage_DoesNotSetExcludePupilId()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Null(vm.ExcludePupilId);
    }

    // ── PupilSearchPage GET — navigation guard ──────────────────────────────

    [Fact]
    public async Task PupilSearchPage_WhenNavigationGuardRedirectsToSummary_RedirectsToSummary()
    {
        SetupSession(SessionWithPupil(history: ["select-pupil"]));
        _flowService.GetNavigationGuard(RemoveConfig, Arg.Any<RequestState>(), "select-pupil")
            .Returns(new RedirectToJourneySummary());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ActionName);
    }

    [Fact]
    public async Task PupilSearchPage_WhenNavigationGuardRedirectsToPage_RedirectsToThatPage()
    {
        SetupSession(SessionWithoutPupil());
        _flowService.GetNavigationGuard(RemoveConfig, Arg.Any<RequestState>(), "select-pupil")
            .Returns(new RedirectToJourneyPage("some-other-page"));

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("some-other-page", redirect.RouteValues!["pageId"]);
    }

    // ── Page GET — dispatches PupilSearch pages ─────────────────────────────

    [Fact]
    public async Task Page_WhenPageIsPupilSearch_RedirectsToPupilSearchPage()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.Page(WindowId, "select-pupil");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("PupilSearchPage", redirect.ActionName);
        Assert.Equal("select-pupil", redirect.RouteValues!["pageId"]);
    }

    // ── PupilSearchPost — validation ────────────────────────────────────────

    [Fact]
    public async Task PupilSearchPost_WhenNoPupilSelected_ReturnsViewWithError()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PupilSearch", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("selectedPupilId"));
    }

    [Fact]
    public async Task PupilSearchPost_WhenInvalidGuid_ReturnsViewWithError()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", "not-a-guid", null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PupilSearch", view.ViewName);
    }

    [Fact]
    public async Task PupilSearchPost_WhenPageHasValidationFailure_UsesConfiguredMessage()
    {
        var pageWithMessage = new JourneyPage
        {
            Id = "select-pupil", Type = PageType.PupilSearch,
            PupilFilter = PupilFilter.Included, PupilKey = JourneyPage.PrimaryKey,
            NextPageId = "reason", ValidationFailure = "Select a pupil to remove"
        };
        var config = new QuestionFlowConfig { FirstPageId = "select-pupil", Pages = [pageWithMessage, QuestionPage] };
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(config);
        _flowService.GetPage(config, "select-pupil").Returns(pageWithMessage);
        SetupSession(SessionWithoutPupil());

        await _sut.PupilSearchPost(WindowId, "select-pupil", null, null);

        var error = _sut.ModelState["selectedPupilId"]?.Errors.FirstOrDefault()?.ErrorMessage;
        Assert.Equal("Select a pupil to remove", error);
    }

    [Fact]
    public async Task PupilSearchPost_WhenValidationFailureContainsPupilName_InterpolatesPupilName()
    {
        var pageWithMessage = new JourneyPage
        {
            Id = "select-match-pupil", Type = PageType.PupilSearch,
            PupilFilter = PupilFilter.All, PupilKey = JourneyPage.MatchKey,
            ValidationFailure = "Select the pupil to merge with {pupilName}"
        };
        var config = new QuestionFlowConfig { FirstPageId = "select-pupil", Pages = [PrimarySearchPage, pageWithMessage] };
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(config);
        _flowService.GetPage(config, "select-match-pupil").Returns(pageWithMessage);
        var state = SessionWithPupil(history: ["select-pupil"]);
        SetupSession(state);

        await _sut.PupilSearchPost(WindowId, "select-match-pupil", null, null);

        var error = _sut.ModelState["selectedPupilId"]?.Errors.FirstOrDefault()?.ErrorMessage;
        Assert.Equal("Select the pupil to merge with Jane Smith", error);
    }

    // ── PupilSearchPost — primary pupil ─────────────────────────────────────

    [Fact]
    public async Task PupilSearchPost_WhenPrimaryKey_SavesSelectedPupilAndGeneratesReference()
    {
        SetupSession(SessionWithoutPupil());
        _pupilDataService.GetPupilAsync(WindowId, PrimaryPupilId).Returns(PrimaryPupil);

        await _sut.PupilSearchPost(WindowId, "select-pupil", PrimaryPupilId.ToString(), "Smith, Jane, 01/01/2010");

        var state = _session.GetRequestState(WindowId);
        Assert.Equal(PrimaryPupilId, state.SelectedPupil?.Id);
        Assert.Equal(PrimaryPupilId.ToString(), state.SelectedPupilId);
        Assert.Equal("CYPMD_KS4June_TEST01", state.ReferenceNumber);
    }

    [Fact]
    public async Task PupilSearchPost_WhenPrimaryKey_ResetsQuestionAnswersAndHistory()
    {
        var state = SessionWithPupil(history: ["old-page"],
            answers: new() { ["q1"] = new QuestionAnswer { TextValue = "old-answer" } });
        SetupSession(state);
        _pupilDataService.GetPupilAsync(WindowId, PrimaryPupilId).Returns(PrimaryPupil);

        await _sut.PupilSearchPost(WindowId, "select-pupil", PrimaryPupilId.ToString(), "Smith, Jane");

        var saved = _session.GetRequestState(WindowId);
        Assert.Empty(saved.QuestionAnswers);
    }

    [Fact]
    public async Task PupilSearchPost_WhenPrimaryKey_ClearsMatchedPupil()
    {
        var state = SessionWithPupil(history: ["select-pupil", "select-match-pupil"]);
        state.MatchedPupil = MatchPupil;
        state.MatchedPupilId = MatchPupilId.ToString();
        state.MatchedPupilLabel = "Doe, John, 02/02/2010";
        SetupSession(state);
        _pupilDataService.GetPupilAsync(WindowId, PrimaryPupilId).Returns(PrimaryPupil);

        await _sut.PupilSearchPost(WindowId, "select-pupil", PrimaryPupilId.ToString(), "Smith, Jane");

        var saved = _session.GetRequestState(WindowId);
        Assert.Null(saved.MatchedPupil);
        Assert.Null(saved.MatchedPupilId);
        Assert.Null(saved.MatchedPupilLabel);
    }

    [Fact]
    public async Task PupilSearchPost_WhenPrimaryKey_AppendsPageIdToHistory()
    {
        SetupSession(SessionWithoutPupil());
        _pupilDataService.GetPupilAsync(WindowId, PrimaryPupilId).Returns(PrimaryPupil);

        await _sut.PupilSearchPost(WindowId, "select-pupil", PrimaryPupilId.ToString(), "Smith, Jane");

        var saved = _session.GetRequestState(WindowId);
        Assert.Contains("select-pupil", saved.QuestionHistory);
    }

    [Fact]
    public async Task PupilSearchPost_WhenPrimaryKeyAndHasNextPageId_RedirectsToNextPage()
    {
        SetupSession(SessionWithoutPupil());
        _pupilDataService.GetPupilAsync(WindowId, PrimaryPupilId).Returns(PrimaryPupil);

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", PrimaryPupilId.ToString(), "Smith, Jane");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("reason", redirect.RouteValues!["pageId"]);
    }

    // ── PupilSearchPost — match pupil ────────────────────────────────────────

    [Fact]
    public async Task PupilSearchPost_WhenMatchKey_SavesMatchedPupil()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);
        var state = SessionWithPupil(history: ["select-pupil"]);
        SetupSession(state);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);

        await _sut.PupilSearchPost(WindowId, "select-match-pupil", MatchPupilId.ToString(), "Doe, John, 02/02/2010");

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(MatchPupilId, saved.MatchedPupil?.Id);
        Assert.Equal(MatchPupilId.ToString(), saved.MatchedPupilId);
    }

    [Fact]
    public async Task PupilSearchPost_WhenMatchKey_DoesNotResetHistoryOrAnswers()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);
        var state = SessionWithPupil(history: ["select-pupil"],
            answers: new() { ["q1"] = new QuestionAnswer { TextValue = "existing" } });
        SetupSession(state);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);

        await _sut.PupilSearchPost(WindowId, "select-match-pupil", MatchPupilId.ToString(), "Doe, John");

        var saved = _session.GetRequestState(WindowId);
        Assert.Contains("select-pupil", saved.QuestionHistory);
        Assert.Equal("existing", saved.QuestionAnswers["q1"].TextValue);
    }

    [Fact]
    public async Task PupilSearchPost_WhenMatchKeyAndNoNextPageId_RedirectsToSummary()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);
        var state = SessionWithPupil(history: ["select-pupil"]);
        SetupSession(state);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);

        var result = await _sut.PupilSearchPost(WindowId, "select-match-pupil", MatchPupilId.ToString(), "Doe, John");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ActionName);
    }

    // ── Summary — merge pupil display ───────────────────────────────────────

    [Fact]
    public async Task Summary_ForMerge_PopulatesFirstRecordDisplay()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);
        _flowService.GetNextPageId(MergeConfig, "select-match-pupil", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        var state = SessionWithPupil(history: ["select-pupil", "select-match-pupil"]);
        state.MatchedPupil = MatchPupil;
        state.MatchedPupilId = MatchPupilId.ToString();
        SetupSession(state);

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Equal("Jane Smith, 1 January 2010", vm.FirstRecordDisplay);
    }

    [Fact]
    public async Task Summary_ForMerge_PopulatesSecondRecordDisplay()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);
        _flowService.GetNextPageId(MergeConfig, "select-match-pupil", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        var state = SessionWithPupil(history: ["select-pupil", "select-match-pupil"]);
        state.MatchedPupil = MatchPupil;
        state.MatchedPupilId = MatchPupilId.ToString();
        SetupSession(state);

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Equal("CYPMD456, John Doe", vm.SecondRecordDisplay);
    }

    [Fact]
    public async Task Summary_ForMerge_PopulatesPupilPageIds()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);
        _flowService.GetNextPageId(MergeConfig, "select-match-pupil", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        var state = SessionWithPupil(history: ["select-pupil", "select-match-pupil"]);
        state.MatchedPupil = MatchPupil;
        state.MatchedPupilId = MatchPupilId.ToString();
        SetupSession(state);

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Equal("select-pupil", vm.PrimaryPupilPageId);
        Assert.Equal("select-match-pupil", vm.MatchedPupilPageId);
    }

    [Fact]
    public async Task Summary_ForRemove_SecondRecordDisplayIsNull()
    {
        // RemoveConfig is the default — no MatchedPupil
        _flowService.GetNextPageId(RemoveConfig, "reason", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        var state = SessionWithPupil(history: ["select-pupil", "reason"]);
        _flowService.GetPage(RemoveConfig, "select-pupil").Returns(PrimarySearchPage);
        _flowService.GetPage(RemoveConfig, "reason").Returns(QuestionPage);
        SetupSession(state);

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Null(vm.SecondRecordDisplay);
    }

    [Fact]
    public async Task Summary_ForRemove_PopulatesPrimaryPupilPageId()
    {
        _flowService.GetNextPageId(RemoveConfig, "reason", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns((string?)null);
        var state = SessionWithPupil(history: ["select-pupil", "reason"]);
        _flowService.GetPage(RemoveConfig, "select-pupil").Returns(PrimarySearchPage);
        _flowService.GetPage(RemoveConfig, "reason").Returns(QuestionPage);
        SetupSession(state);

        var result = await _sut.Summary(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SummaryViewModel>(view.Model);
        Assert.Equal("select-pupil", vm.PrimaryPupilPageId);
    }

    // ── PupilSearchPage GET — back link ─────────────────────────────────────

    [Fact]
    public async Task PupilSearchPage_WhenFirstPageAndEmptyHistory_BackPageIdIsNull()
    {
        SetupSession(SessionWithoutPupil());

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Null(vm.BackPageId);
    }

    [Fact]
    public async Task PupilSearchPage_WhenSecondPageAndFirstPageInHistory_BackPageIdIsFirstPage()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);

        var state = SessionWithPupil(history: ["select-pupil"]);
        SetupSession(state);

        var result = await _sut.PupilSearchPage(WindowId, "select-match-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Equal("select-pupil", vm.BackPageId);
        Assert.True(vm.BackPageIsPupilSearch);
    }

    [Fact]
    public async Task PupilSearchPage_WhenReturningToFirstPageFromSummary_BackPageIdIsNull()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(MergeConfig);

        var state = SessionWithPupil(history: ["select-pupil", "select-match-pupil"]);
        SetupSession(state);

        var result = await _sut.PupilSearchPage(WindowId, "select-pupil");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<PupilSearchViewModel>(view.Model);
        Assert.Null(vm.BackPageId);
    }

    // ── IsSessionReady — no longer requires pupil ────────────────────────────

    [Fact]
    public async Task Page_WhenPupilNotSelected_ButWhatToChangeAndWindowSet_DoesNotRedirectToCheckYourData()
    {
        // After migration, session is ready without a pupil — the journey itself selects the pupil
        SetupSession(SessionWithoutPupil());
        _flowService.GetPage(RemoveConfig, "select-pupil").Returns(PrimarySearchPage);

        var result = await _sut.Page(WindowId, "select-pupil");

        // Should dispatch to PupilSearchPage (not redirect to CheckYourData)
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("PupilSearchPage", redirect.ActionName);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetupSession(RequestState state)
    {
        var json = JsonSerializer.Serialize(state);
        _session.Set($"request_{WindowId}", Encoding.UTF8.GetBytes(json));
    }

    private static RequestState SessionWithoutPupil() => new()
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
        }
    };

    private static RequestState SessionWithPupil(
        List<string>? history = null,
        Dictionary<string, QuestionAnswer>? answers = null)
    {
        var state = SessionWithoutPupil();
        state.SelectedPupil = PrimaryPupil;
        state.SelectedPupilId = PrimaryPupilId.ToString();
        state.QuestionHistory = history ?? [];
        state.QuestionAnswers = answers ?? new();
        state.ReferenceNumber = "CYPMD_KS4June_TEST01";
        return state;
    }

    private static void AssertRedirectToCheckYourData(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
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
