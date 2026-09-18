using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// The dashboard reads site searches and instant searches from one table, and the surface
// filter is what lets a reader separate them. These tests prove the filter actually reaches
// the SQL — every read composes EventsSource, so proving it on the summary tiles and the
// query table is proving the mechanism, not just those two queries.
[Collection(nameof(PostgresCollection))]
public sealed class SearchSurfaceFilterTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private SearchAnalyticsQueryService Service(params string[] surfaces)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(Arg.Any<string>()).Returns(20);

        var filter = Substitute.For<ISearchSurfaceFilter>();
        filter.Surfaces.Returns(surfaces.Length == 0 ? SearchSurfaces.All : surfaces);

        return new SearchAnalyticsQueryService(_fixture.CreateContext(), settings, filter);
    }

    private static SearchEvent Event(DateTime at, string sessionId, string query, string surface, string? hostPath = null) => new()
    {
        OccurredAtUtc = DateTime.SpecifyKind(at, DateTimeKind.Utc),
        SessionId = sessionId,
        QueryRaw = query,
        QueryNormalised = query,
        ResultsPages = surface == SearchSurfaces.InstantPage ? 0 : 1,
        ResultsSections = surface == SearchSurfaces.InstantPage ? 1 : 0,
        LatencyMs = 5,
        Surface = surface,
        HostPath = hostPath,
    };

    private async Task SeedAsync(params SearchEvent[] events)
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE search_events RESTART IDENTITY CASCADE;");
        ctx.SearchEvents.AddRange(events);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task NoFilter_CountsEverySurface()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            Event(now.AddMinutes(-5), "s-1", "site one", SearchSurfaces.Site),
            Event(now.AddMinutes(-4), "s-2", "instant one", SearchSurfaces.Instant),
            Event(now.AddMinutes(-3), "s-3", "page one", SearchSurfaces.InstantPage, "/help/x"));

        var summary = await Service().GetSummaryAsync(now.AddHours(-1), now.AddHours(1));

        Assert.Equal(3, summary.TotalCount);
    }

    [Fact]
    public async Task ASingleSurface_ExcludesTheOthers()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            Event(now.AddMinutes(-5), "s-1", "site one", SearchSurfaces.Site),
            Event(now.AddMinutes(-4), "s-2", "instant one", SearchSurfaces.Instant),
            Event(now.AddMinutes(-3), "s-3", "page one", SearchSurfaces.InstantPage, "/help/x"));

        var summary = await Service(SearchSurfaces.Site).GetSummaryAsync(now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, summary.TotalCount);
    }

    [Fact]
    public async Task TheFilterReachesTheQueryTable_NotJustTheTiles()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            Event(now.AddMinutes(-5), "s-1", "only on site", SearchSurfaces.Site),
            Event(now.AddMinutes(-4), "s-2", "only on page", SearchSurfaces.InstantPage, "/help/x"));

        var (rows, _) = await Service(SearchSurfaces.InstantPage)
            .GetPagedTopQueriesAsync(now.AddHours(-1), now.AddHours(1), page: 0, pageSize: 20);

        Assert.Contains(rows, r => r.QueryNormalised == "only on page");
        Assert.DoesNotContain(rows, r => r.QueryNormalised == "only on site");
    }

    [Fact]
    public async Task SectionsCountTowardTheTotal_SoAnOnPageSearchIsNotAZeroResult()
    {
        // results_total and zero_results are generated columns; an on-page search records
        // sections rather than pages, and would read as a zero-result search if the generated
        // expression had not been widened to include them.
        var now = DateTime.UtcNow;
        await SeedAsync(Event(now.AddMinutes(-3), "s-1", "page one", SearchSurfaces.InstantPage, "/help/x"));

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.SearchEvents.AsNoTracking().SingleAsync();

        Assert.Equal(1, row.ResultsSections);
        Assert.Equal(1, row.ResultsTotal);
        Assert.False(row.ZeroResults);
    }
}
