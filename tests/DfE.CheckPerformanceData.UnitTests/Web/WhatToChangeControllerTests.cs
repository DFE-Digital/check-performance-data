using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class WhatToChangeControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly WhatToChangeController _sut;

    public WhatToChangeControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));
        // #318: both actions now read the window before anything else, to gate on the pupil-data
        // checking exercise. Stubbed for every test; the exercise service decides open/closed.
        _service.GetCheckingWindowAsync(WindowId).Returns(Window());
        _sut = new WhatToChangeController(_service, _flowService, _checkingExercises, _analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "W",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(10)
    };

    [Fact]
    public async Task Confirm_WhenValid_EmitsChangeTypeSelectedEvent()
    {
        _flowService.GetConfigAsync(WhatToChange.Remove, CheckingWindowType.KS4June)
            .Returns(new QuestionFlowConfig { FirstPageId = "page-1", Pages = [] });

        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Remove });

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ChangeTypeSelectedEvent>(e => e.WhatToChange == "Remove" && e.CheckingWindowType == "KS4June"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_WhenNoSelection_DoesNotEmitChangeTypeSelected()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());

        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = null });

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<ChangeTypeSelectedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_WhenNoSelection_EmitsValidationError()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());

        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = null });

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e => e.ErrorCount == 1 && e.ErrorCodes.Contains("no_selection")),
            Arg.Any<CancellationToken>());
    }

    // ── AB#297310: CheckingWindowType feeds the Add-radio gate ───────────────

    [Fact]
    public async Task Index_PopulatesCheckingWindowType()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());

        var result = await _sut.Index(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<WhatToChangeViewModel>(view.Model);
        Assert.Equal(CheckingWindowType.KS4June, vm.CheckingWindowType);
    }

    [Fact]
    public async Task Confirm_WithNoSelection_RetainsCheckingWindowType()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = null });

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<WhatToChangeViewModel>(view.Model);
        Assert.Equal(CheckingWindowType.KS4June, vm.CheckingWindowType);
    }

    // The unknown-flow case: no Add_Post16.json in blob, so the flow service has nothing to
    // return. Pins the redirect only — the window-type rule is pinned by the test below, which
    // stubs a config so this path cannot be what sends the user back.
    [Fact]
    public async Task Confirm_AddOnPost16_WithNoAddFlow_RedirectsToCheckYourPupilData()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Post16Window());
        _flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.Post16)
            .Returns((QuestionFlowConfig?)null);

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Add });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

    // The window-type rule itself: an Add_Post16.json uploaded to blob must not open the journey
    // on a window type the Add radio was never offered for.
    [Fact]
    public async Task Confirm_AddOnPost16_WithAnAddFlowPresent_StillRedirectsAndLeavesStateAlone()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Post16Window());
        _flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.Post16)
            .Returns(FlowWithoutPupilSearch);
        _session.SaveRequestState(WindowId, SeedStaleIdentity);

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Add });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
        AssertStaleIdentityIntact(_session.GetRequestState(WindowId));
    }

    // An unmapped flow is not an empty flow. Every window type renders Merge/Include/Remove, but
    // only four flow files exist per window type at most — on a KS2 window three of those radios
    // resolve to a null config, and a forged enum value binds just as cleanly. Before AB#297310
    // that post was a harmless redirect; it must stay one, rather than destroying a journey the
    // user has already part-completed in another tab.
    [Fact]
    public async Task Confirm_WhenTheFlowIsUnknown_LeavesAnInProgressJourneyIntact()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());
        _flowService.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June)
            .Returns((QuestionFlowConfig?)null);
        _session.SaveRequestState(WindowId, SeedStaleIdentity);

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Include });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        AssertStaleIdentityIntact(_session.GetRequestState(WindowId));
    }

    [Fact]
    public async Task Confirm_WithAnUnmappedWhatToChangeValue_LeavesAnInProgressJourneyIntact()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns((QuestionFlowConfig?)null);
        _session.SaveRequestState(WindowId, SeedStaleIdentity);

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = (WhatToChange)99 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
        AssertStaleIdentityIntact(_session.GetRequestState(WindowId));
    }

    // AB#297310: starting a fresh journey must not inherit a previous one's identity.
    //
    // Every flow that opens with a pupil search is protected by PupilSearchPost, which
    // unconditionally regenerates the reference and the selected pupil and nulls the matched
    // pupil and result. A flow WITHOUT one — the Add journey is the first — has nothing that
    // refreshes any of it, so a reference and pupil left in session by an already-submitted
    // request in the same browser session would be silently reused for a brand-new one (the
    // upsert then overwrites the submitted row), and an abandoned Merge journey's matched pupil
    // would surface on the Add summary as "Second record to merge".
    [Fact]
    public async Task Confirm_ForAFlowWithNoPupilSearchPage_ClearsEveryPerRequestIdentityField()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());
        _flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.KS4June)
            .Returns(FlowWithoutPupilSearch);
        _session.SaveRequestState(WindowId, SeedStaleIdentity);

        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Add });

        var journey = _session.GetRequestState(WindowId);
        Assert.Null(journey.ReferenceNumber);
        Assert.Null(journey.SelectedPupil);
        Assert.Null(journey.SelectedPupilId);
        Assert.Null(journey.SelectedPupilLabel);
        Assert.Null(journey.MatchedPupil);
        Assert.Null(journey.MatchedPupilId);
        Assert.Null(journey.MatchedPupilLabel);
        Assert.Null(journey.SelectedResult);
        // Only the EAL pages write these, and OriginCountryLanguageCapture no-ops on a page that
        // has no country question — so an abandoned EAL journey's country data would otherwise
        // ride into the added pupil's request blob.
        Assert.Null(journey.OriginCountryCode);
        Assert.Null(journey.OriginCountryLanguages);
        Assert.Empty(journey.QuestionAnswers);
        Assert.Empty(journey.QuestionHistory);
    }

    // The other side of the same rule: Remove/Include/Merge/IncorrectGrade all open with a pupil
    // search that does this refresh itself, and re-entering one of those mid-journey must keep
    // behaving exactly as it did before the Add journey existed.
    [Fact]
    public async Task Confirm_ForAFlowWithAPupilSearchPage_LeavesTheSessionIdentityAlone()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(Ks4JuneWindow());
        _flowService.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS4June)
            .Returns(FlowWithPupilSearch);
        _session.SaveRequestState(WindowId, SeedStaleIdentity);

        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Merge });

        AssertStaleIdentityIntact(_session.GetRequestState(WindowId));
    }

    private static readonly QuestionFlowConfig FlowWithoutPupilSearch = new()
    {
        FirstPageId = "learner-details",
        Pages = [new JourneyPage { Id = "learner-details", PupilFromAnswers = true }]
    };

    private static readonly QuestionFlowConfig FlowWithPupilSearch = new()
    {
        FirstPageId = "select-pupil",
        Pages =
        [
            new JourneyPage { Id = "select-pupil", Type = PageType.PupilSearch, PupilKey = JourneyPage.PrimaryKey },
            new JourneyPage { Id = "evidence", Type = PageType.EvidenceUpload }
        ]
    };

    private static PupilDto StalePupil(string firstName, string surname) => new()
    {
        Id = Guid.NewGuid(), Firstname = firstName, Surname = surname, Sex = "F",
        DateOfBirth = "01/09/2010", Age = 0, Cypmd_Id = "", Identifier = "A123456789012"
    };

    private static void SeedStaleIdentity(RequestState s)
    {
        s.ReferenceNumber = "CYPMD_KS4June_STALE01";
        s.SelectedPupil = StalePupil("Alice", "Newpupil");
        s.SelectedPupilId = "some-stale-id";
        s.SelectedPupilLabel = "Newpupil, Alice";
        // Left behind by a Merge journey the user started and walked away from.
        s.MatchedPupil = StalePupil("Ian", "Smith");
        s.MatchedPupilId = "some-stale-match-id";
        s.MatchedPupilLabel = "Smith, Ian";
        s.SelectedResult = new StudentResultRecord();
        s.QuestionAnswers = new() { ["first-name"] = new QuestionAnswer { TextValue = "Alice" } };
        s.QuestionHistory = ["learner-details", "admission-details", "evidence"];
        // Left behind by an EAL journey: nothing outside the EAL pages ever writes these.
        s.OriginCountryCode = "FR";
        s.OriginCountryLanguages = ["French"];
    }

    private static void AssertStaleIdentityIntact(RequestState journey)
    {
        Assert.Equal("CYPMD_KS4June_STALE01", journey.ReferenceNumber);
        Assert.NotNull(journey.SelectedPupil);
        Assert.Equal("some-stale-id", journey.SelectedPupilId);
        Assert.Equal("Newpupil, Alice", journey.SelectedPupilLabel);
        Assert.NotNull(journey.MatchedPupil);
        Assert.Equal("some-stale-match-id", journey.MatchedPupilId);
        Assert.Equal("Smith, Ian", journey.MatchedPupilLabel);
        Assert.NotNull(journey.SelectedResult);
        Assert.Equal("FR", journey.OriginCountryCode);
        Assert.NotEmpty(journey.QuestionAnswers);
        Assert.NotEmpty(journey.QuestionHistory);
    }

    private static CheckingWindowDto Post16Window() => new()
    {
        Id = Guid.NewGuid(),
        Title = "W",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(10)
    };

    private static CheckingWindowDto Ks4JuneWindow() => new()
    {
        Id = Guid.NewGuid(),
        Title = "W",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(10)
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

    // ── #318: closed pupil-data checking exercise ────────────────────────────

    [Fact]
    public async Task Index_WhenPupilDataExerciseClosed_RedirectsToCheckYourPupilDataWithAMessage()
    {
        _checkingExercises.Close();

        var result = await _sut.Index(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
        Assert.Equal(
            ClosedExerciseGuard.MessageFor(CheckingExerciseType.PupilData),
            _sut.TempData[ClosedExerciseGuard.TempDataKey]);
    }

    [Fact]
    public async Task Confirm_WhenPupilDataExerciseClosed_IsRejectedAndStartsNoJourney()
    {
        // The bookmarked-URL case the gate exists for: the option was never rendered, but the post
        // still arrives.
        _checkingExercises.Close();

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Remove });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Null(_session.GetRequestState(WindowId).SelectedWhatToChange);
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<ChangeTypeSelectedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_GatesOnPupilData_NotOnTheOuterWindow()
    {
        // A 16-19 window runs results enquiry on its own later dates. Pupil data closing must shut
        // this journey even while the window itself is open.
        _checkingExercises.IsOpen(default!, default)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseType>(1) == CheckingExerciseType.ResultsEnquiry);

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Remove });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(_session.GetRequestState(WindowId).SelectedWhatToChange);
    }

}
