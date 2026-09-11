using System.IO.Compression;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The Results tab is a third PupilTableSection, built only when the service says the window has
// one. It sits on the view model as ResultsSection (not in Sections) so that on Post16, where the
// pupil sections stack inside one tab, it renders as a sibling tab rather than a third stack.
public sealed class CheckYourPupilDataResultsTabTests
{
    private static readonly Guid WindowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly PupilTable ResultsTable = new(["Last name", "Subject"], [["Smith", "Maths"]]);

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly CheckYourPupilDataController _sut;

    public CheckYourPupilDataResultsTabTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(new FakeSession()));

        _service.GetPupilTableAsync(WindowId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns((PupilTable.Empty, 0));
        _service.GetPupilCsvAsync(WindowId, Arg.Any<bool>()).Returns(PupilTable.Empty);
        _service.GetCheckingWindowAsync(WindowId).Returns(Window());

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.OrganisationUrn.Returns("136309");
        var checkingExercises = new CheckingExerciseService(TimeProvider.System);
        _sut = new CheckYourPupilDataController(
            _service, currentUser, _analytics, new NextStepsService(checkingExercises), checkingExercises)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private void ResultsTabExists()
    {
        _service.GetResultsTableAsync(WindowId, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns((ResultsTable, 1));
        _service.GetResultsCsvAsync(WindowId).Returns(ResultsTable);
    }

    private async Task<CheckYourPupilDataViewModel> IndexModel(int resultsPage = 0, string? resultsSearch = null)
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(WindowId, resultsPage: resultsPage, resultsSearch: resultsSearch));
        return Assert.IsType<CheckYourPupilDataViewModel>(view.Model);
    }

    [Fact]
    public async Task No_results_tab_when_the_service_has_none_for_the_window()
    {
        var model = await IndexModel();

        Assert.Null(model.ResultsSection);
        Assert.Equal(2, model.Sections.Count);
    }

    [Fact]
    public async Task Results_section_is_built_from_the_service_table()
    {
        ResultsTabExists();

        var model = await IndexModel(resultsPage: 2, resultsSearch: "smi");

        Assert.NotNull(model.ResultsSection);
        var section = model.ResultsSection;
        Assert.Equal("results", section.Key);
        Assert.Equal("Results", section.TabLabel);
        // The results search also matches subject, and the label must say so; the pupil sections
        // keep their default wording.
        Assert.Equal("Search for a student by name or subject", section.SearchLabel);
        Assert.All(model.Sections, s => Assert.Equal("Search for a student by first or last name", s.SearchLabel));
        Assert.Equal(nameof(CheckYourPupilDataController.DownloadResults), section.DownloadAction);
        Assert.Same(ResultsTable, section.Table);
        Assert.Equal(2, section.Page);
        Assert.Equal("smi", section.Search);
        Assert.Equal(1, section.TotalPages);
        // Still not in Sections: the pupil tabs and the results tab are different axes.
        Assert.Equal(2, model.Sections.Count);
        await _service.Received(1).GetResultsTableAsync(WindowId, "smi", 2, 10);
    }

    [Fact]
    public async Task Results_search_emits_the_search_results_event_for_the_results_tab()
    {
        ResultsTabExists();

        await IndexModel(resultsSearch: "smi");

        await _analytics.Received(1).TrackAsync(
            Arg.Is<PupilDataSearchResultsEvent>(e => e.ResultCount == 1 && e.ActiveTab == "results"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadResults_returns_the_csv()
    {
        ResultsTabExists();

        var file = Assert.IsType<FileContentResult>(await _sut.DownloadResults(WindowId));

        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("results-136309-", file.FileDownloadName);
        Assert.Contains("Smith", System.Text.Encoding.UTF8.GetString(file.FileContents));
    }

    [Fact]
    public async Task DownloadResults_redirects_to_the_page_when_the_window_has_no_results_tab()
    {
        var redirect = Assert.IsType<RedirectToActionResult>(await _sut.DownloadResults(WindowId));

        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task DownloadAll_adds_the_results_csv_to_the_zip_only_when_the_tab_exists()
    {
        Assert.Equal(2, await ZipEntryCount());

        ResultsTabExists();

        Assert.Equal(3, await ZipEntryCount());
    }

    private async Task<int> ZipEntryCount()
    {
        var file = Assert.IsType<FileContentResult>(await _sut.DownloadAll(WindowId));
        using var zip = new ZipArchive(new MemoryStream(file.FileContents));
        return zip.Entries.Count;
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId,
        Title = "W",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(10),
        Exercises = []
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
