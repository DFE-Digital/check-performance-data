using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AdminRequests;

// The admin requests page is scoped to ONE checking window and filterable by that window's
// checking exercises. A service-wide list of every request in every window could not answer the
// question an admin actually has, which is always about one window.
public class AdminRequestsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WindowId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly IAdminRequestsRepository _repository =
        Substitute.For<IAdminRequestsRepository>();
    private readonly IRequestStateBlobClient _requestStateBlobClient =
        Substitute.For<IRequestStateBlobClient>();
    private readonly IQuestionFlowService _flowService =
        Substitute.For<IQuestionFlowService>();
    private readonly IQueueService _queueService =
        Substitute.For<IQueueService>();
    private readonly IWindowService _windowService =
        Substitute.For<IWindowService>();
    private readonly AdminRequestsService _sut;

    public AdminRequestsServiceTests()
    {
        _repository.GetForWindowAsync(
                Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _sut = new AdminRequestsService(
            _repository, _requestStateBlobClient, _flowService, _queueService, _windowService,
            new FakeTimeProvider(Now));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static CheckingExerciseDto Exercise(CheckingExerciseType type, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseType = type,
        StartDate = new DateTime(2026, 6, 1),
        EndDate = new DateTime(2026, 7, 1),
        SortOrder = sortOrder
    };

    // A 16-19 shaped window, whose two exercises are deliberately declared out of SortOrder so the
    // ordering assertion below is not passing by accident.
    private void SetupWindow(params CheckingExerciseDto[] exercises) =>
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "16 to 19 2026",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 7, 1),
            Exercises = [.. exercises]
        });

    [Fact]
    public async Task GetForWindowAsync_IsNullWhenNoWindowHasThatId()
    {
        // The caller reaches this from a URL, so a stale or hand-typed id must not 500.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns((CheckingWindowDto?)null);

        Assert.Null(await _sut.GetForWindowAsync(WindowId, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetForWindowAsync_AsksTheRepositoryForThatWindowOnly()
    {
        SetupWindow(Exercise(CheckingExerciseType.PupilData, 0));

        await _sut.GetForWindowAsync(WindowId, null, CancellationToken.None);

        await _repository.Received(1).GetForWindowAsync(WindowId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForWindowAsync_OffersTheWindowsOwnExercisesInSortOrder()
    {
        SetupWindow(
            Exercise(CheckingExerciseType.ResultsEnquiry, 1),
            Exercise(CheckingExerciseType.PupilData, 0));

        var result = await _sut.GetForWindowAsync(WindowId, null, CancellationToken.None);

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            result!.Exercises);
    }

    [Fact]
    public async Task GetForWindowAsync_PassesTheSelectedExerciseToTheRepository()
    {
        SetupWindow(
            Exercise(CheckingExerciseType.PupilData, 0),
            Exercise(CheckingExerciseType.ResultsEnquiry, 1));

        var result = await _sut.GetForWindowAsync(
            WindowId, CheckingExerciseType.ResultsEnquiry, CancellationToken.None);

        Assert.Equal(CheckingExerciseType.ResultsEnquiry, result!.SelectedExercise);
        await _repository.Received(1).GetForWindowAsync(
            WindowId, CheckingExerciseType.ResultsEnquiry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForWindowAsync_DropsAFilterForAnExerciseTheWindowDoesNotRun()
    {
        // Honouring it would show an empty table that reads as "no requests" when the truthful
        // answer is "this window has no results enquiry" — and the option was never offered.
        SetupWindow(Exercise(CheckingExerciseType.PupilData, 0));

        var result = await _sut.GetForWindowAsync(
            WindowId, CheckingExerciseType.ResultsEnquiry, CancellationToken.None);

        Assert.Null(result!.SelectedExercise);
        await _repository.Received(1).GetForWindowAsync(WindowId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForWindowAsync_ReturnsRepositoryRowsAndTheWindowTitle()
    {
        SetupWindow(Exercise(CheckingExerciseType.PupilData, 0));
        var rows = new List<AdminRequestRow>
        {
            new()
            {
                ReferenceNumber = "ABC-123",
                OrganisationUrn = 123456,
                PupilFirstname = "Ada",
                PupilSurname = "Lovelace",
                RequestTypeDescription = "Remove pupil",
                Status = RequestStatus.SubmittedUnCommitted,
                SubmittedByName = "Head Teacher",
                Submitted = new DateTime(2026, 6, 18, 14, 0, 0),
                Outcome = DecisionStatus.Scrutiny,
                MatchedRule = "SCRUTINY-1",
                DecidedAtUtc = new DateTime(2026, 6, 18, 15, 0, 0, DateTimeKind.Utc)
            }
        };
        _repository.GetForWindowAsync(WindowId, null, Arg.Any<CancellationToken>()).Returns(rows);

        var result = await _sut.GetForWindowAsync(WindowId, null, CancellationToken.None);

        Assert.Same(rows, result!.Rows);
        Assert.Equal("16 to 19 2026", result.WindowTitle);
        Assert.Equal(WindowId, result.WindowId);
    }
}
