using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// The single-page-search section of the dashboard. Its two reads answer "which pages get
// searched" and "what was asked of this one", and both have to separate three outcomes that
// a site search never distinguishes: nothing matched, something matched and was taken, and
// something matched and was not.
[Collection(nameof(PostgresCollection))]
public sealed class OnPageSearchQueryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private SearchAnalyticsQueryService Service()
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(Arg.Any<string>()).Returns(20);
        return new SearchAnalyticsQueryService(_fixture.CreateContext(), settings);
    }

    private static SearchEvent OnPage(
        DateTime at, string sessionId, string query, string hostPath,
        int sections = 1, string? selectedKey = null) => new()
    {
        OccurredAtUtc = DateTime.SpecifyKind(at, DateTimeKind.Utc),
        SessionId = sessionId,
        QueryRaw = query,
        QueryNormalised = query,
        ResultsPages = 0,
        ResultsSections = sections,
        LatencyMs = 3,
        Surface = SearchSurfaces.InstantPage,
        HostPath = hostPath,
        SelectedKey = selectedKey,
        SelectedPosition = selectedKey is null ? null : 1,
    };

    private static SearchEvent SiteSearch(DateTime at, string sessionId, string query) => new()
    {
        OccurredAtUtc = DateTime.SpecifyKind(at, DateTimeKind.Utc),
        SessionId = sessionId,
        QueryRaw = query,
        QueryNormalised = query,
        ResultsPages = 1,
        LatencyMs = 3,
        Surface = SearchSurfaces.Site,
    };

    private async Task SeedAsync(params SearchEvent[] events)
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE search_events RESTART IDENTITY CASCADE;");
        ctx.SearchEvents.AddRange(events);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Pages_AreRankedByHowMuchTheyWereSearched()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            OnPage(now.AddMinutes(-9), "s-1", "evidence", "/help/busy"),
            OnPage(now.AddMinutes(-8), "s-2", "upload", "/help/busy"),
            OnPage(now.AddMinutes(-7), "s-3", "evidence", "/help/busy"),
            OnPage(now.AddMinutes(-6), "s-4", "evidence", "/help/quiet"));

        var (rows, total) = await Service().GetOnPageSearchPagesAsync(now.AddHours(-1), now.AddHours(1), 1, 20);

        Assert.Equal(2, total);
        Assert.Equal("/help/busy", rows[0].HostPath);
        Assert.Equal(3, rows[0].Searches);
        Assert.Equal(3, rows[0].UniqueSessions);
        Assert.Equal("/help/quiet", rows[1].HostPath);
    }

    [Fact]
    public async Task SiteSearches_NeverAppear_TheyHaveNoHostPage()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            SiteSearch(now.AddMinutes(-5), "s-1", "evidence"),
            OnPage(now.AddMinutes(-4), "s-2", "evidence", "/help/busy"));

        var (rows, total) = await Service().GetOnPageSearchPagesAsync(now.AddHours(-1), now.AddHours(1), 1, 20);

        Assert.Equal(1, total);
        Assert.Equal("/help/busy", Assert.Single(rows).HostPath);
    }

    [Fact]
    public async Task ThreeOutcomes_AreCountedApart()
    {
        // Nothing matched; matched and taken; matched and left. The third is the one no other
        // part of the dashboard can see.
        var now = DateTime.UtcNow;
        await SeedAsync(
            OnPage(now.AddMinutes(-9), "s-1", "nothing here", "/help/x", sections: 0),
            OnPage(now.AddMinutes(-8), "s-2", "evidence", "/help/x", selectedKey: "#providing-evidence"),
            OnPage(now.AddMinutes(-7), "s-3", "evidence", "/help/x"));

        var (rows, _) = await Service().GetOnPageSearchPagesAsync(now.AddHours(-1), now.AddHours(1), 1, 20);

        var page = Assert.Single(rows);
        Assert.Equal(3, page.Searches);
        Assert.Equal(1, page.ZeroResultCount);
        Assert.Equal(1, page.SelectedCount);
    }

    [Fact]
    public async Task Terms_AreScopedToTheirOwnPage()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            OnPage(now.AddMinutes(-9), "s-1", "on this page", "/help/x"),
            OnPage(now.AddMinutes(-8), "s-2", "on another page", "/help/y"));

        var (rows, total) = await Service()
            .GetOnPageSearchTermsAsync("/help/x", now.AddHours(-1), now.AddHours(1), 1, 20);

        Assert.Equal(1, total);
        Assert.Equal("on this page", Assert.Single(rows).QueryNormalised);
    }

    [Fact]
    public async Task Terms_CountRepeatsTogether_AndKeepTheirOutcomes()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            OnPage(now.AddMinutes(-9), "s-1", "evidence", "/help/x", selectedKey: "#a"),
            OnPage(now.AddMinutes(-8), "s-2", "evidence", "/help/x"),
            OnPage(now.AddMinutes(-7), "s-3", "evidence", "/help/x", sections: 0));

        var (rows, _) = await Service()
            .GetOnPageSearchTermsAsync("/help/x", now.AddHours(-1), now.AddHours(1), 1, 20);

        var term = Assert.Single(rows);
        Assert.Equal(3, term.Searches);
        Assert.Equal(1, term.SelectedCount);
        Assert.Equal(1, term.ZeroResultCount);
    }

    [Fact]
    public async Task EventsOutsideTheWindow_AreNotCounted()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            OnPage(now.AddDays(-30), "s-old", "evidence", "/help/x"),
            OnPage(now.AddMinutes(-5), "s-new", "evidence", "/help/x"));

        var (rows, _) = await Service().GetOnPageSearchPagesAsync(now.AddHours(-1), now.AddHours(1), 1, 20);

        Assert.Equal(1, Assert.Single(rows).Searches);
    }
}
