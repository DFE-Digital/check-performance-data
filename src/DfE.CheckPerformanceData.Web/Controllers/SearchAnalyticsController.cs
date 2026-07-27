using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Admin landing dashboard for the search-analytics surface. The class-level
// [RequireAdminSection] gate 302s anonymous requests to sign-in and 404s editor-only users;
// admins with the search-analytics section grant hit the Index action which renders the
// 4 stat tiles + volume-over-time chart + top-10 tables + empty-state panel. Three drill-in
// actions (Queries, ZeroResults, Pages) render the full paged lists — pagination sizes come
// from the shared CMS:PageLength admin setting so all paged tables share one knob.
[RequireAdminSection(AdminNavKeys.SearchAnalytics)]
[Route("admin/Search")]
public sealed class SearchAnalyticsController : Controller
{
    private readonly ISearchAnalyticsQueryService _query;
    private readonly ISettingService _settings;

    // The default time window if no ?range= is supplied. Chosen to give admins one week of
    // trend visibility on first landing — enough to see week-over-week zero-result drift
    // without pulling three months of scan back on every load.
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    // The widest a custom range may span. Matches the sink's 90-day retention window: nothing
    // older than 90 days exists in search_events (the retention job purges it), so a request
    // for older data would return an empty tile regardless. Also caps the scan an abusive
    // ?range=custom&from=1990-01-01 could trigger.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(90);

    // The 20-row threshold below which the landing view swaps the tiles + tables for a
    // single empty-state inset. Chosen so p95 latency and zero-result-rate figures are
    // statistically meaningful — below 20 samples the tiles report noise, not signal.
    private const int EmptyStateThreshold = 20;

    // The number of top queries + top zero-result queries shown on the landing tables.
    // 10 is the industry-standard "top-10" list; drill-in views hang off "View all →" links.
    private const int TopNLimit = 10;

    // Fallback drill-in page size if the stored CMS:PageLength value is missing or non-positive.
    // Mirrors ObservabilityController's fallback so all admin paged surfaces share one floor.
    private const int DefaultPageSize = 20;

    public SearchAnalyticsController(
        ISearchAnalyticsQueryService query,
        ISettingService settings,
        ISearchMessageService? messages = null,
        IPortalDbContext? dbContext = null,
        ICurrentUserService? currentUserService = null)
    {
        _query = query;
        _settings = settings;
        _ = messages; _ = dbContext; _ = currentUserService;
    }

    // Session drill-in — compile stubs. Real implementation lands in the session-purge
    // follow-up.
    [HttpGet("Session/{id}")]
    public Task<IActionResult> Session(string id, CancellationToken ct = default)
        => throw new NotImplementedException("Session drill-in not yet implemented.");

    [HttpPost("Session/{id}/Delete")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(string id, CancellationToken ct = default)
        => throw new NotImplementedException("Session drill-in not yet implemented.");

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? range,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Search analytics";

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);

        var totalCount = await _query.GetRowCountAsync(fromUtc, toUtc, ct);
        var hasData = totalCount >= EmptyStateThreshold;

        SearchAnalyticsSummary summary;
        IReadOnlyList<TopQueryRow> topQueries;
        IReadOnlyList<TopQueryRow> topZeroResultQueries;
        IReadOnlyList<VolumeBucket> volumeSeries;

        if (hasData)
        {
            // Only run the heavier aggregates when the empty-state guard has passed. Below
            // 20 rows the view renders a single inset panel and never reads the summary,
            // the top-N tables, or the volume series.
            summary = await _query.GetSummaryAsync(fromUtc, toUtc, ct);
            topQueries = await _query.GetTopQueriesAsync(fromUtc, toUtc, TopNLimit, ct);
            topZeroResultQueries = await _query.GetTopZeroResultQueriesAsync(fromUtc, toUtc, TopNLimit, ct);
            volumeSeries = await _query.GetVolumeOverTimeAsync(fromUtc, toUtc, ct);
        }
        else
        {
            summary = new SearchAnalyticsSummary(totalCount, 0, 0d, 0);
            topQueries = Array.Empty<TopQueryRow>();
            topZeroResultQueries = Array.Empty<TopQueryRow>();
            volumeSeries = Array.Empty<VolumeBucket>();
        }

