using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.Analytics;
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
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// AB#027: Include journey no-results decision flow. When a user on the Include journey's
/// select-pupil step types a name, gets no autocomplete result, and clicks Continue without
/// selecting a pupil, the system looks the typed entry up against BOTH the included and
/// non-included populations:
///  - included match warns "already included" (abort);
///  - non-included-only match is a valid include candidate — the journey proceeds as normal
///    (re-renders the search page with the candidate visible, no warning, no decision page);
///  - no match on either list offers "pupil not found" (start Add / search again).
/// Blank entry and lookup failure keep the existing validation (fail safe, never a dead end).
/// </summary>
public class IncludeNoResultsDecisionTests
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
    private static readonly Guid IncludedPupilId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly JourneyPage IncludePrimarySearchPage = new()
    {
        Id = "select-pupil",
        Type = PageType.PupilSearch,
        Title = "What is the name of the pupil to be included?",
        PupilFilter = PupilFilter.NonIncluded,
        PupilKey = JourneyPage.PrimaryKey,
        ValidationFailure = "Enter the name of the pupil to be included."
    };

    private static readonly JourneyPage RemovePrimarySearchPage = new()
    {
        Id = "select-pupil",
        Type = PageType.PupilSearch,
        Title = "Which pupil do you want to remove?",
        PupilFilter = PupilFilter.Included,
        PupilKey = JourneyPage.PrimaryKey,
        ValidationFailure = "Enter the name of the pupil"
    };

    private static readonly JourneyPage QuestionPage = new()
    {
        Id = "evidence",
        Questions =
        [
            new Question { Id = "q1", Type = QuestionType.FileUpload, Title = "Upload" }
        ]
    };

    private static readonly QuestionFlowConfig IncludeConfig = new()
    {
        FirstPageId = "select-pupil",
        Pages = [IncludePrimarySearchPage, QuestionPage]
    };

    private static readonly QuestionFlowConfig RemoveConfig = new()
    {
        FirstPageId = "select-pupil",
        Pages = [RemovePrimarySearchPage, QuestionPage]
    };

    private static readonly PupilSuggestionDto IncludedSuggestion = new(IncludedPupilId, "Smith, Alice, 01/01/2010");
    private static readonly PupilSuggestionDto NonIncludedSuggestion = new(IncludedPupilId, "Johnson, Bob, 02/02/2010");

    public IncludeNoResultsDecisionTests()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(IncludeConfig);
        _flowService.GetPage(IncludeConfig, "select-pupil").Returns(IncludePrimarySearchPage);
        _flowService.GetPage(IncludeConfig, "evidence").Returns(QuestionPage);
        _flowService.GetPage(RemoveConfig, "select-pupil").Returns(RemovePrimarySearchPage);
        _journeyService.GenerateReference(Arg.Any<CheckingWindowType?>()).Returns("CYPMD_KS4June_TEST01");
        _currentUserService.OrganisationUrn.Returns("100000");
        _requestService.HasSubmittedRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>())
            .Returns(new DuplicateCheckResult.NoConflict());

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

    // -- T004: non-blank typed text + no selection + no match on EITHER list → "Pupil not found" --

    [Fact]
    public async Task PupilSearchPost_NeitherListMatch_ReturnsPupilNotFoundView()
    {
        SetupSession(SessionForInclude());
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "No Such", PupilFilter.Included, Arg.Any<Guid?>(), Arg.Any<bool>())
            .Returns(new List<PupilSuggestionDto>());
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "No Such", PupilFilter.NonIncluded, Arg.Any<Guid?>(), Arg.Any<bool>())
            .Returns(new List<PupilSuggestionDto>());

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, "No Such");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("PupilNotFound", redirect.ActionName);
        var routePageId = Assert.IsType<string>(redirect.RouteValues!["pageId"]);
        Assert.Equal("select-pupil", routePageId);
        // The typed label is carried in session (PII — never in the URL) for the page to render.
        Assert.Equal("No Such", _session.GetRequestState(WindowId).IncludeSearchLabel);
        Assert.True(_sut.ModelState.IsValid);
    }

    // -- T005: non-blank typed text + no selection + no included match but a NON-INCLUDED match
    //    → a valid include candidate: proceed as normal (no decision page, no "not found" warning) --

    [Fact]
    public async Task PupilSearchPost_NonIncludedOnlyMatch_ProceedsAsNormal_NoDecisionRedirect()
    {
        SetupSession(SessionForInclude());
        // "Johnson" is a non-included surname bucket — it matches the non-included pupil but no
        // included one (included surnames are Smith..Davies).
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "Johnson", PupilFilter.Included, Arg.Any<Guid?>(), Arg.Any<bool>())
            .Returns(new List<PupilSuggestionDto>());
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "Johnson", PupilFilter.NonIncluded, Arg.Any<Guid?>(), Arg.Any<bool>())
            .Returns(new List<PupilSuggestionDto> { NonIncludedSuggestion });

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, "Johnson");

        // Not a decision redirect — the normal no-selection flow re-renders the search page with
        // the typed label restored so the user can pick the matching non-included candidate.
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PupilSearch", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("selectedPupilId"), "The ordinary no-selection validation is shown so the user picks the candidate from the suggestions.");
        Assert.Equal("Johnson", _session.GetRequestState(WindowId).IncludeSearchLabel);
        // The three-way decision consults the non-included list to discover the valid candidate.
        await _pupilDataService.Received()
            .GetPupilSuggestionsAsync(WindowId, "Johnson", PupilFilter.NonIncluded, Arg.Any<Guid?>(), Arg.Any<bool>());
    }

    // -- T005: non-blank typed text + no selection + included match → "Already included" --

    [Fact]
    public async Task PupilSearchPost_IncludedMatch_RedirectsToAlreadyIncluded()
    {
        SetupSession(SessionForInclude());
        // "Smith" is an included surname bucket — it matches the included pupil Alice Smith.
        _pupilDataService.GetPupilSuggestionsAsync(WindowId, "Smith", PupilFilter.Included, Arg.Any<Guid?>(), Arg.Any<bool>())
            .Returns(new List<PupilSuggestionDto> { IncludedSuggestion });

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, "Smith");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("AlreadyIncluded", redirect.ActionName);
        var saved = _session.GetRequestState(WindowId);
        Assert.Equal("Smith", saved.IncludeSearchLabel);
        // The matching included pupils are carried in session (PII — never in the URL) so the
        // "already included" page can show who was matched.
        Assert.NotNull(saved.IncludeMatchedPupils);
        var match = Assert.Single(saved.IncludeMatchedPupils!);
        Assert.Equal(IncludedPupilId, match.Id);
        Assert.Equal("Smith, Alice, 01/01/2010", match.Label);
        Assert.True(_sut.ModelState.IsValid);
        // An included match is conclusive — there is no need to consult the non-included list.
        await _pupilDataService.DidNotReceive()
            .GetPupilSuggestionsAsync(WindowId, "Smith", PupilFilter.NonIncluded, Arg.Any<Guid?>(), Arg.Any<bool>());
    }

    // -- The "Already included" GET surfaces the persisted matches and consumes them on arrival --

    [Fact]
    public async Task AlreadyIncluded_Get_ShowsTypedLabelAndMatches_ThenClearsSession()
    {
        SetupSession(SessionForIncludeWith(
            label: "Smith",
            matches: [IncludedSuggestion]));

        var result = await _sut.AlreadyIncluded(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<IncludeAlreadyIncludedViewModel>(view.Model);
        Assert.Equal("Smith", model.TypedPupilLabel);
        var match = Assert.Single(model.Matches);
        Assert.Equal("Smith, Alice, 01/01/2010", match.Label);
        // Consumed on arrival: both the label and the match list are cleared so the URL can't be
        // reused to replay the (PII) decision.
        Assert.Null(_session.GetRequestState(WindowId).IncludeSearchLabel);
        Assert.Null(_session.GetRequestState(WindowId).IncludeMatchedPupils);
    }

    // -- T006: blank typed text + no selection → existing validation error (unchanged) --

    [Fact]
    public async Task PupilSearchPost_BlankLabel_ReturnsExistingValidationError()
    {
        SetupSession(SessionForInclude());

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, null);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PupilSearch", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("selectedPupilId"));
        var error = _sut.ModelState["selectedPupilId"]?.Errors.FirstOrDefault()?.ErrorMessage;
        Assert.Equal("Enter the name of the pupil to be included.", error);
        await _pupilDataService.DidNotReceive()
            .GetPupilSuggestionsAsync(Arg.Any<Guid>(), Arg.Any<string>(), PupilFilter.Included, Arg.Any<Guid?>(), Arg.Any<bool>());
    }

    // -- T007: non-blank typed text + no selection on non-Include journey → unchanged --

    [Fact]
    public async Task PupilSearchPost_NonIncludeJourney_ReturnsExistingValidationError()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(RemoveConfig);
        SetupSession(SessionForRemove());

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, "No Such");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PupilSearch", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("selectedPupilId"));
        await _pupilDataService.DidNotReceive()
            .GetPupilSuggestionsAsync(Arg.Any<Guid>(), Arg.Any<string>(), PupilFilter.Included, Arg.Any<Guid?>(), Arg.Any<bool>());
    }

    // -- T008: lookup failure → falls back to existing validation (fail safe) --

    [Fact]
    public async Task PupilSearchPost_LookupFailure_FallsBackToExistingValidation()
    {
        SetupSession(SessionForInclude());
        _pupilDataService.GetPupilSuggestionsAsync(Arg.Any<Guid>(), Arg.Any<string>(), PupilFilter.Included, Arg.Any<Guid?>(), Arg.Any<bool>())
            .Returns(Task.FromException<IReadOnlyList<PupilSuggestionDto>>(new InvalidOperationException("blob unavailable")));

        var result = await _sut.PupilSearchPost(WindowId, "select-pupil", null, "No Such");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PupilSearch", view.ViewName);
        Assert.True(_sut.ModelState.ContainsKey("selectedPupilId"));
    }

    // -- T018: StartAddingPupil resets session to WhatToChange.Add and redirects to first Add page --

    [Fact]
    public async Task StartAddingPupil_ResetsToAddJourney_RedirectsToLearnerDetails()
    {
        SetupSession(SessionForInclude());
        var addConfig = new QuestionFlowConfig
        {
            FirstPageId = "learner-details",
            Pages = [new JourneyPage { Id = "learner-details", Type = PageType.Question }]
        };
        _flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.KS4June).Returns(addConfig);

        var result = await _sut.StartAddingPupil(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("learner-details", redirect.RouteValues!["pageId"]);

        var saved = _session.GetRequestState(WindowId);
        Assert.Equal(WhatToChange.Add, saved.SelectedWhatToChange);
        Assert.Null(saved.SelectedPupil);
        Assert.Null(saved.SelectedPupilId);
        Assert.Null(saved.SelectedPupilLabel);
        Assert.Null(saved.MatchedPupil);
        Assert.Null(saved.MatchedPupilId);
        Assert.Null(saved.MatchedPupilLabel);
        Assert.Null(saved.DuplicateCheck);
        Assert.Null(saved.IncludeMatchedPupils);
        Assert.Empty(saved.QuestionAnswers);
        Assert.Equal("learner-details", saved.QuestionHistory.Single());
    }

    // -- T019: PupilSearchAgain returns to the Include select-pupil search page --

    [Fact]
    public async Task PupilSearchAgain_RedirectsToPupilSearchPage()
    {
        SetupSession(SessionForInclude());

        var result = _sut.PupilSearchAgain(WindowId, "select-pupil");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("PupilSearchPage", redirect.ActionName);
        Assert.Equal("select-pupil", redirect.RouteValues!["pageId"]);
    }

    // -- T024: AbortInclude abandons the Include journey and returns to Check Your Pupil Data --

    [Fact]
    public async Task AbortInclude_RedirectsToCheckYourPupilData()
    {
        SetupSession(SessionForInclude());

        var result = await _sut.AbortInclude(WindowId);

        AssertRedirectToCheckYourData(result);
    }

    // -- Helpers ---------------------------------------------------------------

    private void SetupSession(RequestState state)
    {
        var json = JsonSerializer.Serialize(state);
        _session.Set($"request_{WindowId}", Encoding.UTF8.GetBytes(json));
    }

    private static RequestState SessionForInclude() => new()
    {
        SelectedWhatToChange = WhatToChange.Include,
        CheckingWindow = new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(13)
        },
        ReferenceNumber = "CYPMD_KS4June_TEST01"
    };

    private static RequestState SessionForIncludeWith(string label, params PupilSuggestionDto[] matches) => new()
    {
        SelectedWhatToChange = WhatToChange.Include,
        CheckingWindow = new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(13)
        },
        ReferenceNumber = "CYPMD_KS4June_TEST01",
        IncludeSearchLabel = label,
        IncludeMatchedPupils = matches.ToList()
    };

    private static RequestState SessionForRemove() => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
        CheckingWindow = new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(13)
        },
        ReferenceNumber = "CYPMD_KS4June_TEST01"
    };

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