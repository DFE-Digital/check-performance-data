using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Renders Views/Admin/Search/Index.cshtml through the real Razor view engine to prove:
//   - four .sa-tile blocks render with their labelled values when HasData is true
//   - empty-state inset text replaces the tiles when HasData is false
//   - a chart-placeholder marker is present so the volume-chart follow-up knows where to slot in
//   - a top-queries + top-zero-result table skeleton renders when data is present
//
// Mirrors WikiTemplateRenderTests: spin up a minimal MVC host, resolve the composite view
// engine, render the view with isMainPage:false so _ViewStart / _AdminLayout are skipped.
// The plan's controller-scope assertions live in SearchAnalyticsControllerTests; this class
// only exercises the view template's own markup rules.
public sealed class SearchAnalyticsIndexViewRenderTests
{
    [Fact]
    public async Task RendersFourStatTiles_WhenHasDataIsTrue()
    {
        var model = ViewModelWithData(
            totalCount: 1247, uniqueSessions: 481, zeroRate: 12.3, p95: 87);

        var html = await RenderIndexAsync(model);

        // Four sa-tile blocks — one per tile.
        var tileCount = System.Text.RegularExpressions.Regex.Matches(
            html, "sa-tile(?![_-])").Count;
        Assert.True(tileCount >= 4, $"Expected at least 4 sa-tile references, found {tileCount}.");

        // Each tile's value renders inside a sa-tile__value span.
        Assert.Contains("sa-tile__value", html);
        Assert.Contains("sa-tile__label", html);

        // The four labels + the four numbers appear in the rendered output.
        Assert.Contains("Searches", html);
        Assert.Contains("Unique users", html);
        Assert.Contains("Zero-result rate", html);
        Assert.Contains("P95 latency", html);
        Assert.Contains("1,247", html);
        Assert.Contains("481", html);
        Assert.Contains("87", html);
    }

    [Fact]
    public async Task RendersEmptyStateInsetText_WhenHasDataIsFalse()
    {
        var model = EmptyViewModel(totalRowCount: 5);

        var html = await RenderIndexAsync(model);

        Assert.Contains("govuk-inset-text", html);
        Assert.Contains("Fewer than 20 searches were captured in this window.", html);

        // Empty-state suppresses tiles — the sa-tile__value token must not appear.
        Assert.DoesNotContain("sa-tile__value", html);
    }

    [Fact]
    public async Task RendersVolumeChartPlaceholder_ForNextPlanToReplace()
    {
        var model = ViewModelWithData(totalCount: 100, uniqueSessions: 20, zeroRate: 10.0, p95: 50);

        var html = await RenderIndexAsync(model);

        // The placeholder marker the next plan replaces with a real chart partial.
        Assert.Contains("sa-chart-placeholder", html);
    }

    [Fact]
    public async Task RendersTopQueriesAndTopZeroResultTables_WhenHasDataIsTrue()
    {
        var model = ViewModelWithData(
            totalCount: 50, uniqueSessions: 5, zeroRate: 20.0, p95: 40,
            topQueries: new[]
            {
                new TopQueryRow("how do i submit", 20, 0),
                new TopQueryRow("what is a window", 15, 2),
            },
            topZero: new[]
            {
                new TopQueryRow("missing content", 10, 10),
                new TopQueryRow("nothing here", 5, 5),
            });

        var html = await RenderIndexAsync(model);

        Assert.Contains("Top queries", html);
        Assert.Contains("Top zero-result queries", html);
        Assert.Contains("how do i submit", html);
        Assert.Contains("missing content", html);
        Assert.Contains("govuk-table", html);
    }

    [Fact]
    public async Task RendersFilterFormWithRangeRadios_AlwaysVisible()
    {
        var model = EmptyViewModel(totalRowCount: 0);

        var html = await RenderIndexAsync(model);

        Assert.Contains("Time window", html);
        Assert.Contains("Last 7 days", html);
        Assert.Contains("Last 24 hours", html);
        Assert.Contains("Apply filters", html);
    }

    // Renders Views/Admin/Search/Index.cshtml with isMainPage:false — skips the _ViewStart's
    // Layout = "_AdminLayout" so the test does not need to wire the full admin layout
    // dependency graph. The resulting HTML is the view body only, which is enough to assert
    // on the sa-tile / empty-state / chart-placeholder markers.
    private static async Task<string> RenderIndexAsync(SearchAnalyticsIndexViewModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(SearchAnalyticsController).Assembly);
                    // GDS tag helpers used by the view (govuk-radios, govuk-button etc.) need
                    // the ComponentGenerator service graph — mirror the production Program.cs
                    // registration so the same view code path runs under test.
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

        // The view file sits under Views/Admin/Search/ (not the controller-name default of
        // Views/SearchAnalytics/), so ask the engine for the file directly via GetView. The
        // production controller does the same — return View("~/Views/Admin/Search/Index.cshtml").
        var view = viewEngine.GetView(executingFilePath: null, viewPath: "/Views/Admin/Search/Index.cshtml", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate Search Index view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<SearchAnalyticsIndexViewModel>(
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

    private static SearchAnalyticsIndexViewModel ViewModelWithData(
        int totalCount,
        int uniqueSessions,
        double zeroRate,
        int p95,
        IReadOnlyList<TopQueryRow>? topQueries = null,
        IReadOnlyList<TopQueryRow>? topZero = null)
    {
        var now = DateTime.UtcNow;
        return new SearchAnalyticsIndexViewModel
        {
            Summary = new SearchAnalyticsSummary(totalCount, uniqueSessions, zeroRate, p95),
            TopQueries = topQueries ?? Array.Empty<TopQueryRow>(),
            TopZeroResultQueries = topZero ?? Array.Empty<TopQueryRow>(),
            FromUtc = now.AddDays(-7),
            ToUtc = now,
            RangeKey = "7d",
            HasData = totalCount >= 20,
            TotalRowCount = totalCount,
        };
    }

    private static SearchAnalyticsIndexViewModel EmptyViewModel(int totalRowCount)
    {
        var now = DateTime.UtcNow;
        return new SearchAnalyticsIndexViewModel
        {
            Summary = new SearchAnalyticsSummary(totalRowCount, 0, 0d, 0),
            TopQueries = Array.Empty<TopQueryRow>(),
            TopZeroResultQueries = Array.Empty<TopQueryRow>(),
            FromUtc = now.AddDays(-7),
            ToUtc = now,
            RangeKey = "7d",
            HasData = false,
            TotalRowCount = totalRowCount,
        };
    }
}
