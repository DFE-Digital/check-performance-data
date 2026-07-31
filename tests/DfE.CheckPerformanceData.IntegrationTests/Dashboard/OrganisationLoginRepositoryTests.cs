using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Dashboard;

[Collection(nameof(PostgresCollection))]
public sealed class OrganisationLoginRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    // RecordAsync always stamps LoggedInAtUtc as DateTime.UtcNow, so rows from one test can
    // fall inside another test's fixed query range depending on when the suite runs. Cleaning
    // up the URNs each test writes keeps the two tests isolated regardless of the current date.
    private static readonly long[] TestUrns = [142313, 100001, 200002];

    public Task InitializeAsync() => CleanUpAsync();

    public Task DisposeAsync() => CleanUpAsync();

    private async Task CleanUpAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.OrganisationLogins.Where(l => TestUrns.Contains(l.OrganisationUrn)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task RecordAsync_InsertsRow_AndQueryReturnsDistinctPairs()
    {
        await using var context = fixture.CreateContext();
        var repo = new OrganisationLoginRepository(context);
        // Derived from UtcNow, not a fixed calendar month: RecordAsync stamps rows with
        // DateTime.UtcNow, so a hard-coded month stops containing them the moment the
        // calendar moves on and the test would start failing on a date rather than a change.
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);

        // Two logins by the same school + one by another school inside the range.
        await repo.RecordAsync(new OrganisationLoginRecord("user-1", 142313, "8604070", "Kingsmead School"));
        await repo.RecordAsync(new OrganisationLoginRecord("user-2", 142313, "8604070", "Kingsmead School"));
        await repo.RecordAsync(new OrganisationLoginRecord("user-3", 100001, "9334070", "Other School"));

        var logins = await repo.GetDistinctLoginsBetweenAsync(from, to);

        Assert.Equal(2, logins.Count);
        Assert.Contains(new SchoolLogin(142313, "8604070"), logins);
        Assert.Contains(new SchoolLogin(100001, "9334070"), logins);
    }

    [Fact]
    public async Task GetDistinctLoginsBetweenAsync_ExcludesLoginsOutsideRange()
    {
        await using var context = fixture.CreateContext();
        var repo = new OrganisationLoginRepository(context);
        await repo.RecordAsync(new OrganisationLoginRecord("user-4", 200002, "1112222", "Early School"));

        // A range in the past cannot contain the row just written with UtcNow.
        var logins = await repo.GetDistinctLoginsBetweenAsync(
            new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain(logins, l => l.OrganisationUrn == 200002);
    }
}
