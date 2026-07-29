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
    public async Task RendersFourInteractiveTileButtons_WithAriaPressedAndDataSaTile()
    {
        var model = ViewModelWithData(
            totalCount: 100, uniqueSessions: 20, zeroRate: 5.0, p95: 40);

        var html = await RenderIndexAsync(model);

        // 4 tiles as <button> with data-sa-tile.
        var tileButtonCount = System.Text.RegularExpressions.Regex.Matches(
            html, "<button[^>]+class=\"sa-tile[^\"]*\"[^>]+data-sa-tile=\"").Count;
        Assert.Equal(4, tileButtonCount);

        // The four tile keys.
        Assert.Contains("data-sa-tile=\"volume\"", html);
        Assert.Contains("data-sa-tile=\"unique-users\"", html);
        Assert.Contains("data-sa-tile=\"zero-results\"", html);
        Assert.Contains("data-sa-tile=\"latency\"", html);

        // Volume is the default: aria-pressed="true" on that button, "false" on the rest.
        Assert.Contains("data-sa-tile=\"volume\"", html);
        var pressedTrueCount = System.Text.RegularExpressions.Regex.Matches(
            html, "aria-pressed=\"true\"").Count;
        var pressedFalseCount = System.Text.RegularExpressions.Regex.Matches(
            html, "aria-pressed=\"false\"").Count;
        Assert.Equal(1, pressedTrueCount);
        Assert.Equal(3, pressedFalseCount);
    }

    [Fact]
    public async Task RendersFourChartPanels_OneForEachTile()
    {
        var now = DateTime.UtcNow;
        var vol = new List<VolumeBucket> { new(now.AddHours(-2), 5, 3), new(now.AddHours(-1), 8, 4), new(now, 10, 5) };
        var uniq = new List<VolumeBucket> { new(now.AddHours(-2), 3, 0), new(now.AddHours(-1), 4, 0), new(now, 5, 0) };
        var zero = new List<VolumeBucket> { new(now.AddHours(-2), 1, 0), new(now.AddHours(-1), 0, 0), new(now, 2, 0) };
        var lat = new List<LatencyBucket> { new(now.AddHours(-2), 5, 20, 90), new(now.AddHours(-1), 6, 21, 95), new(now, 7, 22, 100) };

        var model = ViewModelWithData(
            totalCount: 23, uniqueSessions: 12, zeroRate: 13.0, p95: 100,
            volumeSeries: vol, uniqueSessionsSeries: uniq, zeroResultCountSeries: zero, latencySeries: lat);

        var html = await RenderIndexAsync(model);

        Assert.Contains("data-sa-panel=\"volume\"", html);
        Assert.Contains("data-sa-panel=\"unique-users\"", html);
        Assert.Contains("data-sa-panel=\"zero-results\"", html);
        Assert.Contains("data-sa-panel=\"latency\"", html);

        // Latency chart has three polylines (p50 / p95 / p5).
        var latencyPanel = html.IndexOf("data-sa-panel=\"latency\"", StringComparison.Ordinal);
        Assert.True(latencyPanel > 0);
        var latencyEndish = html.IndexOf("</div>", latencyPanel + 200, StringComparison.Ordinal);
        var latencyChunk = html.Substring(latencyPanel, Math.Max(0, latencyEndish - latencyPanel + 200));
        var polylineCount = System.Text.RegularExpressions.Regex.Matches(latencyChunk, "<polyline").Count;
        Assert.True(polylineCount >= 3, $"Expected 3 polylines in the latency chart panel, found {polylineCount}.");
    }

    [Fact]
    public async Task RendersTopPagesSummaryCardInline_WithViewAllLinkWhenOverflow()
    {
        var pages = new[]
        {
            new TopPageRow("https://example.gov.uk/one", 50, 10),
            new TopPageRow("https://example.gov.uk/two", 40, 8),
            new TopPageRow("home", 30, 6),
        };

        var model = ViewModelWithData(
            totalCount: 100, uniqueSessions: 20, zeroRate: 5.0, p95: 40,
            topPages: pages, topPagesTotalCount: 25);

        var html = await RenderIndexAsync(model);

        // Card renders with the "Top pages" heading (matches the two sibling summary cards).
        Assert.Contains(">Top pages<", html);
        // All three page keys appear.
        Assert.Contains("https://example.gov.uk/one", html);
        Assert.Contains("https://example.gov.uk/two", html);
        // Bare block key renders as <code> not an anchor.
        Assert.Matches(new System.Text.RegularExpressions.Regex(@"<code>home</code>"), html);
        // URL-shaped keys are linked with target=_blank rel=noopener.
        Assert.Matches(new System.Text.RegularExpressions.Regex(
            @"target=""_blank""[^>]*rel=""noopener noreferrer""[^>]*>https://example\.gov\.uk/one</a>|>https://example\.gov\.uk/one</a>"),
            html);
        // Total (25) exceeds rendered count (3) → "View all" link is present.
        Assert.Contains("View all top pages by search impressions", html);
        Assert.Contains("/admin/Search/Pages", html);
    }

    [Fact]
    public async Task RendersTopPagesSummaryCard_NoViewAllLinkWhenAllRowsFitOnCard()
    {
        var pages = new[]
        {
            new TopPageRow("https://example.gov.uk/only-one", 5, 2),
        };

        var model = ViewModelWithData(
            totalCount: 5, uniqueSessions: 2, zeroRate: 0.0, p95: 15,
            topPages: pages, topPagesTotalCount: 1);

        var html = await RenderIndexAsync(model);

        Assert.Contains(">Top pages<", html);
        Assert.Contains("https://example.gov.uk/only-one", html);
        // Total (1) matches rendered count (1) → no "View all" link.
        Assert.DoesNotContain("View all top pages by search impressions", html);
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

    // Filter + aggregate live in ONE form so clicking Apply filters preserves the current
    // aggregate state (otherwise a separate aggregate form gets clobbered on every filter
    // submit). The visible order under the form is: time window controls, bucket size,
    // aggregate toggle, then the Apply filters button - all four grouped so admins see
    // them as a single "chart controls" cluster.
    [Fact]
    public async Task FilterFormMerged_ContainsRangeBucketAndAggregate_InsideSingleForm()
    {
        var model = EmptyViewModel(totalRowCount: 0);

        var html = await RenderIndexAsync(model);

        // Exactly one <form> submitting to /admin/Search/ (the filter+aggregate merged form).
        // A stray aggregate form would be a second match here.
        var formOpens = System.Text.RegularExpressions.Regex.Matches(
            html, "<form[^>]+action=\"/admin/Search/\"[^>]*>").Count;
        Assert.Equal(1, formOpens);

        // The merged form must contain BOTH the bucket radios AND the aggregate checkbox.
        // Locate the form region and assert both live inside it.
        var formStart = html.IndexOf("<form", StringComparison.Ordinal);
        var formEnd = html.IndexOf("</form>", formStart, StringComparison.Ordinal);
        Assert.True(formStart >= 0 && formEnd > formStart, "Expected one <form>…</form> pair.");
        var formBody = html.Substring(formStart, formEnd - formStart);

        // 5 range radios + 5 bucket radios + 1 from + 1 to + 1 aggregate checkbox all inside.
        Assert.Contains("name=\"range\"", formBody);
        Assert.Contains("name=\"from\"", formBody);
        Assert.Contains("name=\"to\"", formBody);
        Assert.Contains("name=\"bucket\"", formBody);
        Assert.Contains("name=\"aggregate\"", formBody);
        Assert.Contains("data-sa-aggregate-toggle=\"true\"", formBody);

        // The aggregate checkbox should sit AFTER the bucket-size radios and BEFORE the
        // Apply filters submit button so the visual grouping reads: window -> bucket ->
        // aggregate -> apply (Lance's UAT ask for B3).
        var bucketIdx = formBody.IndexOf("Chart bucket size", StringComparison.Ordinal);
        var aggregateIdx = formBody.IndexOf("data-sa-aggregate-toggle=\"true\"", StringComparison.Ordinal);
        var applyIdx = formBody.IndexOf("Apply filters", StringComparison.Ordinal);
        Assert.True(bucketIdx >= 0, "Bucket-size legend should appear inside the form.");
        Assert.True(aggregateIdx > bucketIdx,
            "Aggregate checkbox should render AFTER the bucket-size selector.");
        Assert.True(applyIdx > aggregateIdx,
            "Apply filters button should render AFTER the aggregate checkbox.");
    }

    // B2 regression: with the aggregate checkbox checked, the rendered form still carries
    // the current range + from/to + bucket values so a subsequent Apply filters submit
    // does not clobber the aggregate state.
    [Fact]
    public async Task FilterFormMerged_WithAggregateOn_PreservesRangeAndBucketOnSubmit()
    {
        var model = EmptyViewModel(totalRowCount: 0, aggregateMode: true);

        var html = await RenderIndexAsync(model);

        // Aggregate checkbox renders in checked state.
        var checkboxMatch = System.Text.RegularExpressions.Regex.Match(
            html,
            "<input[^>]+data-sa-aggregate-toggle=\"true\"[^>]*>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(checkboxMatch.Success, "Aggregate checkbox should render.");
        Assert.Contains("checked", checkboxMatch.Value);

        // The current range key (7d, from EmptyViewModel) is echoed on its radio so a
        // subsequent Apply filters submit re-sends it.
        Assert.Matches(
            new System.Text.RegularExpressions.Regex(
                "<input[^>]+name=\"range\"[^>]+value=\"7d\"[^>]+checked",
                System.Text.RegularExpressions.RegexOptions.Singleline),
            html);
        Assert.Matches(
            new System.Text.RegularExpressions.Regex(
                "<input[^>]+name=\"bucket\"[^>]+value=\"1d\"[^>]+checked",
                System.Text.RegularExpressions.RegexOptions.Singleline),
            html);
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
        IReadOnlyList<VolumeBucket>? volumeSeries = null,
        IReadOnlyList<VolumeBucket>? uniqueSessionsSeries = null,
        IReadOnlyList<VolumeBucket>? zeroResultCountSeries = null,
        IReadOnlyList<LatencyBucket>? latencySeries = null,
        IReadOnlyList<TopPageRow>? topPages = null,
        int? topPagesTotalCount = null)
    {
        var now = DateTime.UtcNow;
        var pages = topPages ?? Array.Empty<TopPageRow>();
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
            BucketKey = "1d",
            UniqueSessionsSeries = uniqueSessionsSeries ?? Array.Empty<VolumeBucket>(),
            ZeroResultCountSeries = zeroResultCountSeries ?? Array.Empty<VolumeBucket>(),
            LatencyPercentileSeries = latencySeries ?? Array.Empty<LatencyBucket>(),
            TopPages = pages,
            TopPagesTotalCount = topPagesTotalCount ?? pages.Count,
            RequestTimings = Array.Empty<RequestTimingPoint>(),
            RequestTimingsTotalCount = 0,
            WeekdayHourGrid = ZeroFilledWeekdayHourGrid(),
            ZeroResultOutcomes = new ZeroResultOutcomeSummary(0, 0, 0, 0, 0),
            SummaryDeltas = new SearchAnalyticsSummaryDeltas(0, 0, 0d, 0, Available: false),
            AggregateMode = false,
        };
    }

    private static SearchAnalyticsIndexViewModel EmptyViewModel(int totalRowCount, bool aggregateMode = false)
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
            BucketKey = "1d",
            UniqueSessionsSeries = Array.Empty<VolumeBucket>(),
            ZeroResultCountSeries = Array.Empty<VolumeBucket>(),
            LatencyPercentileSeries = Array.Empty<LatencyBucket>(),
            TopPages = Array.Empty<TopPageRow>(),
            TopPagesTotalCount = 0,
            RequestTimings = Array.Empty<RequestTimingPoint>(),
            RequestTimingsTotalCount = 0,
            WeekdayHourGrid = ZeroFilledWeekdayHourGrid(),
            ZeroResultOutcomes = new ZeroResultOutcomeSummary(0, 0, 0, 0, 0),
            SummaryDeltas = new SearchAnalyticsSummaryDeltas(0, 0, 0d, 0, Available: false),
            AggregateMode = aggregateMode,
        };
    }

    private static IReadOnlyList<WeekdayHourBucket> ZeroFilledWeekdayHourGrid()
    {
        var grid = new List<WeekdayHourBucket>(168);
        for (var w = 1; w <= 7; w++)
        {
            for (var h = 0; h < 24; h++)
            {
                grid.Add(new WeekdayHourBucket(w, h, 0));
            }
        }
        return grid;
    }
}
