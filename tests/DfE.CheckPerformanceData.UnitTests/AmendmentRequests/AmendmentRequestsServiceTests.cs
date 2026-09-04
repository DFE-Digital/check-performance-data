using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
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
    private readonly IRequestStateBlobClient _blobClient = Substitute.For<IRequestStateBlobClient>();
    private readonly ILogger<AmendmentRequestsService> _logger = Substitute.For<ILogger<AmendmentRequestsService>>();
    private readonly AmendmentRequestsService _sut;

    public AmendmentRequestsServiceTests()
    {
        _currentUser.OrganisationUrn.Returns("100001");
        _requestRepo.GetSubmittedRequestsAsync(Arg.Any<Guid>(), Arg.Any<long>()).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(Arg.Any<Guid>(), Arg.Any<long>()).Returns([]);
        _sut = new AmendmentRequestsService(_windowService, _requestRepo, _checkingExercises, _currentUser, _blobClient, _logger);
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

    // The row alone can't fill the Issues table: CYPMD id and qualification live only in the
    // journey blob the submission saved. A regression here renders blank cells for every issue.
    [Fact]
    public async Task GetAmendmentRequestsAsync_BuildsIssueRowsFromRowAndJourneyBlob()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L)
            .Returns([Enquiry("REF-1", "Alice", "Smith")]);
        _blobClient.GetAsync(WindowId, "REF-1").Returns(new RequestState
        {
            SelectedPupil = new PupilDto
            {
                Firstname = "Alice", Surname = "Smith", Id = Guid.NewGuid(), Sex = "F",
                DateOfBirth = "2008-01-01", Age = 18, Cypmd_Id = "500001", Identifier = "ULN500001"
            },
            SelectedQualification = new QualificationReference
                { Qan = "6037116X", QualificationTitle = "ABRSM level 3 certificate in practical music (Grade 8)" }
        });

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        var row = Assert.Single(result.IssueRows);
        Assert.Equal("Alice Smith", row.PupilName);
        Assert.Equal("500001", row.CypmdId);
        Assert.Equal("Missing qualification", row.TypeLabel);
        Assert.Equal("ABRSM level 3 certificate in practical music (Grade 8)", row.QualificationText);
        Assert.True(result.HasAnyIssues);
    }

    // Incorrect-grade and result-does-not-belong journeys store SelectedResult, not
    // SelectedQualification; the qualification cell must come from the result record's name.
    [Fact]
    public async Task GetAmendmentRequestsAsync_IssueQualificationFallsBackToSelectedResult()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        var enquiry = new SubmittedRequestData
        {
            PupilFirstname = "Noor", PupilSurname = "Farah",
            RequestType = RequestType.ResultsEnquiry,
            RequestTypeDescription = "Results enquiry - Incorrect grade",
            ReferenceNumber = "REF-2", Status = RequestStatus.SubmittedUnCommitted, Submitted = DateTime.UtcNow
        };
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L).Returns([enquiry]);
        _blobClient.GetAsync(WindowId, "REF-2").Returns(new RequestState
        {
            SelectedResult = new StudentResultRecord { QualificationName = "GCSE (9-1) Bus. Studs:Single" }
        });

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        var row = Assert.Single(result.IssueRows);
        Assert.Equal("Incorrect grade", row.TypeLabel);
        Assert.Equal("GCSE (9-1) Bus. Studs:Single", row.QualificationText);
        Assert.Equal("", row.CypmdId);
    }

    // A missing or unreadable blob must degrade to empty cells, never fail the whole page — the
    // row is the record of truth and the school must still see that the enquiry exists.
    [Fact]
    public async Task GetAmendmentRequestsAsync_MissingJourneyBlobLeavesEnrichmentCellsEmpty()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L)
            .Returns([Enquiry("REF-3", "Billy", "Brown")]);
        _blobClient.GetAsync(WindowId, "REF-3").Returns((RequestState?)null);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        var row = Assert.Single(result.IssueRows);
        Assert.Equal("Billy Brown", row.PupilName);
        Assert.Equal("", row.CypmdId);
        Assert.Equal("", row.QualificationText);
    }

    // A blob read that throws (corrupt document, storage outage) must degrade the same way a
    // missing blob does — the row stays visible with empty cells — and the failure is logged by
    // reference number only, never pupil data, so an operator can see it without a PII leak.
    [Fact]
    public async Task GetAmendmentRequestsAsync_BlobReadThatThrowsLeavesEnrichmentCellsEmpty()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L)
            .Returns([Enquiry("REF-4", "Cara", "Davies")]);
        _blobClient.GetAsync(WindowId, "REF-4").Returns(Task.FromException<RequestState?>(new InvalidOperationException("blob storage unavailable")));

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        var row = Assert.Single(result.IssueRows);
        Assert.Equal("Cara Davies", row.PupilName);
        Assert.Equal("", row.CypmdId);
        Assert.Equal("", row.QualificationText);
        // The negative half is the real pin: adding {PupilName} "for debuggability" would land
        // PII in the Serilog sinks while a contains-reference-only assertion stayed green.
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("REF-4")
                && !o.ToString()!.Contains("Cara")
                && !o.ToString()!.Contains("Davies")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_SearchFiltersByFirstOrLastNameCaseInsensitively()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L).Returns(
        [
            Enquiry("REF-A", "Alice", "Smith"),
            Enquiry("REF-B", "Billy", "Brown"),
            Enquiry("REF-C", "Chloe", "Alison")
        ]);

        // "ali" hits Alice (first name) and Alison (last name), never Billy Brown.
        var result = await _sut.GetAmendmentRequestsAsync(WindowId, issueSearch: "  ALI ");

        Assert.Equal(["REF-A", "REF-C"], result.IssueRows.Select(r => r.ReferenceNumber));
        Assert.True(result.HasAnyIssues);
    }

    // HasAnyIssues reports the pre-search population: the view uses it to choose between the
    // "no enquiries at all" empty state and the "search matched nothing" message. Conflating them
    // would tell a school with enquiries that it has none.
    [Fact]
    public async Task GetAmendmentRequestsAsync_NoMatchSearchKeepsHasAnyIssuesTrue()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L)
            .Returns([Enquiry("REF-A", "Alice", "Smith")]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId, issueSearch: "zzz");

        Assert.Empty(result.IssueRows);
        Assert.True(result.HasAnyIssues);
    }

    // Blob loads are IO per row; filtering first keeps a search over a long list from fetching
    // blobs it will immediately discard.
    [Fact]
    public async Task GetAmendmentRequestsAsync_OnlyLoadsBlobsForRowsThatSurviveTheSearch()
    {
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(DateTime.UtcNow));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);
        _requestRepo.GetSubmittedResultsEnquiriesAsync(WindowId, 100001L).Returns(
        [
            Enquiry("REF-A", "Alice", "Smith"),
            Enquiry("REF-B", "Billy", "Brown")
        ]);

        await _sut.GetAmendmentRequestsAsync(WindowId, issueSearch: "alice");

        await _blobClient.Received(1).GetAsync(WindowId, "REF-A");
        await _blobClient.DidNotReceive().GetAsync(WindowId, "REF-B");
    }

    private static SubmittedRequestData Enquiry(string reference, string first, string last, DateTime? submitted = null) => new()
    {
        PupilFirstname = first,
        PupilSurname = last,
        RequestType = RequestType.ResultsEnquiry,
        RequestTypeDescription = "Results enquiry - Missing qualification",
        ReferenceNumber = reference,
        Status = RequestStatus.SubmittedUnCommitted,
        Submitted = submitted ?? DateTime.UtcNow
    };

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
