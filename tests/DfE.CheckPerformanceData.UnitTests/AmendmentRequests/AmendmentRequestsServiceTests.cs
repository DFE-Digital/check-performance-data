using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this file
// already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class AmendmentRequestsServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ICheckYourPupilDataService _windowService = Substitute.For<ICheckYourPupilDataService>();
    private readonly IRequestRepository _requestRepo = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly AmendmentRequestsService _sut;

    public AmendmentRequestsServiceTests()
    {
        _currentUser.OrganisationUrn.Returns("100001");
        _requestRepo.GetSubmittedRequestsAsync(Arg.Any<Guid>(), Arg.Any<long>()).Returns([]);
        _sut = new AmendmentRequestsService(_windowService, _requestRepo, _checkingExercises, _currentUser);
    }

    // #320: a deadline belongs to a checking exercise, not to the window. The window's own end is
    // the union of its exercises, so on a 16-19 window it is the results-enquiry close — months
    // after pupil data shuts. Reading it told a school it still had time to amend pupil data.
    [Fact]
    public async Task GetAmendmentRequestsAsync_ReturnsOneDeadlinePerCheckingExercise()
    {
        var pupilDataEnd = new DateTime(2026, 10, 18, 17, 0, 0);
        var resultsEnd = new DateTime(2027, 3, 31, 17, 0, 0);
        _windowService.GetCheckingWindowAsync(WindowId).Returns(
            Window(resultsEnd,
                Exercise(CheckingExerciseType.PupilData, pupilDataEnd, sortOrder: 0),
                Exercise(CheckingExerciseType.ResultsEnquiry, resultsEnd, sortOrder: 1)));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            result.Deadlines.Select(d => d.Exercise));
        Assert.Equal(pupilDataEnd, result.Deadlines[0].EndDate);
        Assert.Equal(resultsEnd, result.Deadlines[1].EndDate);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_OrdersTheDeadlinesBySortOrder()
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _windowService.GetCheckingWindowAsync(WindowId).Returns(
            Window(endDate,
                Exercise(CheckingExerciseType.ResultsEnquiry, endDate, sortOrder: 1),
                Exercise(CheckingExerciseType.PupilData, endDate, sortOrder: 0)));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            result.Deadlines.Select(d => d.Exercise));
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_MarksEachDeadlineOpenOrClosed()
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _windowService.GetCheckingWindowAsync(WindowId).Returns(
            Window(endDate,
                Exercise(CheckingExerciseType.PupilData, endDate, sortOrder: 0),
                Exercise(CheckingExerciseType.ResultsEnquiry, endDate, sortOrder: 1)));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _checkingExercises.IsOpen(default!, default)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseType>(1) == CheckingExerciseType.PupilData);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.True(result.Deadlines[0].IsOpen);
        Assert.False(result.Deadlines[1].IsOpen);
    }

    // A window with no exercise rows shows no deadline at all. Inventing one from the window's own
    // dates is what #320 removed, so the empty answer must stay empty.
    [Fact]
    public async Task GetAmendmentRequestsAsync_AWindowWithNoExercises_HasNoDeadlines()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Empty(result.Deadlines);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_MapsRequestsToRows()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns(
        [
            RequestData("Jane", "Smith", "Remove - Permanently left England", RequestStatus.ReadyToSubmit, "REF001"),
            RequestData("John", "Doe", "Social care situation", RequestStatus.InProgress, "REF002")
        ]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Jane Smith", result.Rows[0].PupilName);
        Assert.Equal("Remove - Permanently left England", result.Rows[0].RequestTypeDescription);
        Assert.Equal(RequestStatus.ReadyToSubmit, result.Rows[0].Status);
        Assert.Equal("REF001", result.Rows[0].ReferenceNumber);
        Assert.Equal("John Doe", result.Rows[1].PupilName);
        Assert.Equal(RequestStatus.InProgress, result.Rows[1].Status);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_WhenNoPupilDetails_UsesFallbackName()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns(
        [
            RequestData(null, null, "Confirm Pupil Data Declaration", RequestStatus.ReadyToSubmit, "REF003")
        ]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal("N/A", result.Rows[0].PupilName);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_WhenOnlyFirstnamePresent_ReturnsFirstname()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns(
        [
            RequestData("Jane", null, "Remove - Permanently left England", RequestStatus.InProgress, "REF004")
        ]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal("Jane", result.Rows[0].PupilName);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_WhenNoRequests_ReturnsEmptyRows()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_PassesOrgUrnToRepository()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(Arg.Any<Guid>(), Arg.Any<long>()).Returns([]);

        await _sut.GetAmendmentRequestsAsync(WindowId);

        await _requestRepo.Received(1).GetAmendmentRequestsAsync(WindowId, 100001L);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_MapsSubmittedRequestsToRows()
    {
        var submitted = new DateTime(2026, 6, 16, 9, 30, 0);
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedRequestsAsync(WindowId, 100001L).Returns(
        [
            SubmittedData("Jane", "Smith", "Remove - Permanently left England", "REF010", submitted,
                RequestStatus.Withdrawn)
        ]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Single(result.SubmittedRows);
        Assert.Equal("Jane Smith", result.SubmittedRows[0].PupilName);
        Assert.Equal("Remove - Permanently left England", result.SubmittedRows[0].RequestTypeDescription);
        Assert.Equal("REF010", result.SubmittedRows[0].ReferenceNumber);
        Assert.Equal(RequestStatus.Withdrawn, result.SubmittedRows[0].Status);
        Assert.Equal(submitted, result.SubmittedRows[0].Submitted);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_WhenSubmittedHasNoPupilDetails_UsesFallbackName()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedRequestsAsync(WindowId, 100001L).Returns(
        [
            SubmittedData(null, null, "Confirm Pupil Data Declaration", "REF011", DateTime.UtcNow)
        ]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal("N/A", result.SubmittedRows[0].PupilName);
    }

    private static CheckingWindowDto Window(DateTime endDate, params CheckingExerciseDto[] exercises) => new()
    {
        Id = WindowId,
        Title = "KS4 2026",
        EndDate = endDate,
        StartDate = endDate.AddMonths(-3),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        Exercises = [.. exercises]
    };

    private static CheckingExerciseDto Exercise(
        CheckingExerciseType type, DateTime endDate, int sortOrder) => new()
    {
        ExerciseType = type,
        StartDate = endDate.AddMonths(-3),
        EndDate = endDate,
        SortOrder = sortOrder
    };

    private static AmendmentRequestData RequestData(
        string? firstname, string? surname, string requestType,
        RequestStatus status, string referenceNumber) => new()
    {
        PupilFirstname = firstname,
        PupilSurname = surname,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = requestType,
        Status = status,
        ReferenceNumber = referenceNumber
    };

    private static SubmittedRequestData SubmittedData(
        string? firstname, string? surname, string requestType,
        string referenceNumber, DateTime submitted,
        RequestStatus status = RequestStatus.SubmittedUnCommitted) => new()
    {
        PupilFirstname = firstname,
        PupilSurname = surname,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = requestType,
        ReferenceNumber = referenceNumber,
        Status = status,
        Submitted = submitted
    };
}
