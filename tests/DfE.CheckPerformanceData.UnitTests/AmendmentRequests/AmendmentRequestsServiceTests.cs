using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class AmendmentRequestsServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ICheckYourPupilDataService _windowService = Substitute.For<ICheckYourPupilDataService>();
    private readonly IRequestRepository _requestRepo = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly AmendmentRequestsService _sut;

    public AmendmentRequestsServiceTests()
    {
        _currentUser.OrganisationUrn.Returns("100001");
        _sut = new AmendmentRequestsService(_windowService, _requestRepo, _currentUser);
    }

    [Fact]
    public async Task GetAmendmentRequestsAsync_ReturnsWindowEndDate()
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _windowService.GetCheckingWindowAsync(WindowId).Returns(Window(endDate));
        _requestRepo.GetAmendmentRequestsAsync(WindowId, 100001L).Returns([]);

        var result = await _sut.GetAmendmentRequestsAsync(WindowId);

        Assert.Equal(endDate, result.WindowEndDate);
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
        Assert.Equal("Remove - Permanently left England", result.Rows[0].RequestType);
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

    private static CheckingWindowDto Window(DateTime endDate) => new()
    {
        Id = WindowId,
        Title = "KS4 2026",
        EndDate = endDate,
        StartDate = endDate.AddMonths(-3),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June
    };

    private static AmendmentRequestData RequestData(
        string? firstname, string? surname, string requestType,
        RequestStatus status, string referenceNumber) => new()
    {
        PupilFirstname = firstname,
        PupilSurname = surname,
        RequestType = requestType,
        Status = status,
        ReferenceNumber = referenceNumber
    };
}
