using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Read-side tests for the three admin drill-in tables (top queries, top zero-result queries,
// top pages returned). Each reader returns (Rows, TotalCount) so the view can render both the
// current page and the pagination widget from a single call. Every SQL parameter binds via
// Npgsql — the read path has no injection surface even though the paging inputs (page, pageSize)
// arrive from the query string.
[Collection(nameof(PostgresCollection))]
public sealed class SearchAnalyticsDrillInTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsDrillInTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- GetPagedTopQueriesAsync: paged + ordered by count DESC + total is DISTINCT count ---

    [Fact]
    public async Task GetPagedTopQueries_FirstPage_ReturnsAllDistinctQueriesOrderedByCountDesc()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        // 3 dominant queries (20 hits each) + 5 zero-result queries (5 hits each) = 85 rows, 8 distinct.
        await SeedDrillInCorpusAsync(now);

        var (rows, total) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        // 8 distinct queries: 3 with results + 5 zero-result. All fit on one page of 10.
        Assert.Equal(8, total);
        Assert.Equal(8, rows.Count);

        // Dominant queries lead by count (20 each); zero-result queries follow (5 each).
        Assert.Equal(20, rows[0].Count);
        Assert.Equal(20, rows[1].Count);
        Assert.Equal(20, rows[2].Count);
        Assert.Contains(rows.Take(3), r => r.QueryNormalised == "alpha");
        Assert.Contains(rows.Take(3), r => r.QueryNormalised == "bravo");
        Assert.Contains(rows.Take(3), r => r.QueryNormalised == "charlie");
    }

    [Fact]
    public async Task GetPagedTopQueries_PageSmallerThanCorpus_PagesCorrectly()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        await SeedDrillInCorpusAsync(now);

        var (page1, total1) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 1, pageSize: 3, CancellationToken.None);
        var (page2, total2) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 2, pageSize: 3, CancellationToken.None);
        var (page3, total3) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 3, pageSize: 3, CancellationToken.None);
        var (page4, total4) = await CreateService()
            .GetPagedTopQueriesAsync(from, now, page: 4, pageSize: 3, CancellationToken.None);

        Assert.Equal(8, total1);
        Assert.Equal(8, total2);
        Assert.Equal(8, total3);
        Assert.Equal(8, total4);
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.Equal(2, page3.Count);
        Assert.Empty(page4);

        // No cross-page duplicates.
        var union = page1.Concat(page2).Concat(page3).Select(r => r.QueryNormalised).ToList();
        Assert.Equal(union.Distinct().Count(), union.Count);
    }

    [Fact]
    public async Task GetPagedTopQueries_EmptyWindow_ReturnsEmptyRowsAndZeroTotal()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;

        var (rows, total) = await CreateService()
            .GetPagedTopQueriesAsync(now.AddHours(-1), now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Empty(rows);
        Assert.Equal(0, total);
    }

    // --- GetPagedTopZeroResultQueriesAsync: filters to zero_results = true ---

    [Fact]
    public async Task GetPagedTopZeroResults_ReturnsOnlyZeroResultQueries()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        await SeedDrillInCorpusAsync(now);

        var (rows, total) = await CreateService()
            .GetPagedTopZeroResultQueriesAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        // 5 distinct zero-result queries in the corpus.
        Assert.Equal(5, total);
        Assert.Equal(5, rows.Count);
        Assert.DoesNotContain(rows, r => r.QueryNormalised is "alpha" or "bravo" or "charlie");
        Assert.All(rows, r => Assert.StartsWith("gap-", r.QueryNormalised));
        Assert.All(rows, r => Assert.Equal(5, r.Count));
    }

    // --- GetTopPagesAsync: joins search_event_results to search_events, groups by result_key ---

    [Fact]
    public async Task GetTopPages_ReturnsPagesInImpressionOrder()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        // 10 distinct pages, each impressed 6 times = 60 total impressions.
        await SeedTopPagesCorpusAsync(now);

        var (rows, total) = await CreateService()
            .GetTopPagesAsync(from, now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(10, total);
        Assert.Equal(10, rows.Count);
        Assert.All(rows, r => Assert.Equal(6, r.ImpressionCount));
        // Rows are ordered by ImpressionCount DESC; ties break deterministically so the same query
        // returns the same order twice — ordered by result_key ascending as the tie-break.
        var byKeyAscending = rows.OrderBy(r => r.ResultKey).Select(r => r.ResultKey).ToList();
        Assert.Equal(byKeyAscending, rows.Select(r => r.ResultKey).ToList());
    }

    [Fact]
    public async Task GetTopPages_HonoursPageAndPageSize()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-1);

        await SeedTopPagesCorpusAsync(now);

        var (page1, total1) = await CreateService()
            .GetTopPagesAsync(from, now, page: 1, pageSize: 4, CancellationToken.None);
        var (page2, total2) = await CreateService()
            .GetTopPagesAsync(from, now, page: 2, pageSize: 4, CancellationToken.None);
        var (page3, total3) = await CreateService()
            .GetTopPagesAsync(from, now, page: 3, pageSize: 4, CancellationToken.None);

        Assert.Equal(10, total1);
        Assert.Equal(10, total2);
        Assert.Equal(10, total3);
        Assert.Equal(4, page1.Count);
        Assert.Equal(4, page2.Count);
        Assert.Equal(2, page3.Count);
    }

    [Fact]
    public async Task GetTopPages_ExcludesResultsOutsideTheWindow()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;

        // One event with a hit inside the window, one older event with a different hit outside.
        var insideEvent = NewEvent(now.AddMinutes(-30), "s-in", "query-in", results: 1, latency: 10);
        var outsideEvent = NewEvent(now.AddDays(-30), "s-out", "query-out", results: 1, latency: 10);

        long insideId, outsideId;
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.Add(insideEvent);
            ctx.SearchEvents.Add(outsideEvent);
            await ctx.SaveChangesAsync();
            insideId = insideEvent.Id;
            outsideId = outsideEvent.Id;

            ctx.SearchEventResults.Add(NewResult(insideId, "https://example.gov.uk/inside", position: 1));
            ctx.SearchEventResults.Add(NewResult(outsideId, "https://example.gov.uk/outside", position: 1));
            await ctx.SaveChangesAsync();
        }

        var (rows, total) = await CreateService()
            .GetTopPagesAsync(now.AddHours(-1), now, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(rows);
        Assert.Equal("https://example.gov.uk/inside", rows[0].ResultKey);
    }

    // --- Controller actions return the expected view + view model for each drill-in ---

    [Fact]
    public async Task QueriesAction_ReturnsQueryDrillInViewModel_WithPagedRows()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        await SeedDrillInCorpusAsync(now);

        var controller = SutWithPageSize(10);
        var result = await controller.Queries(range: "7d", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsQueryDrillInViewModel>(view.Model);

        Assert.Equal(8, model.TotalCount);
        Assert.Equal(8, model.Rows.Count);
        Assert.Equal(1, model.Page);
        Assert.Equal(10, model.PageSize);
    }

    [Fact]
    public async Task ZeroResultsAction_ReturnsQueryDrillInViewModel_WithZeroResultRowsOnly()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        await SeedDrillInCorpusAsync(now);

        var controller = SutWithPageSize(10);
        var result = await controller.ZeroResults(range: "7d", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsQueryDrillInViewModel>(view.Model);

        Assert.Equal(5, model.TotalCount);
        Assert.All(model.Rows, r => Assert.StartsWith("gap-", r.QueryNormalised));
    }

    [Fact]
    public async Task PagesAction_ReturnsPagesDrillInViewModel_OrderedByImpressions()
    {
        await ResetAsync();
        var now = DateTime.UtcNow;
        await SeedTopPagesCorpusAsync(now);

        var controller = SutWithPageSize(10);
        var result = await controller.Pages(range: "7d", from: null, to: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchAnalyticsPagesDrillInViewModel>(view.Model);

        Assert.Equal(10, model.TotalCount);
        Assert.Equal(10, model.Rows.Count);
        Assert.All(model.Rows, r => Assert.Equal(6, r.ImpressionCount));
    }

    [Fact]
    public async Task QueriesAction_HasRouteAndGateAttributes()
    {
        var method = typeof(SearchAnalyticsController)
            .GetMethod(nameof(SearchAnalyticsController.Queries))!;

        var http = method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>().FirstOrDefault();
        Assert.NotNull(http);
        Assert.Equal("Queries", http!.Template);
    }

    [Fact]
    public async Task ZeroResultsAction_HasRouteAndGateAttributes()
    {
        var method = typeof(SearchAnalyticsController)
            .GetMethod(nameof(SearchAnalyticsController.ZeroResults))!;

        var http = method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>().FirstOrDefault();
        Assert.NotNull(http);
        Assert.Equal("ZeroResults", http!.Template);
    }

    [Fact]
    public async Task PagesAction_HasRouteAndGateAttributes()
    {
        var method = typeof(SearchAnalyticsController)
            .GetMethod(nameof(SearchAnalyticsController.Pages))!;

        var http = method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>().FirstOrDefault();
        Assert.NotNull(http);
        Assert.Equal("Pages", http!.Template);
    }

    // --- Drill-in views render the expected GDS markup + client-side filter attributes ---

    [Fact]
    public async Task QueriesView_RendersTableWithFilterKeyAttributesAndPagination()
    {
        var model = new SearchAnalyticsQueryDrillInViewModel
        {
            Rows = new[]
            {
                new TopQueryRow("alpha", 20, 0),
                new TopQueryRow("bravo", 15, 5),
            },
            TotalCount = 50,
            Page = 1,
            PageSize = 10,
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow,
            RangeKey = "7d",
        };

        var html = await RenderDrillInAsync<SearchAnalyticsQueryDrillInViewModel>(
            "/Views/Admin/Search/Queries.cshtml", model);

        Assert.Contains("govuk-table", html);
        Assert.Contains("data-filter-key", html);
        // Client-side filter input (data-filter-target references the table id).
        Assert.Contains("data-filter-target", html);
        // Pagination widget renders when TotalCount > PageSize.
        Assert.Contains("govuk-pagination", html);
        Assert.Contains("alpha", html);
        Assert.Contains("bravo", html);
    }

    [Fact]
    public async Task ZeroResultsView_RendersTableWithFilterKeyAttributes()
    {
        var model = new SearchAnalyticsQueryDrillInViewModel
        {
            Rows = new[] { new TopQueryRow("gap-1", 5, 5) },
            TotalCount = 1,
            Page = 1,
            PageSize = 10,
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow,
            RangeKey = "7d",
        };

        var html = await RenderDrillInAsync<SearchAnalyticsQueryDrillInViewModel>(
            "/Views/Admin/Search/ZeroResults.cshtml", model);

        Assert.Contains("govuk-table", html);
        Assert.Contains("data-filter-key", html);
        Assert.Contains("gap-1", html);
    }

    [Fact]
    public async Task PagesView_RendersTableWithPageUrlAndImpressionColumns()
    {
        var model = new SearchAnalyticsPagesDrillInViewModel
        {
            Rows = new[]
            {
                new TopPageRow("https://example.gov.uk/page-01", 42, 12),
                new TopPageRow("https://example.gov.uk/page-02", 30, 9),
            },
            TotalCount = 15,
            Page = 1,
            PageSize = 10,
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow,
            RangeKey = "7d",
        };

        var html = await RenderDrillInAsync<SearchAnalyticsPagesDrillInViewModel>(
            "/Views/Admin/Search/Pages.cshtml", model);

        Assert.Contains("govuk-table", html);
        Assert.Contains("data-filter-key", html);
        Assert.Contains("govuk-pagination", html);
        Assert.Contains("https://example.gov.uk/page-01", html);
        Assert.Contains("42", html);
    }

    // Renders a drill-in view standalone (isMainPage:false → no layout wiring needed).
    private static async Task<string> RenderDrillInAsync<TModel>(string viewPath, TModel model)
        where TModel : class
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(SearchAnalyticsController).Assembly);
                    services.AddGovUkFrontend();
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData = new RouteData();
        routeData.Values["controller"] = "SearchAnalytics";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate view {viewPath}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }

    // Constructs a controller with a real query service + a stubbed ISettingService returning
    // the requested CMS:PageLength value. The controller's Queries / ZeroResults / Pages
    // actions read the page size through the settings service — mirroring the ObservabilityController
    // resolution pattern — so this stub replaces the settings-repository lookup a full boot would need.
    private SearchAnalyticsController SutWithPageSize(int pageSize)
    {
        var context = _fixture.CreateContext();
        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(pageSize);
        return new SearchAnalyticsController(query, settings);
    }

    // --- Corpus builders ---

    private async Task SeedDrillInCorpusAsync(DateTime now)
    {
        var events = new List<SearchEvent>();
        foreach (var q in new[] { "alpha", "bravo", "charlie" })
        {
            // Start at now - 1 minute so no row lands at exactly `now` (the SQL window is
            // half-open with `< @to`, so an at-`now` row would fall outside and skew counts).
            for (var i = 1; i <= 20; i++)
            {
                events.Add(NewEvent(now.AddMinutes(-i * 2), $"s-{q}-{i}", q, results: 3, latency: 15));
            }
        }
        for (var g = 1; g <= 5; g++)
        {
            var name = $"gap-{g}";
            for (var i = 1; i <= 5; i++)
            {
                events.Add(NewEvent(now.AddMinutes(-i * 3 - 10), $"s-{name}-{i}", name, results: 0, latency: 12));
            }
        }
        await using var ctx = _fixture.CreateContext();
        ctx.SearchEvents.AddRange(events);
        await ctx.SaveChangesAsync();
    }

    // 10 pages, 6 impressions each = 60 total impressions attached to 10 parent search_events.
    // Each parent event holds 6 result rows, one per page — mirrors a real result set landing
    // 10 hits for a single query.
    private async Task SeedTopPagesCorpusAsync(DateTime now)
    {
        var events = Enumerable.Range(0, 6)
            .Select(i => NewEvent(now.AddMinutes(-i * 4 - 5), $"s-page-{i}", $"query-{i}", results: 10, latency: 15))
            .ToList();

        long[] eventIds;
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.SearchEvents.AddRange(events);
            await ctx.SaveChangesAsync();
            eventIds = events.Select(e => e.Id).ToArray();
        }

        await using (var ctx = _fixture.CreateContext())
        {
            for (var i = 0; i < 6; i++)
            {
                for (var p = 0; p < 10; p++)
                {
                    ctx.SearchEventResults.Add(NewResult(eventIds[i], $"https://example.gov.uk/page-{p:00}", position: p + 1));
                }
            }
            await ctx.SaveChangesAsync();
        }
    }

    // --- Helpers ---

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(
                _fixture.CreateContext(),
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc,
        string sessionId,
        string queryNormalised,
        int results,
        int latency) => new()
        {
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            SessionId = sessionId,
            QueryRaw = queryNormalised,
            QueryNormalised = queryNormalised,
            Scope = null,
            ResultsPages = results,
            ResultsBlocks = 0,
            LatencyMs = latency,
        };

    private static SearchEventResult NewResult(long searchEventId, string resultKey, int position) => new()
    {
        SearchEventId = searchEventId,
        Position = position,
        ResultKind = "page",
        ResultKey = resultKey,
        Rank = 1.0f,
    };

    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE search_events RESTART IDENTITY CASCADE;");
    }
}
