using System.Text;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
// WindowManagement is imported in full, not aliased: IExerciseTabBuilder.BuildAsync takes its
// CheckingWindowDto (the one carrying TabName/Datasets), which is what Arg.Any<CheckingWindowDto>
// below must match. The service-facing DTO (ICheckYourPupilDataService.GetCheckingWindowAsync)
// is LandingPage's own type of the same name, so it is aliased instead of imported unqualified.
using DfE.CheckPerformanceData.Application.WindowManagement;
using LandingPageCheckingWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #466 Task 23: the tabs the exercise framework draws, and the fallback to the inclusion tabs it
// must never break. The tab-building itself is ExerciseTabBuilder's job (see
// ExerciseTabBuilderTests) — this only checks the controller wires it in and still falls back.
public sealed class CheckYourPupilDataControllerTabsTests
{
    private static readonly Guid WindowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ExerciseId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0);

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly IExerciseTabBuilder _tabBuilder = Substitute.For<IExerciseTabBuilder>();
    private readonly ICheckingDataReader _reader = Substitute.For<ICheckingDataReader>();
    private readonly IExerciseDisplayService _display = new ExerciseDisplayService();

    public CheckYourPupilDataControllerTabsTests()
    {
        // Set once, at construction, so a test's own OrganisationLaestab override (set in its body
        // before Controller() is called) is not clobbered by a default re-applied afterwards.
        _service.GetPupilTableAsync(WindowId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns((PupilTable.Empty, 0));
        _service.GetCheckingWindowAsync(WindowId).Returns(Window());
        _currentUser.OrganisationLaestab.Returns("933/4290");
    }

    private CheckYourPupilDataController Controller()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(new FakeSession()));

        var checkingExercises = new CheckingExerciseService(TimeProvider.System);
        return new CheckYourPupilDataController(_service, _currentUser, _analytics,
            new NextStepsService(checkingExercises), checkingExercises, _tabBuilder, _display, _reader)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task ShowsTheExerciseTabsWhenTheWindowHasThem()
    {
        _tabBuilder.BuildAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([ATab()]);

        var model = Model(await Controller().Index(WindowId));

        Assert.Single(model.CheckingExerciseTabs);
    }

    [Fact]
    public async Task FallsBackToTheInclusionTabsWhenNoExerciseDrawsOne()
    {
        // Every KS2 and KS4 window today. The page must look exactly as it did.
        _tabBuilder.BuildAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var model = Model(await Controller().Index(WindowId));

        Assert.Empty(model.CheckingExerciseTabs);
        Assert.NotEmpty(model.Sections);
    }

    [Fact]
    public async Task DownloadsOneDatasetAsCsv()
    {
        _tabBuilder.BuildAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([ATab()]);

        var result = Assert.IsType<FileContentResult>(
            await Controller().DownloadExerciseDataset(WindowId, ExerciseId, "students-included"));

        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal("students-included.csv", result.FileDownloadName);
    }

    [Fact]
    public async Task DownloadOfADatasetThatIsNotOnThisWindow_IsNotFound()
    {
        // The tabs are rebuilt for this school, so an id from another window reaches nothing.
        _tabBuilder.BuildAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([ATab()]);

        Assert.IsType<NotFoundResult>(
            await Controller().DownloadExerciseDataset(WindowId, Guid.NewGuid(), "students-included"));
    }

    [Fact]
    public async Task DownloadsTheRawJsonForOneExercise()
    {
        // The JSON is what the supplier's file became. An admin chasing a data fault needs it
        // exactly as ingress wrote it, not reshaped by a schema.
        _tabBuilder.BuildAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([ATab()]);
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1"}]"""));

        var result = Assert.IsType<FileContentResult>(
            await Controller().DownloadExerciseJson(WindowId, ExerciseId, CancellationToken.None));

        Assert.Equal("application/json", result.ContentType);
    }

    [Fact]
    public async Task ASignedInUserWithNoSchool_DownloadsNothing()
    {
        _currentUser.OrganisationLaestab.Returns((string?)null);

        Assert.IsType<ForbidResult>(
            await Controller().DownloadExerciseDataset(WindowId, ExerciseId, "students-included"));
    }

    private static CheckYourPupilDataViewModel Model(IActionResult result) =>
        Assert.IsType<CheckYourPupilDataViewModel>(Assert.IsType<ViewResult>(result).Model);

    private static ExerciseTab ATab()
    {
        var dataset = new ExerciseDataset("students-included", "Included students",
            "students-included.csv", [], [], [], true, new HashSet<string>());
        var table = new ExerciseTableView([dataset], dataset, [], null, 0, 0);
        var exercise = new CheckingDataExercise(ExerciseId, WindowId, "Students", "Students", 0,
            CheckingExerciseType.PupilData, KeyStages.Post16, true, null, null,
            Now.AddDays(-1), Now.AddDays(1), Now.AddDays(-1), Now.AddDays(1), null, true, false);
        return new ExerciseTab(exercise, [], true) { Table = table };
    }

    private static LandingPageCheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "W",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(10),
        Exercises =
        [
            new()
            {
                Id = ExerciseId,
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(10)
            }
        ]
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
