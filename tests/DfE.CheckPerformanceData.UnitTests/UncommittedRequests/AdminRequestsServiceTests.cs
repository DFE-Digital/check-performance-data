using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.UncommittedRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.UncommittedRequests;

public class AdminRequestsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly IUncommittedRequestsRepository _repository =
        Substitute.For<IUncommittedRequestsRepository>();
    private readonly IRequestStateBlobClient _requestStateBlobClient =
        Substitute.For<IRequestStateBlobClient>();
    private readonly IQuestionFlowService _flowService =
        Substitute.For<IQuestionFlowService>();
    private readonly IQueueService _queueService =
        Substitute.For<IQueueService>();
    private readonly AdminRequestsService _sut;

    public AdminRequestsServiceTests()
    {
        _sut = new AdminRequestsService(
            _repository, _requestStateBlobClient, _flowService, _queueService, new FakeTimeProvider(Now));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task GetAsync_RequestsAllWindowsFromRepository()
    {
        await _sut.GetAsync(CancellationToken.None);

        await _repository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ReturnsRepositoryRows()
    {
        var rows = new List<UncommittedRequestRow>
        {
            new()
            {
                ReferenceNumber = "ABC-123",
                WindowTitle = "KS2 June 2026",
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
        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _sut.GetAsync(CancellationToken.None);

        Assert.Same(rows, result);
    }
}
