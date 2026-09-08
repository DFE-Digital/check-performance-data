using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
using CheckingWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// AB#297780 conflict check at the Include hand-off. When the school chooses "Include this pupil"
/// (single non-included match) or "Switch to include" (a non-included row on a multiple list), the
/// Add journey's learner-details interceptor already holds the duplicate-check result in session.
/// Before seeding that pupil into the Include journey, the hand-off re-runs the one-request-per-pupil
/// rule (HasSubmittedRequestAsync) exactly as PupilSearchPost does: a SelfSubmitted/OtherSubmitted
/// conflict blocks the hand-off — no seed, no redirect, no include decision event — and re-renders
/// the duplicate-check page with the GDS conflict error. The no-conflict path is unchanged.
/// </summary>
public class IncludeHandoffConflictTests
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
    private readonly FakeSession _session = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly JourneyController _sut;

    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatchPupilId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid IncludedPupilId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly JourneyPage IncludeSearchPage = new()
    {
        Id = "select-pupil",
        Type = PageType.PupilSearch,
        Title = "Which pupil do you want to include?",
        PupilFilter = PupilFilter.Included,
        PupilKey = JourneyPage.PrimaryKey,
        NextPageId = "evidence"
    };

    private static readonly JourneyPage EvidencePage = new()
    {
        Id = "evidence",
        Questions = [new Question { Id = "q1", Type = QuestionType.FileUpload, Title = "Upload" }]
    };

    private static readonly JourneyPage LearnerDetailsPage = new()
    {
        Id = "learner-details",
        Questions = []
    };

    private static readonly QuestionFlowConfig IncludeConfig = new()
    {
        FirstPageId = "select-pupil",
        Pages = [IncludeSearchPage, EvidencePage]
    };

    private static readonly QuestionFlowConfig AddConfig = new()
    {
        FirstPageId = "learner-details",
        Pages = [LearnerDetailsPage]
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
        Identifier = "UPN002"
    };

    private static readonly DuplicateMatch SingleMatch = new()
    {
        Id = MatchPupilId,
        Firstname = "John",
        Surname = "Doe",
        DateOfBirth = "02/02/2010",
        Identifier = "UPN002",
        IsIncluded = false
    };

    private static readonly DuplicateMatch SecondMatch = new()
    {
        Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Firstname = "Jane",
        Surname = "Smith",
        DateOfBirth = "03/03/2010",
        Identifier = "UPN003",
        IsIncluded = true
    };

    private static readonly PupilDuplicateCheckResult SingleNonIncludedCheck =
        PupilDuplicateCheckResult.Build([SingleMatch]);

    private static readonly PupilDuplicateCheckResult MultipleCheck =
        PupilDuplicateCheckResult.Build([SingleMatch, SecondMatch]);

    public IncludeHandoffConflictTests()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(AddConfig);
        _flowService.GetPage(AddConfig, "learner-details").Returns(LearnerDetailsPage);
        _flowService.GetPage(IncludeConfig, "select-pupil").Returns(IncludeSearchPage);
        _flowService.GetPage(IncludeConfig, "evidence").Returns(EvidencePage);
        _flowService.GetNextPageId(IncludeConfig, "select-pupil", Arg.Any<Dictionary<string, QuestionAnswer>>())
            .Returns("evidence");
        _journeyService.GenerateReference(Arg.Any<CheckingWindowType?>()).Returns("CYPMD_KS4June_TEST01");
        _currentUserService.OrganisationUrn.Returns("142313");
        _requestService.HasSubmittedRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>())
            .Returns(new DuplicateCheckResult.NoConflict());
        _flowService.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>())
            .Returns(string.Empty);

        _optionalityService.GetConditionallyOptionalQuestionIds(Arg.Any<JourneyPage>(), Arg.Any<JourneyConditionContext>())
            .Returns(new HashSet<string>());

        _httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        var viewModelBuilder = new JourneyViewModelBuilder(
            _flowService, _journeyService, _optionVisibilityService, _currentUserService);

        _sut = new JourneyController(_flowService, _journeyService, _fileStorageService,
            _requestService, _pupilDataService, viewModelBuilder, _analytics, _currentUserService,
            _optionVisibilityService, _optionalityService, _languageCapture,
            Substitute.For<DfE.CheckPerformanceData.Application.ResultsEnquiry.IStudentResultsClient>(),
            Substitute.For<DfE.CheckPerformanceData.Application.ResultsEnquiry.IGradeReferenceClient>(),
            Substitute.For<DfE.CheckPerformanceData.Application.ResultsEnquiry.IQualificationReferenceClient>(),
            Substitute.For<DfE.CheckPerformanceData.Application.Notify.IRequestNotificationService>(),
            OpenCheckingExercises.AlwaysOpen(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext },
            TempData = new TempDataDictionary(_httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    // ── IncludeThisPupil — self-submitted conflict ─────────────────────────

    [Fact]
    public async Task IncludeThisPupil_SelfSubmitted_ReRendersDuplicateCheckWithConflict()
    {
        SetupSession(AddSessionAwaitingInclude(SingleNonIncludedCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _requestService.HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313)
            .Returns(new DuplicateCheckResult.SelfSubmitted("REF001", "IncorrectGrade", "Include", "Test User"));

        var result = await _sut.IncludeThisPupil(WindowId);

        var vm = AssertConflictReRender(result);
        Assert.Equal("REF001", vm.ConflictErrorReference);
        Assert.Equal($"/{WindowId}/AmendmentRequests/REF001/view", vm.ConflictErrorLink);
        Assert.Equal("John Doe", vm.ConflictPupilName);
        Assert.Equal("IncorrectGrade", vm.ConflictReasonType);
        Assert.Equal("Test User", vm.ConflictUserName);
        Assert.Contains("REF001", vm.ConflictAttentionHtml);
        Assert.Contains("View submitted request (opens in new tab)", vm.ConflictAttentionHtml);
        Assert.Equal(DuplicateScenario.SingleNonIncluded, vm.Scenario);
        Assert.Single(vm.Matches);
        Assert.Equal("Choose another pupil", _sut.ModelState["selectedPupilId"]?.Errors.FirstOrDefault()?.ErrorMessage);
        Assert.Equal("A request has already been submitted for this pupil",
            _sut.ModelState[string.Empty]?.Errors.FirstOrDefault()?.ErrorMessage);

        await _requestService.Received(1).HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313);
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<DuplicateCheckDecisionEvent>(), Arg.Any<CancellationToken>());
        await AssertValidationErrorEventOnce();

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Add, saved.SelectedWhatToChange);
        Assert.NotNull(saved.DuplicateCheck);
    }

    // ── IncludeThisPupil — other-submitted conflict ────────────────────────

    [Fact]
    public async Task IncludeThisPupil_OtherSubmitted_NamesColleagueAndDoesNotSeed()
    {
        SetupSession(AddSessionAwaitingInclude(SingleNonIncludedCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _requestService.HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313)
            .Returns(new DuplicateCheckResult.OtherSubmitted("REF002", "Merge", "Pupil data", "A Colleague"));

        var result = await _sut.IncludeThisPupil(WindowId);

        var vm = AssertConflictReRender(result);
        Assert.Equal("A Colleague", vm.ConflictUserName);
        Assert.Equal("REF002", vm.ConflictErrorReference);
        Assert.Equal("Merge", vm.ConflictReasonType);
        Assert.Contains("A Colleague", vm.ConflictAttentionHtml);
        Assert.Contains("/AmendmentRequests/REF002/view", vm.ConflictAttentionHtml);

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<DuplicateCheckDecisionEvent>(), Arg.Any<CancellationToken>());
        await AssertValidationErrorEventOnce();

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Add, saved.SelectedWhatToChange);
        Assert.NotNull(saved.DuplicateCheck);
    }

    // ── IncludeThisPupil — no conflict (unchanged) ─────────────────────────

    [Fact]
    public async Task IncludeThisPupil_NoConflict_SeedsAndRedirectsToEvidence()
    {
        SetupSession(AddSessionAwaitingInclude(SingleNonIncludedCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);

        var result = await _sut.IncludeThisPupil(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("evidence", redirect.RouteValues!["pageId"]);

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Include, saved.SelectedWhatToChange);
        Assert.Equal(MatchPupilId, saved.SelectedPupil?.Id);
        Assert.Null(saved.DuplicateCheck);
        Assert.Contains("select-pupil", saved.QuestionHistory);

        await _requestService.Received(1).HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313);
        await _analytics.Received(1).TrackAsync(
            Arg.Is<DuplicateCheckDecisionEvent>(e => e.Scenario == "SingleNonIncluded" && e.Action == "include"),
            Arg.Any<CancellationToken>());
        await AssertNoValidationErrorEvent();
    }

    // ── SwitchToInclude — conflict ─────────────────────────────────────────

    [Fact]
    public async Task SwitchToInclude_SelfSubmitted_ReRendersDuplicateCheckWithConflict()
    {
        SetupSession(AddSessionAwaitingInclude(MultipleCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _requestService.HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313)
            .Returns(new DuplicateCheckResult.SelfSubmitted("REF003", "IncorrectGrade", "Include", "Test User"));

        var result = await _sut.SwitchToInclude(WindowId, MatchPupilId);

        var vm = AssertConflictReRender(result);
        Assert.Equal("REF003", vm.ConflictErrorReference);
        Assert.Equal("John Doe", vm.ConflictPupilName);
        Assert.Equal("Test User", vm.ConflictUserName);
        Assert.Contains("REF003", vm.ConflictAttentionHtml);
        Assert.Equal(DuplicateScenario.Multiple, vm.Scenario);
        Assert.Equal(2, vm.Matches.Count);

        await _requestService.Received(1).HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313);
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<DuplicateCheckDecisionEvent>(), Arg.Any<CancellationToken>());
        await AssertValidationErrorEventOnce();

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Add, saved.SelectedWhatToChange);
        Assert.NotNull(saved.DuplicateCheck);
    }

    [Fact]
    public async Task SwitchToInclude_NoConflict_SeedsAndRedirectsToEvidence()
    {
        SetupSession(AddSessionAwaitingInclude(MultipleCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);

        var result = await _sut.SwitchToInclude(WindowId, MatchPupilId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("evidence", redirect.RouteValues!["pageId"]);

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Include, saved.SelectedWhatToChange);
        Assert.Equal(MatchPupilId, saved.SelectedPupil?.Id);
        Assert.Null(saved.DuplicateCheck);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<DuplicateCheckDecisionEvent>(e => e.Scenario == "Multiple" && e.Action == "switch-to-include"),
            Arg.Any<CancellationToken>());
        await AssertNoValidationErrorEvent();
    }

    // ── AB#027 already-included guard at the hand-off ──────────────────────

    [Fact]
    public async Task IncludeThisPupil_WhenSameNameAlreadyIncluded_RedirectsToAlreadyIncludedWithoutSeeding()
    {
        SetupSession(AddSessionAwaitingInclude(SingleNonIncludedCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "John Doe", PupilFilter.Included)
            .Returns([new PupilSuggestionDto(IncludedPupilId, "Doe, John", "John", "Doe", "02/02/2010")]);

        var result = await _sut.IncludeThisPupil(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("AlreadyIncluded", redirect.ActionName);
        // AB#297780: the warning came from the duplicate-check hand-off, so "Back" must return to
        // the match list — the pageId-less DuplicateCheck action, not the Include search.
        Assert.Equal("DuplicateCheck", redirect.RouteValues!["backAction"]);

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Add, saved.SelectedWhatToChange);
        Assert.Null(saved.SelectedPupil);
        Assert.NotNull(saved.DuplicateCheck);
        Assert.Equal("Doe, John, 02/02/2010", saved.IncludeSearchLabel);
        var matches = Assert.Single(saved.IncludeMatchedPupils!);
        Assert.Equal(IncludedPupilId, matches.Id);

        await _requestService.DidNotReceive().HasSubmittedRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>());
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<DuplicateCheckDecisionEvent>(), Arg.Any<CancellationToken>());
        await AssertNoValidationErrorEvent();
    }

    [Fact]
    public async Task SwitchToInclude_WhenSameNameAlreadyIncluded_RedirectsToAlreadyIncluded()
    {
        SetupSession(AddSessionAwaitingInclude(MultipleCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "John Doe", PupilFilter.Included)
            .Returns([new PupilSuggestionDto(IncludedPupilId, "Doe, John", "John", "Doe", "02/02/2010")]);

        var result = await _sut.SwitchToInclude(WindowId, MatchPupilId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("AlreadyIncluded", redirect.ActionName);
        Assert.Equal("DuplicateCheck", redirect.RouteValues!["backAction"]);

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Add, saved.SelectedWhatToChange);
        Assert.Null(saved.SelectedPupil);
        Assert.NotNull(saved.DuplicateCheck);

        await _requestService.DidNotReceive().HasSubmittedRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>());
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<DuplicateCheckDecisionEvent>(), Arg.Any<CancellationToken>());
        await AssertNoValidationErrorEvent();
    }

    [Fact]
    public async Task SwitchToInclude_WhenNoAlreadyIncludedNameMatch_ProceedsToSeed()
    {
        SetupSession(AddSessionAwaitingInclude(MultipleCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "John Doe", PupilFilter.Included)
            .Returns([]);

        var result = await _sut.SwitchToInclude(WindowId, MatchPupilId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("evidence", redirect.RouteValues!["pageId"]);

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Include, saved.SelectedWhatToChange);
        Assert.Equal(MatchPupilId, saved.SelectedPupil?.Id);
        Assert.Null(saved.DuplicateCheck);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<DuplicateCheckDecisionEvent>(e => e.Scenario == "Multiple" && e.Action == "switch-to-include"),
            Arg.Any<CancellationToken>());
        await AssertNoValidationErrorEvent();
    }

    // ── Results-enquiry guard (US2 / FR-007) ───────────────────────────────

    [Fact]
    public async Task Handoff_WhenPupilDataJourney_AppliesTheConflictCheck()
    {
        SetupSession(AddSessionAwaitingInclude(SingleNonIncludedCheck));
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _requestService.HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313)
            .Returns(new DuplicateCheckResult.SelfSubmitted("REF004", "IncorrectGrade", "Include", "Test User"));

        var result = await _sut.IncludeThisPupil(WindowId);

        AssertConflictReRender(result);
        await _requestService.Received(1).HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313);
        Assert.False(WhatToChangeCheckingExerciseMap.IsResultsEnquiry(WhatToChange.Add));
        Assert.False(WhatToChangeCheckingExerciseMap.IsResultsEnquiry(WhatToChange.Include));
    }

    [Fact]
    public async Task Handoff_WhenResultsEnquiryJourney_SkipsTheConflictCheck()
    {
        var state = AddSessionAwaitingInclude(SingleNonIncludedCheck);
        state.SelectedWhatToChange = WhatToChange.IncorrectGrade;
        SetupSession(state);
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(IncludeConfig);
        _pupilDataService.GetPupilAsync(WindowId, MatchPupilId).Returns(MatchPupil);
        _requestService.HasSubmittedRequestAsync(WindowId, MatchPupilId, 142313)
            .Returns(new DuplicateCheckResult.SelfSubmitted("REF004", "IncorrectGrade", "Include", "Test User"));

        var result = await _sut.IncludeThisPupil(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("evidence", redirect.RouteValues!["pageId"]);
        await _requestService.DidNotReceive().HasSubmittedRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>());
        Assert.True(WhatToChangeCheckingExerciseMap.IsResultsEnquiry(WhatToChange.IncorrectGrade));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private DuplicateCheckViewModel AssertConflictReRender(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("DuplicateCheck", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("selectedPupilId"));
        Assert.True(_sut.ModelState.ContainsKey(string.Empty));
        return Assert.IsType<DuplicateCheckViewModel>(view.Model);
    }

    private async Task AssertValidationErrorEventOnce() =>
        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e =>
                e.ErrorCount == 1 &&
                e.ErrorCodes.Count == 1 && e.ErrorCodes.Contains(ValidationErrorCoding.Conflict) &&
                e.ErrorFields != null && e.ErrorFields.Contains("selectedPupilId") &&
                e.WhatToChange == "Add"),
            Arg.Any<CancellationToken>());

    private async Task AssertNoValidationErrorEvent() =>
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<ValidationErrorEvent>(), Arg.Any<CancellationToken>());

    private void SetupSession(RequestState state)
    {
        var json = JsonSerializer.Serialize(state);
        _session.Set($"request_{WindowId}", Encoding.UTF8.GetBytes(json));
    }

    private static RequestState AddSessionAwaitingInclude(PupilDuplicateCheckResult check) => new()
    {
        SelectedWhatToChange = WhatToChange.Add,
        CheckingWindow = new CheckingWindowDto
        {
            Id = Guid.NewGuid(),
            Title = "Test Window",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(13)
        },
        DuplicateCheck = check
    };

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