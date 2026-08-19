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
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class WhatToChangeControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly WhatToChangeController _sut;

    public WhatToChangeControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));
        _sut = new WhatToChangeController(_service, _flowService, _analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task Confirm_WhenValid_EmitsChangeTypeSelectedEvent()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = Guid.NewGuid(),
            Title = "W",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10)
        });
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

    [Fact]
    public async Task Confirm_AddOnPost16_WithNoAddFlow_RedirectsToCheckYourPupilData()
    {
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = Guid.NewGuid(),
            Title = "W",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10)
        });
        _flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.Post16)
            .Returns((QuestionFlowConfig?)null);

        var result = await _sut.Confirm(WindowId,
            new WhatToChangeViewModel { WindowId = WindowId, SelectedWhatToChange = WhatToChange.Add });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

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
}
