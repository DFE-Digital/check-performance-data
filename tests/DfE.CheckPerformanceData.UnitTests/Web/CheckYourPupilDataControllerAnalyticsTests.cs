using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class CheckYourPupilDataControllerAnalyticsTests
{
    private static readonly Guid WindowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly CheckYourPupilDataController _sut;

    public CheckYourPupilDataControllerAnalyticsTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _service.GetIncludedPupilsAsync(WindowId, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(((IReadOnlyList<PupilDto>)[], 0));
        _service.GetNonIncludedPupilsAsync(WindowId, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(((IReadOnlyList<PupilDto>)[], 0));
        _service.GetCheckingWindowAsync(WindowId).Returns(Window());

        _sut = new CheckYourPupilDataController(_service, TimeProvider.System, _currentUser, _analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task Index_WithIncludedSearch_EmitsSearchResults()
    {
        _service.GetIncludedPupilsAsync(WindowId, "smith", 0, 10)
            .Returns(((IReadOnlyList<PupilDto>)[], 3));

        await _sut.Index(WindowId, includedSearch: "smith");

        await _analytics.Received(1).TrackAsync(
            Arg.Is<PupilDataSearchResultsEvent>(e => e.ResultCount == 3 && e.ActiveTab == "included"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_WithoutSearch_DoesNotEmitSearchResults()
    {
        await _sut.Index(WindowId);

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<PupilDataSearchResultsEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NextStep_WhenNoSelection_EmitsValidationError()
    {
        await _sut.NextStep(WindowId, InputVm(next: null));

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e => e.ErrorCount == 1 && e.ErrorCodes.Contains("no_selection")),
            Arg.Any<CancellationToken>());
    }

    private static CheckYourPupilDataViewModel InputVm(NextSteps? next) => new()
    {
        WindowId = WindowId.ToString(),
        IncludedPupils = [], IncludedPupilsPage = 0, IncludedPupilsTotalPages = 0,
        NonIncludedPupils = [], NonIncludedPupilsPage = 0, NonIncludedPupilsTotalPages = 0,
        WindowEndDate = "", WindowEndTime = "", WindowTitle = "",
        IsWindowOpen = true, OrganisationName = "",
        SelectedNextStep = next,
    };

    private static CheckingWindowDto Window() => new()
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
