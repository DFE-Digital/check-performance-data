using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
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
        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = null });

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<ChangeTypeSelectedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_WhenNoSelection_EmitsValidationError()
    {
        await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = null });

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e => e.ErrorCount == 1 && e.ErrorCodes.Contains("no_selection")),
            Arg.Any<CancellationToken>());
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
