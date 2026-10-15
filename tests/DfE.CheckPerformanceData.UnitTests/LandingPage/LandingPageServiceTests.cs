using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto.
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using ICheckingDataReader = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingDataReader;
using CheckingExerciseReleaseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseReleaseDto;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.LandingPage;

public class LandingPageServiceTests
{
    private readonly ILandingPageRepository _repository = Substitute.For<ILandingPageRepository>();
    private readonly IDfESignInApiClient _dfESignInApiClient = Substitute.For<IDfESignInApiClient>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly ICheckingDataReader _reader = Substitute.For<ICheckingDataReader>();
    private readonly LandingPageService _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    public LandingPageServiceTests()
    {
        _currentUserService.UserId.Returns("user-1");
        _currentUserService.OrganisationId.Returns("org-1");
        var clock = new FakeTimeProvider(Now);
        _sut = new LandingPageService(_repository, clock, _dfESignInApiClient, _currentUserService,
            _reader, new CheckingExerciseService(clock), Substitute.For<ILogger<LandingPageService>>());
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task WhenOrganisationNotFound_ReturnsNull()
    {
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").ReturnsNull();

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhenOrganisationIdMissing_ReturnsNullWithoutCallingDfeApi(string organisationId)
    {
        // Synthetic principals (dev impersonation, plus any future code path that
        // doesn't carry the DfE Sign-In organisation claim) reach this method with an
        // empty OrganisationId. Hitting the DfE Sign-In API with an empty id 500s on
        // the upstream side and bubbles back as an unhandled exception. Guard explicitly.
        _currentUserService.OrganisationId.Returns(organisationId);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Null(result);
        await _dfESignInApiClient.DidNotReceive().GetOrganisationAsync(
            Arg.Any<string>(), Arg.Any<string>());
    }

    // #535: the repository filters windows by the start against the time it is handed, so that time must be the UK
    // wall clock. 16:30 UTC on a summer day is 17:30 in the UK.
    [Fact]
    public async Task The_repository_is_asked_which_windows_are_open_on_the_UK_clock()
    {
        var clock = new UkClockAt("2026-07-15T16:30:00Z");
        var sut = new LandingPageService(_repository, clock, _dfESignInApiClient, _currentUserService,
            _reader, new CheckingExerciseService(clock), Substitute.For<ILogger<LandingPageService>>());
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);
        _repository.GetStartedWindowsAsync(Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>());

        await sut.GetLandingPageDataAsync(CancellationToken.None);

        await _repository.Received(1).GetStartedWindowsAsync(
            new DateTime(2026, 7, 15, 17, 30, 0), org.Laestab, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenWindowHasPupilDataAndMatchingKeyStage_IncludedInOpenWindows()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(keyStage: KeyStages.KS2, hasPupilData: true);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.OpenWindows);
        Assert.Equal(window.Id, result.OpenWindows[0].Id);
    }

    [Fact]
    public async Task WhenWindowHasNoPupilData_ExcludedFromOpenWindows_AddedToNoDataWindows()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(title: "KS2 2026", keyStage: KeyStages.KS2, hasPupilData: false);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Equal("KS2 2026", Assert.Single(result.NoDataWindows).Title);
        Assert.Empty(result.NotValidWindows);
    }

