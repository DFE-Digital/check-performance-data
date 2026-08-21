using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Dashboard;

public class DashboardServiceTests
{
    private readonly IPupilDataBlobClient _blobClient = Substitute.For<IPupilDataBlobClient>();
    private readonly IOrganisationLoginRepository _logins = Substitute.For<IOrganisationLoginRepository>();
    private readonly IDashboardRequestRepository _requests = Substitute.For<IDashboardRequestRepository>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private static CheckingWindowDto Window(Guid id) => new()
    {
        Id = id,
        Title = "KS4 June 2026",
        StartDate = new DateTime(2026, 06, 01),
        EndDate = new DateTime(2026, 06, 30, 17, 0, 0),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
    };
    // NOTE: CheckingWindowDto has required members beyond these; initialise whatever the
    // compiler demands with neutral values — the service only reads Id, Title, StartDate, EndDate.

    private DashboardService CreateSut() => new(
        _blobClient, _logins, _requests, _cache,
        Options.Create(new DashboardSettings { RefreshMinutes = 15 }));

    [Fact]
    public async Task GetMetricsAsync_ComputesAllNineFigures()
    {
        var windowId = Guid.NewGuid();
        // 4 eligible schools; 2 log in (plus an LA login that is not eligible and must not count);
        // school 1111111 (urn 111) submitted, school 2222222 (urn 222) logged in but did not.
        _blobClient.ListSchoolLaestabsAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(["1111111", "2222222", "3333333", "4444444"]);
        _logins.GetDistinctLoginsBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new SchoolLogin(111, "1111111"),
                new SchoolLogin(222, "2222222"),
                new SchoolLogin(999, "9999999"), // logged in but not eligible → ignored
            ]);
        _requests.GetRequestAggregatesAsync(windowId, Arg.Any<CancellationToken>())
            .Returns(new DashboardRequestAggregates
            {
                TotalRequests = 10,
                AutoApproved = 4,
                AutoRejected = 2,
                RequiringScrutiny = 3,
                SubmittingUrns = [111],
            });

        var metrics = await CreateSut().GetMetricsAsync(Window(windowId));

        Assert.Equal(4, metrics.EligibleSchools);
        Assert.Equal(2, metrics.LoggedIn);
        Assert.Equal(2, metrics.NotLoggedIn);
        Assert.Equal(1, metrics.SchoolsSubmitted);
        Assert.Equal(1, metrics.LoggedInNotSubmitted);
        Assert.Equal(10, metrics.TotalRequests);
        Assert.Equal(4, metrics.AutoApproved);
        Assert.Equal(2, metrics.AutoRejected);
        Assert.Equal(3, metrics.RequiringScrutiny);
    }

    [Fact]
    public async Task GetMetricsAsync_QueriesLoginsOverTheWindowDates_AsUtc()
    {
        var windowId = Guid.NewGuid();
        _blobClient.ListSchoolLaestabsAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>()).Returns([]);
        _logins.GetDistinctLoginsBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _requests.GetRequestAggregatesAsync(windowId, Arg.Any<CancellationToken>())
            .Returns(EmptyAggregates());

        await CreateSut().GetMetricsAsync(Window(windowId));

        await _logins.Received(1).GetDistinctLoginsBetweenAsync(
            Arg.Is<DateTime>(d => d == new DateTime(2026, 06, 01) && d.Kind == DateTimeKind.Utc),
            Arg.Is<DateTime>(d => d.Kind == DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMetricsAsync_SecondCallWithinTtl_ServesFromCache()
    {
        var windowId = Guid.NewGuid();
        _blobClient.ListSchoolLaestabsAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>()).Returns([]);
        _logins.GetDistinctLoginsBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _requests.GetRequestAggregatesAsync(windowId, Arg.Any<CancellationToken>())
            .Returns(EmptyAggregates());
        var sut = CreateSut();

        var first = await sut.GetMetricsAsync(Window(windowId));
        var second = await sut.GetMetricsAsync(Window(windowId));

        Assert.Same(first, second);
        await _requests.Received(1).GetRequestAggregatesAsync(windowId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMetricsAsync_DifferentWindows_AreCachedIndependently()
    {
        var windowA = Guid.NewGuid();
        var windowB = Guid.NewGuid();
        _blobClient.ListSchoolLaestabsAsync(Arg.Any<Guid>(), CheckingExerciseType.PupilData, Arg.Any<CancellationToken>()).Returns([]);
        _logins.GetDistinctLoginsBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _requests.GetRequestAggregatesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAggregates());
        var sut = CreateSut();

        await sut.GetMetricsAsync(Window(windowA));
        await sut.GetMetricsAsync(Window(windowB));

        await _requests.Received(1).GetRequestAggregatesAsync(windowA, Arg.Any<CancellationToken>());
        await _requests.Received(1).GetRequestAggregatesAsync(windowB, Arg.Any<CancellationToken>());
    }

    // A misconfigured Dashboard:RefreshMinutes must degrade to "no caching benefit", never to a
    // 500 on the dashboard page: IMemoryCache rejects a non-positive relative expiration.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetMetricsAsync_WithNonPositiveRefreshMinutes_StillReturnsMetrics(int refreshMinutes)
    {
        var windowId = Guid.NewGuid();
        _blobClient.ListSchoolLaestabsAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>()).Returns([]);
        _logins.GetDistinctLoginsBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _requests.GetRequestAggregatesAsync(windowId, Arg.Any<CancellationToken>())
            .Returns(EmptyAggregates());
        var sut = new DashboardService(
            _blobClient, _logins, _requests, _cache,
            Options.Create(new DashboardSettings { RefreshMinutes = refreshMinutes }));

        var metrics = await sut.GetMetricsAsync(Window(windowId));

        Assert.Equal(windowId, metrics.WindowId);
    }

    private static DashboardRequestAggregates EmptyAggregates() => new()
    {
        TotalRequests = 0,
        AutoApproved = 0,
        AutoRejected = 0,
        RequiringScrutiny = 0,
        SubmittingUrns = [],
    };

    // Findings 5+6 from review: SchoolsSubmitted must count the same population by the same
    // key as the adjacent tiles — eligible schools, by laestab. A URN with no eligible login
    // in the window (LA, admin org, or a school whose blob is absent) must not count, so
    // "Submitted amendments" can never exceed "Eligible schools".
    [Fact]
    public async Task GetMetricsAsync_SubmittingUrnWithoutEligibleLogin_NotCountedAsSchoolSubmitted()
    {
        var windowId = Guid.NewGuid();
        _blobClient.ListSchoolLaestabsAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(["1111111", "2222222"]);
        _logins.GetDistinctLoginsBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new SchoolLogin(111, "1111111")]);
        _requests.GetRequestAggregatesAsync(windowId, Arg.Any<CancellationToken>())
            .Returns(new DashboardRequestAggregates
            {
                TotalRequests = 5,
                AutoApproved = 1,
                AutoRejected = 1,
                RequiringScrutiny = 1,
                SubmittingUrns = [111, 999], // 999 has no eligible login in the window
            });

        var metrics = await CreateSut().GetMetricsAsync(Window(windowId));

        Assert.Equal(1, metrics.SchoolsSubmitted);      // 999 excluded
        Assert.Equal(0, metrics.LoggedInNotSubmitted);  // the one logged-in school submitted
        Assert.Equal(5, metrics.TotalRequests);         // request tiles are unaffected
    }
}
