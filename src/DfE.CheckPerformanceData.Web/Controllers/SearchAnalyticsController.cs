using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly ISearchMessageService? _messages;
    private readonly IPortalDbContext? _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    // The default time window if no ?range= is supplied. Chosen to give admins one week of
    // trend visibility on first landing — enough to see week-over-week zero-result drift
    // without pulling three months of scan back on every load.
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    // The widest a custom range may span. Matches the sink's 90-day retention window: nothing
    // older than 90 days exists in search_events (the retention job purges it), so a request
    // for older data would return an empty tile regardless. Also caps the scan an abusive
    // ?range=custom&from=1990-01-01 could trigger.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(90);

    // Retained as a public marker for the view — it renders a "small sample, treat
    // percentiles carefully" hint AFTER the data when TotalRowCount is below this. The
    // tiles + tables always render; the hint is a footnote, not a gate.
    public const int SmallSampleThreshold = 20;

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
        _messages = messages;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? range,
        DateTime? from,
        DateTime? to,
        string? bucket = null,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Search analytics";
        // Widen the layout so the tiles + chart + tables get the full viewport when the
        // admin sidebar is collapsed. Every other search-analytics action follows suit.
        ViewData["AdminWide"] = true;

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var (bucketSize, bucketKey) = ResolveBucketSize(bucket, toUtc - fromUtc);

        var totalCount = await _query.GetRowCountAsync(fromUtc, toUtc, ct);

        // Always fetch the aggregates. Admins want to see whatever data is there — the
        // small-sample cost is a hint at the bottom of the page, not a suppression gate.
        var summary = await _query.GetSummaryAsync(fromUtc, toUtc, ct);
        var topQueries = await _query.GetTopQueriesAsync(fromUtc, toUtc, TopNLimit, ct);
        var topZeroResultQueries = await _query.GetTopZeroResultQueriesAsync(fromUtc, toUtc, TopNLimit, ct);

        // Fetch all four bucketed series server-side so the interactive tiles can swap the
        // rendered chart client-side without a round-trip. Every series rides the same
        // bucket-size spine so the four charts stay aligned on the X-axis.
        var volumeSeries = await _query.GetVolumeOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
        var uniqueSessionsSeries = await _query.GetUniqueSessionsOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
        var zeroResultCountSeries = await _query.GetZeroResultCountOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
        var latencyPercentileSeries = await _query.GetLatencyPercentilesOverTimeAsync(fromUtc, toUtc, bucketSize, ct);

        // Inline top-pages card: fetch the first page of the drill-in reader so the view can
        // render a top-10 table alongside top-queries and top-zero-result. Total row count
        // decides whether the "View all top pages by search impressions →" link renders.
        var (topPages, topPagesTotal) = await _query.GetTopPagesAsync(fromUtc, toUtc, page: 1, pageSize: TopNLimit, ct);

        return View("~/Views/Admin/Search/Index.cshtml", new SearchAnalyticsIndexViewModel
        {
            Summary = summary,
            TopQueries = topQueries,
            TopZeroResultQueries = topZeroResultQueries,
            VolumeSeries = volumeSeries,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
            TotalRowCount = totalCount,
            BucketKey = bucketKey,
            UniqueSessionsSeries = uniqueSessionsSeries,
            ZeroResultCountSeries = zeroResultCountSeries,
            LatencyPercentileSeries = latencyPercentileSeries,
            TopPages = topPages,
            TopPagesTotalCount = topPagesTotal,
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
        ViewData["AdminWide"] = true;

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
        ViewData["AdminWide"] = true;

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
        ViewData["AdminWide"] = true;

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

    [HttpGet("Session/{id}")]
    public async Task<IActionResult> Session(string id, CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Session";
        ViewData["AdminWide"] = true;

        var events = await _query.GetSessionHistoryAsync(id, ct);

        // 404 on empty — an unknown session id should not render an empty drill-in that
        // implicitly confirms the id doesn't exist to anyone hitting the URL blind.
        if (events.Count == 0)
        {
            return NotFound();
        }

        var messages = _messages is null
            ? Array.Empty<SearchMessageSummary>()
            : await _messages.GetForSessionAsync(id, ct);

        return View("~/Views/Admin/Search/Session.cshtml", new SearchSessionDrillInViewModel
        {
            SessionId = id,
            Events = events,
            Messages = messages,
        });
    }

    [HttpPost("Session/{id}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        // Purge is admin-only, XSRF-gated, and confirm-modal-gated in the view. The
        // three deletes + the audit-entry write share one transaction so a partial
        // delete without a matching audit row is impossible.
        if (_dbContext is null || _messages is null)
        {
            // Bare construction (no persistence wired) — refuse rather than silently
            // half-purge. Production DI always wires both.
            return StatusCode(500);
        }

        var eventsDeleted = 0;
        var resultsDeleted = 0;
        var messagesDeleted = 0;

        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            // Pre-count the child rows first — Postgres does not surface CASCADE child
            // counts from the parent DELETE, so we count them explicitly before the
            // parent delete so the audit payload carries an accurate resultsDeleted
            // figure. Same transaction, same isolation snapshot, so the count and the
            // delete see the same rows.
            resultsDeleted = await _dbContext.SearchEventResults
                .Where(r => _dbContext.SearchEvents.Any(e => e.Id == r.SearchEventId && e.SessionId == id))
                .CountAsync(ct);

            eventsDeleted = await _dbContext.SearchEvents
                .Where(e => e.SessionId == id)
                .ExecuteDeleteAsync(ct);

            var purge = await _messages.PurgeSessionAsync(id, ct);
            messagesDeleted = purge.MessagesDeleted;

            var payload = JsonSerializer.Serialize(new
            {
                eventsDeleted,
                resultsDeleted,
                messagesDeleted,
                deletedBy = _currentUserService?.UserId,
                deletedAt = DateTime.UtcNow,
            });

            _dbContext.AuditEntries.Add(new AuditEntry
            {
                EntityType = "SearchSession",
                EntityId = id,
                Action = "SearchSessionDelete",
                NewValues = payload,
                Timestamp = DateTime.UtcNow,
                UserId = _currentUserService?.UserId,
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        if (TempData is not null)
        {
            TempData["SearchSessionDeletedCounts"] =
                $"{eventsDeleted} events, {resultsDeleted} results, {messagesDeleted} messages";
        }

        return Redirect("/admin/Search/");
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

    // Resolves the ?bucket= query string into a (bucketSize, key) pair. The five explicit
    // sizes are 15m / 1h / 1d / 1w / 1mo; anything else falls back to the auto-picked
    // default from the window width — mirrors the earlier chart-granularity rule so a
    // stale query string is never a source of error.
    //
    // Auto rule:
    //   <= 48h  → 1h  (Hour)
    //   <= 30d  → 1d  (Day)
    //    > 30d  → 1w  (Week)
    // The upper "wider → week" band avoids emitting 90+ hour ticks on a 90-day view.
    // Exposed as public-static so the controller-level test can pin the mapping without
    // going through the HTTP surface. The method is a pure helper with no I/O — every
    // caller in production is the Index action above.
    public static (VolumeBucketSize BucketSize, string BucketKey) ResolveBucketSize(
        string? bucket,
        TimeSpan window)
    {
        return bucket switch
        {
            "15m" => (VolumeBucketSize.FifteenMinutes, "15m"),
            "1h"  => (VolumeBucketSize.Hour,           "1h"),
            "1d"  => (VolumeBucketSize.Day,            "1d"),
            "1w"  => (VolumeBucketSize.Week,           "1w"),
            "1mo" => (VolumeBucketSize.Month,          "1mo"),
            _ when window <= TimeSpan.FromHours(48) => (VolumeBucketSize.Hour, "1h"),
            _ when window <= TimeSpan.FromDays(30)  => (VolumeBucketSize.Day,  "1d"),
            _ => (VolumeBucketSize.Week, "1w"),
        };
    }
}