    [Fact]
    public async Task WhenWindowKeyStageDoesNotMatchOrganisation_ExcludedFromOpenWindows_AddedToNotValidWindows()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 12); // KS2 only
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(title: "KS4 2026", keyStage: KeyStages.KS4, hasPupilData: true);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Equal("KS4 2026", Assert.Single(result.NotValidWindows).Title);
        Assert.Empty(result.NoDataWindows);
    }

    [Fact]
    public async Task WhenWindowHasNoDataAndWrongKeyStage_OnlyAppearsInNotValidWindows()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 12); // KS2 only
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(title: "KS4 2026", keyStage: KeyStages.KS4, hasPupilData: false);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Equal("KS4 2026", Assert.Single(result.NotValidWindows).Title);
        Assert.Empty(result.NoDataWindows);
    }

    [Fact]
    public async Task WhenNoExcludedWindows_NoDataWindowsAndNotValidWindowsAreEmpty()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(keyStage: KeyStages.KS2, hasPupilData: true);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.NoDataWindows);
        Assert.Empty(result.NotValidWindows);
    }

    [Fact]
    public async Task MapsOrganisationFieldsCorrectly()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16, name: "Test School", laestab: "1234567",
            urn: "123456", address: "1 School Lane");
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test School", result.OrganisationName);
        Assert.Equal("1234567", result.OrganisationLaestab);
        Assert.Equal("123456", result.OrganisationUrn);
        Assert.Equal("1 School Lane", result.OrganisationAddress);
        Assert.Equal(org.KeyStages, result.KeyStages);
    }

    private static OrganisationDto MakeOrganisation(
        int lowAge = 3,
        int highAge = 16,
        string name = "Test School",
        string laestab = "8604070",
        string urn = "100000",
        string address = "1 Test Street") => new()
    {
        Id = "org-1",
        Urn = urn,
        Name = name,
        Laestab = laestab,
        Address = address,
        StatutoryLowAge = lowAge,
        StatutoryHighAge = highAge
    };

    [Fact]
    public async Task NoDataWindowsAreKeptSeparate_SoEachBannerCanNameItsOwnWindow()
    {
        // A school with a KS4 and a 16-19 window has no single word for a learner, which is why
        // these are a list rather than one joined sentence: the page prints a banner per window.
        var org = MakeOrganisation(lowAge: 11, highAge: 19);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var ks4 = MakeWindow(title: "KS4 June 2026", keyStage: KeyStages.KS4, hasPupilData: false,
            windowType: CheckingWindowType.KS4June);
        var post16 = MakeWindow(title: "16 to 19 2026", keyStage: KeyStages.Post16, hasPupilData: false,
            windowType: CheckingWindowType.Post16);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([ks4, post16]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.NoDataWindows.Count);
        Assert.Equal(["KS4 June 2026", "16 to 19 2026"], result.NoDataWindows.Select(w => w.Title));
        // The window type reaches the page, which is what lets each banner pick its own noun.
        Assert.Equal([CheckingWindowType.KS4June, CheckingWindowType.Post16],
            result.NoDataWindows.Select(w => w.CheckingWindowType));
    }

    // KS4 June has one exercise, so the window's own dates end when it closes. Schools still need
    // to view and download their data for a while after that, and VisibleUntil says how long.
    [Fact]
    public async Task AWindowWhoseOnlyExerciseHasClosed_IsShownWhileItsVisibleUntilIsAhead()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var exercise = Exercise(CheckingExerciseType.PupilData, end: Now.DateTime.AddDays(-1),
            visibleUntil: Now.DateTime.AddDays(14));
        var window = MakeWindowWith("KS4 June 2026", KeyStages.KS2, CheckingWindowType.KS4June, (exercise, true));
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(window.Id, Assert.Single(result.OpenWindows).Id);
    }

    [Fact]
    public async Task AWindowWhoseOnlyExerciseHasClosed_IsHiddenWhenNoVisibleUntilIsSet()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var exercise = Exercise(CheckingExerciseType.PupilData, end: Now.DateTime.AddDays(-1));
        var window = MakeWindowWith("KS4 June 2026", KeyStages.KS2, CheckingWindowType.KS4June, (exercise, true));
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Empty(result.NoDataWindows);
        Assert.Empty(result.NotValidWindows);
    }

    // One live exercise, with or without a file for the school. Most tests only care about that.
    private CheckingWindowDto MakeWindow(
        string title = "Test Window",
        KeyStages keyStage = KeyStages.KS2,
        bool hasPupilData = true,
        CheckingWindowType windowType = CheckingWindowType.KS2) =>
        MakeWindowWith(title, keyStage, windowType, (Exercise(CheckingExerciseType.PupilData), hasPupilData));

    private CheckingWindowDto MakeWindowWith(string title, KeyStages keyStage, CheckingWindowType windowType,
        params (CheckingExerciseDto Exercise, bool HasFile)[] exercises)
    {
        var window = new CheckingWindowDto
        {
            Id = Guid.NewGuid(),
            Title = title,
            KeyStage = keyStage,
            CheckingWindowType = windowType,
            StartDate = Now.DateTime.AddDays(-1),
            EndDate = Now.DateTime.AddDays(30),
            Exercises = [.. exercises.Select(e => e.Exercise)]
        };
        foreach (var (exercise, hasFile) in exercises)
            _reader.HasSchoolDataAsync(window.Id, exercise, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(hasFile);
        return window;
    }

    // Has a live release unless told otherwise: an exercise with no data is not ready.
    private static CheckingExerciseDto Exercise(CheckingExerciseType? type, bool enabled = true,
        DateTime? visibleFrom = null, DateTime? end = null, DateTime? visibleUntil = null,
        bool hasLiveData = true)
    {
        var releaseId = Guid.NewGuid();
        return new()
        {
            Id = Guid.NewGuid(),
            CurrentReleaseId = hasLiveData ? releaseId : null,
            Releases = hasLiveData ? [new CheckingExerciseReleaseDto { Id = releaseId, Number = 1 }] : [],
            ExerciseType = type,
            TabName = "Tab",
            IsEnabled = enabled,
            VisibleFrom = visibleFrom,
            VisibleUntil = visibleUntil,
            StartDate = Now.DateTime.AddDays(-30),
            EndDate = end ?? Now.DateTime.AddDays(30)
        };
    }

    private void ArrangeWindows(params CheckingWindowDto[] windows)
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 19);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);
        _repository.GetStartedWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([.. windows]);
    }

    // A window with no live exercise is not set up yet. Schools must not see it at all: not as a
    // card, and not as a "no data" or "not for your school" message that hints at work in progress.
    [Fact]
    public async Task A_window_with_no_live_exercise_is_hidden_completely()
    {
        var disabled = MakeWindowWith("Not ready", KeyStages.KS2, CheckingWindowType.KS2,
            (Exercise(CheckingExerciseType.PupilData, enabled: false), true));
        var notYetVisible = MakeWindowWith("Not yet", KeyStages.KS4, CheckingWindowType.KS4June,
            (Exercise(CheckingExerciseType.PupilData, visibleFrom: Now.DateTime.AddDays(1)), true));
        ArrangeWindows(disabled, notYetVisible);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Empty(result.NoDataWindows);
        Assert.Empty(result.NotValidWindows);
        await _reader.DidNotReceiveWithAnyArgs().HasSchoolDataAsync(default, default!, default!, default);
    }

    // Enabled and visible is not enough: an exercise with no live release has no data ingested,
    // so its window is not ready either, and must not tell a school it has no data.
    [Fact]
    public async Task A_window_whose_live_exercises_have_no_data_is_hidden_completely()
    {
        var window = MakeWindowWith("Key Stage 4 June", KeyStages.KS4, CheckingWindowType.KS4June,
            (Exercise(CheckingExerciseType.PupilData, hasLiveData: false), false));
        ArrangeWindows(window);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Empty(result.NoDataWindows);
        Assert.Empty(result.NotValidWindows);
        await _reader.DidNotReceiveWithAnyArgs().HasSchoolDataAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task A_window_whose_only_file_is_in_a_live_results_exercise_shows_a_card()
    {
        var window = MakeWindowWith("16 to 19", KeyStages.Post16, CheckingWindowType.Post16,
            (Exercise(CheckingExerciseType.PupilData, enabled: false), false),
            (Exercise(CheckingExerciseType.ResultsEnquiry), true));
        ArrangeWindows(window);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Equal("16 to 19", Assert.Single(result!.OpenWindows).Title);
        Assert.Empty(result.NoDataWindows);
    }

    [Fact]
    public async Task A_file_in_a_disabled_exercise_does_not_count()
    {
        var window = MakeWindowWith("16 to 19", KeyStages.Post16, CheckingWindowType.Post16,
            (Exercise(CheckingExerciseType.PupilData, enabled: false), true),
            (Exercise(CheckingExerciseType.ResultsEnquiry), false));
        ArrangeWindows(window);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Empty(result!.OpenWindows);
        Assert.Equal("16 to 19", Assert.Single(result.NoDataWindows).Title);
    }

    [Fact]
    public async Task A_school_with_no_file_in_any_live_exercise_gets_the_no_data_message()
    {
        var window = MakeWindowWith("KS4 June", KeyStages.KS4, CheckingWindowType.KS4June,
            (Exercise(CheckingExerciseType.PupilData), false),
            (Exercise(type: null), false));
        ArrangeWindows(window);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Empty(result!.OpenWindows);
        Assert.Equal("KS4 June", Assert.Single(result.NoDataWindows).Title);
    }
}