        return View("~/Views/Admin/Search/Index.cshtml", new SearchAnalyticsIndexViewModel
        {
            Summary = summary,
            TopQueries = topQueries,
            TopZeroResultQueries = topZeroResultQueries,
            VolumeSeries = volumeSeries,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
            HasData = hasData,
            TotalRowCount = totalCount,
        });
    }

    [HttpGet("Queries")]
    public async Task<IActionResult> Queries(
        string? range,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Top queries";

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        var (rows, total) = await _query.GetPagedTopQueriesAsync(fromUtc, toUtc, page, pageSize, ct);

        return View("~/Views/Admin/Search/Queries.cshtml", new SearchAnalyticsQueryDrillInViewModel
        {
            Rows = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
        });
    }

    [HttpGet("ZeroResults")]
    public async Task<IActionResult> ZeroResults(
        string? range,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Zero-result queries";

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        var (rows, total) = await _query.GetPagedTopZeroResultQueriesAsync(fromUtc, toUtc, page, pageSize, ct);

        return View("~/Views/Admin/Search/ZeroResults.cshtml", new SearchAnalyticsQueryDrillInViewModel
        {
            Rows = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
        });
    }

    [HttpGet("Pages")]
    public async Task<IActionResult> Pages(
        string? range,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Top pages by search impressions";

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        var (rows, total) = await _query.GetTopPagesAsync(fromUtc, toUtc, page, pageSize, ct);

        return View("~/Views/Admin/Search/Pages.cshtml", new SearchAnalyticsPagesDrillInViewModel
        {
            Rows = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
        });
    }

    // Reads CMS:PageLength for the drill-in tables. The admin admin setting is trusted verbatim
    // (floor 1 only, no upper clamp) — Phase 1.10 UAT locked this convention: admin editors own
    // the value and drill-ins render it, no URL override. Falls back to DefaultPageSize when the
    // stored value is missing or non-positive.
    private async Task<int> ResolvePageSizeAsync()
    {
        var size = await _settings.GetIntAsync(SettingKeys.CmsPageLength);
        return size > 0 ? size : DefaultPageSize;
    }

    // Resolves the (from, to, rangeKey) triple from the ?range=, ?from=, ?to= query
    // parameters. Preset ranges (24h / 7d / 30d / 90d) win outright; "custom" with both dates
    // supplied is clamped to [UtcNow - 90d, UtcNow] so a hand-edited past-date query string
    // can't force a full-table scan. Anything unrecognised or malformed snaps to the 7-day
    // default — the form is a GET, so a stale query string must still render a dashboard.
    // Made internal so future drill-in actions on the same controller can share one
    // window-resolution rule.
    internal static (DateTime FromUtc, DateTime ToUtc, string RangeKey) ResolveWindow(
        string? range,
        DateTime? from,
        DateTime? to)
    {
        var now = DateTime.UtcNow;

        var presetWindow = range switch
        {
            "24h" => TimeSpan.FromHours(24),
            "7d"  => TimeSpan.FromDays(7),
            "30d" => TimeSpan.FromDays(30),
            "90d" => TimeSpan.FromDays(90),
            _ => (TimeSpan?)null,
        };

        if (presetWindow is { } window)
        {
            return (now - window, now, range!);
        }

        if (string.Equals(range, "custom", StringComparison.Ordinal)
            && from is { } fromRaw
            && to is { } toRaw)
        {
            var fromUtc = AsUtc(fromRaw);
            var toUtc = AsUtc(toRaw);

            // Clamp to the sink's 90-day retention window: nothing older exists, and this
            // blocks a decade-of-events range that would seq-scan the whole table.
            if (toUtc > now) toUtc = now;
            var earliestAllowed = now - MaxWindow;
            if (fromUtc < earliestAllowed) fromUtc = earliestAllowed;

            // If the caller supplied a from-after-to pair, snap to the default rather than
            // rendering a zero-width window (which would look like an empty state to the user
            // without explaining why).
            if (fromUtc >= toUtc)
            {
                return (now - DefaultWindow, now, "7d");
            }

            return (fromUtc, toUtc, "custom");
        }

        return (now - DefaultWindow, now, "7d");
    }

    // Model-bound DateTime values arrive with Kind=Unspecified for an offset-less query
    // string (e.g. ?from=2026-06-01T10:00). Mirror the ObservabilityController convention:
    // an unspecified Kind is treated as UTC; a local-Kind value is converted to UTC; a Utc
    // value is returned unchanged.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };
}
