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
//   - four .sa-tile blocks always render, populated from the summary (regardless of sample size)
//   - a "small sample" hint appears AFTER the data when 0 < count < the small-sample threshold
//   - a "no searches in window" hint appears AFTER the data when count == 0
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
    public async Task RendersFourStatTiles_AlwaysPopulatedFromSummary()
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
    public async Task RendersTilesAndSmallSampleHint_WhenBelowThreshold()
    {
        // Regression guard: below the small-sample threshold we still show the tiles and
        // tables — the hint about small samples is a footnote AFTER the data, not a
        // replacement for it.
        var model = ViewModelWithData(
            totalCount: 5, uniqueSessions: 3, zeroRate: 20.0, p95: 15);

        var html = await RenderIndexAsync(model);

        // Tiles rendered with real values.
        Assert.Contains("sa-tile__value", html);
        Assert.Contains(">5<", html);
        Assert.Contains(">3<", html);

        // Small-sample hint present and positioned AFTER the tiles.
        var hintText = "Small sample";
        var tileIndex = html.IndexOf("sa-tile__value", StringComparison.Ordinal);
        var hintIndex = html.IndexOf(hintText, StringComparison.Ordinal);
        Assert.True(hintIndex > tileIndex, "Small-sample hint should appear after the tiles.");
    }

    [Fact]
    public async Task RendersTilesAndNoSearchesHint_WhenWindowIsEmpty()
    {
        var model = EmptyViewModel(totalRowCount: 0);

        var html = await RenderIndexAsync(model);

        // Tiles still render — they read as zero (which is a truthful signal).
        Assert.Contains("sa-tile__value", html);
        // Friendly note that the window is empty, positioned after the tiles.
        Assert.Contains("No searches recorded in this window", html);
        var tileIndex = html.IndexOf("sa-tile__value", StringComparison.Ordinal);
        var hintIndex = html.IndexOf("No searches recorded in this window", StringComparison.Ordinal);
        Assert.True(hintIndex > tileIndex, "Empty-window hint should appear after the tiles.");
    }

    [Fact]
    public async Task RendersVolumeChart_AlwaysRenders()
    {
        var now = DateTime.UtcNow;
        var buckets = new List<VolumeBucket>();
        for (var i = 0; i < 6; i++)
            buckets.Add(new VolumeBucket(now.AddHours(-6 + i), 10 + i, 5 + i));

        var model = ViewModelWithData(
            totalCount: 100, uniqueSessions: 20, zeroRate: 10.0, p95: 50,
            volumeSeries: buckets);

        var html = await RenderIndexAsync(model);

        // Real chart replaces the P03 placeholder — SVG root + WCAG 1.1.1 details fallback.
        Assert.Contains("<svg", html);
        Assert.Contains("sa-chart", html);
        Assert.Contains("<details class=\"govuk-details\"", html);
    }

    [Fact]
    public async Task RendersTopQueriesAndTopZeroResultTables_AlwaysRenders()
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
        IReadOnlyList<TopQueryRow>? topZero = null,
        IReadOnlyList<VolumeBucket>? volumeSeries = null)
    {
        var now = DateTime.UtcNow;
        return new SearchAnalyticsIndexViewModel
        {
            Summary = new SearchAnalyticsSummary(totalCount, uniqueSessions, zeroRate, p95),
            TopQueries = topQueries ?? Array.Empty<TopQueryRow>(),
            TopZeroResultQueries = topZero ?? Array.Empty<TopQueryRow>(),
            VolumeSeries = volumeSeries ?? Array.Empty<VolumeBucket>(),
            FromUtc = now.AddDays(-7),
            ToUtc = now,
            RangeKey = "7d",
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
            VolumeSeries = Array.Empty<VolumeBucket>(),
            FromUtc = now.AddDays(-7),
            ToUtc = now,
            RangeKey = "7d",
            TotalRowCount = totalRowCount,
        };
    }
}
