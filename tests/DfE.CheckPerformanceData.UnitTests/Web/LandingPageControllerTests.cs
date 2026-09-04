using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// AB#298317: the landing page tells a school when pupil-data checking has closed, when the next
// opportunity is, and what it can still do — per window, from the exercises, never from the outer
// window dates (which on a 16-19 window run to the results-enquiry close months later).
//
// Not E2E-testable: dev-impersonated users carry no organisation claim and are challenged by this
// page, so these tests and LandingPageViewRenderTests are the whole pin.
public sealed class LandingPageControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime PupilDataStart = new(2026, 10, 5);
    private static readonly DateTime PupilDataEndPast = new(2026, 10, 16, 17, 0, 0);
    private static readonly DateTime PupilDataEndFuture = new(2026, 10, 30, 17, 0, 0);
    private static readonly DateTime EnquiryEnd = new(2027, 3, 31, 17, 0, 0);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly ILandingPageService _service = Substitute.For<ILandingPageService>();
    private readonly LandingPageController _sut;

    public LandingPageControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(new FakeSession()));

        _sut = new LandingPageController(
            Substitute.For<ILogger<LandingPageController>>(),
            _service,
            new CheckingExerciseService(new FixedTimeProvider(Now)))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static CheckingExerciseDto Exercise(CheckingExerciseType type, DateTime start, DateTime end, int sortOrder) =>
        new() { ExerciseType = type, StartDate = start, EndDate = end, SortOrder = sortOrder };

    private static CheckingWindowDto Post16(DateTime pupilDataEnd, DateTime? nextOpportunity, bool enquiryOpen = true) => new()
    {
        Id = Guid.NewGuid(),
        Title = "16 to 19",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        HasPupilData = true,
        StartDate = PupilDataStart,
        EndDate = EnquiryEnd,
        NextOpportunity = nextOpportunity,
        Exercises =
        [
            Exercise(CheckingExerciseType.PupilData, PupilDataStart, pupilDataEnd, 0),
            Exercise(CheckingExerciseType.ResultsEnquiry, PupilDataStart, enquiryOpen ? EnquiryEnd : PupilDataEndPast, 1)
        ]
    };

    private static CheckingWindowDto Ks4June() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Key Stage 4 June",
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        HasPupilData = true,
        StartDate = PupilDataStart,
        EndDate = PupilDataEndFuture,
        Exercises = [Exercise(CheckingExerciseType.PupilData, PupilDataStart, PupilDataEndFuture, 0)]
    };

    private void Landing(params CheckingWindowDto[] windows) =>
        _service.GetLandingPageDataAsync(Arg.Any<CancellationToken>()).Returns(new LandingPageResult
        {
            OrganisationName = "Teignmouth Community School",
            OrganisationLaestab = "878/4120",
            OrganisationUrn = "136495",
            KeyStages = [],
            OpenWindows = [.. windows]
        });

    private async Task<LandingPageViewModel> Model()
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(CancellationToken.None));
        return Assert.IsType<LandingPageViewModel>(view.Model);
    }

    // ── Cards ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_open_pupil_data_exercise_keeps_the_deadline_sentence_from_its_own_end_date()
    {
        Landing(Post16(PupilDataEndFuture, null));

        var card = Assert.Single((await Model()).OpenWindows);

        Assert.True(card.IsPupilDataOpen);
        Assert.Equal("5pm", card.PupilDataEndTime);
        Assert.Equal("Friday 30 October 2026", card.PupilDataEndDate);
        Assert.True(card.IsResultsEnquiryOpen);
        Assert.Equal("31 March 2027", card.ResultsEnquiryEndDate);
        Assert.Equal("student", card.LearnerNoun.Singular);
    }

    [Fact]
    public async Task A_closed_pupil_data_exercise_gives_the_card_the_amendment_range()
    {
        Landing(Post16(PupilDataEndPast, null));

        var card = Assert.Single((await Model()).OpenWindows);

        Assert.False(card.IsPupilDataOpen);
        Assert.Equal("5 October", card.PupilDataRangeStart);
        Assert.Equal("16 October 2026", card.PupilDataRangeEnd);
        Assert.True(card.IsResultsEnquiryOpen);
    }

    [Fact]
    public async Task A_single_exercise_window_has_no_results_enquiry_facts()
    {
        Landing(Ks4June());

        var card = Assert.Single((await Model()).OpenWindows);

        Assert.True(card.IsPupilDataOpen);
        Assert.False(card.IsResultsEnquiryOpen);
        Assert.Null(card.ResultsEnquiryEndDate);
        Assert.Equal("pupil", card.LearnerNoun.Singular);
    }

    // ── Closed banners ───────────────────────────────────────────────────────

    [Fact]
    public async Task Pupil_data_closed_puts_one_banner_on_the_page_naming_the_next_opportunity()
    {
        Landing(Post16(PupilDataEndPast, new DateTime(2027, 10, 1)));

        var banner = Assert.Single((await Model()).ClosedWindows);

        Assert.Equal("16 to 19", banner.Title);
        Assert.Equal("October 2027", banner.NextOpportunity);
        Assert.True(banner.IsResultsEnquiryOpen);
        Assert.Equal("student", banner.LearnerNoun.Singular);
    }

    [Fact]
    public async Task No_next_opportunity_leaves_the_banner_sentence_null_not_blank()
    {
        Landing(Post16(PupilDataEndPast, null));

        Assert.Null(Assert.Single((await Model()).ClosedWindows).NextOpportunity);
    }

    [Fact]
    public async Task Pupil_data_still_open_means_no_banner()
    {
        Landing(Post16(PupilDataEndFuture, new DateTime(2027, 10, 1)), Ks4June());

        Assert.Empty((await Model()).ClosedWindows);
    }

    [Fact]
    public async Task The_banner_knows_when_results_enquiry_has_closed_too()
    {
        // Still inside the outer window (a window with both exercises shut but its outer pair
        // bracketing now — possible if an admin edits dates). Card stays; banner offers the
        // read-only sentence.
        var window = Post16(PupilDataEndPast, null, enquiryOpen: false);
        Landing(window);

        Assert.False(Assert.Single((await Model()).ClosedWindows).IsResultsEnquiryOpen);
    }

    [Fact]
    public async Task One_banner_per_closed_window()
    {
        var first = Post16(PupilDataEndPast, new DateTime(2027, 10, 1));
        var second = Post16(PupilDataEndPast, new DateTime(2027, 11, 1));
        Landing(first, second, Ks4June());

        var banners = (await Model()).ClosedWindows;

        Assert.Equal(2, banners.Count);
        Assert.Equal(["October 2027", "November 2027"], banners.Select(b => b.NextOpportunity));
    }

    // ── Session plumbing for the hostless controller ─────────────────────────

    private sealed class TestSessionFeature(ISession session) : ISessionFeature
    {
        public ISession Session { get; set; } = session;
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
