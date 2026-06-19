using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.UncommittedRequests;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.UncommittedRequests;

public class UncommittedRequestsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly IUncommittedRequestsRepository _repository =
        Substitute.For<IUncommittedRequestsRepository>();
    private readonly UncommittedRequestsService _sut;

    public UncommittedRequestsServiceTests()
    {
        _sut = new UncommittedRequestsService(_repository, new FakeTimeProvider(Now));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task GetAsync_PassesLocalNowToRepository()
    {
        await _sut.GetAsync(CancellationToken.None);

        await _repository.Received(1)
            .GetForOpenWindowsAsync(new DateTime(2026, 6, 19, 9, 0, 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ReturnsRepositoryRows()
    {
        var rows = new List<UncommittedRequestRow>
        {
            new()
            {
                ReferenceNumber = "ABC-123",
                PupilFirstname = "Ada",
                PupilSurname = "Lovelace",
                RequestTypeDescription = "Remove pupil",
                SubmittedByName = "Head Teacher",
                Submitted = new DateTime(2026, 6, 18, 14, 0, 0),
                Outcome = DecisionStatus.Scrutiny,
                MatchedRule = "SCRUTINY-1",
                DecidedAtUtc = new DateTime(2026, 6, 18, 15, 0, 0, DateTimeKind.Utc)
            }
        };
        _repository.GetForOpenWindowsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _sut.GetAsync(CancellationToken.None);

        Assert.Same(rows, result);
    }
}
