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

    // Cap on how many raw event points the request-timings scatter renders. 2000 keeps the
    // SVG lightweight while still giving a visually meaningful spread across the plot area.
    // When the window has more events the reader samples via ORDER BY random() so the
    // scatter still spans the whole X-axis; the visible sample count is announced to the
    // admin via a "showing X of Y" hint below the chart.
    private const int RequestTimingsSampleCap = 2000;

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
        string? aggregate = null,
        CancellationToken ct = default)
    {
        // "aggregate=week" collapses the four bucketed series into a 168-cell cyclic
        // typical-week view — Mon 00:00 .. Sun 23:00 — using the weekday-hour aggregate
        // readers. The default (unset) is the raw linear time-series over the window.
        var aggregateMode = string.Equals(aggregate, "week", StringComparison.OrdinalIgnoreCase);
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

        // Fetch all four series server-side so the interactive tiles can swap the rendered
        // chart client-side without a round-trip. Aggregate mode replaces every series with
        // its cyclic weekday-hour variant; the four charts stay X-aligned either way because
        // both spines are deterministic given the same window.
        IReadOnlyList<VolumeBucket> volumeSeries;
        IReadOnlyList<VolumeBucket> uniqueSessionsSeries;
        IReadOnlyList<VolumeBucket> zeroResultCountSeries;
        IReadOnlyList<LatencyBucket> latencyPercentileSeries;
        if (aggregateMode)
        {
            // The volume chart is dual-axis: left Y = searches (SearchCount), right Y =
            // unique sessions (UniqueSessionCount) - both read from the SAME VolumeBucket.
            // In non-aggregate mode GetVolumeOverTimeAsync fills both fields per bucket.
            // In aggregate mode there is no single reader that populates both fields, so
            // fetch the two aggregate readers separately and merge them positionally:
            // both return the same 168-cell (weekday, hour) spine in the same order, so
            // element i on each side describes the same cyclic cell.
            var volumeCells = await _query.GetVolumeAggregatedByWeekdayHourAsync(fromUtc, toUtc, ct);
            var uniqueCells = await _query.GetUniqueSessionsAggregatedByWeekdayHourAsync(fromUtc, toUtc, ct);
            volumeSeries = MergeAggregateVolumeAndUniqueSeries(volumeCells, uniqueCells);
            uniqueSessionsSeries = uniqueCells;
            zeroResultCountSeries = await _query.GetZeroResultCountAggregatedByWeekdayHourAsync(fromUtc, toUtc, ct);
            latencyPercentileSeries = await _query.GetLatencyPercentilesAggregatedByWeekdayHourAsync(fromUtc, toUtc, ct);
        }
        else
        {
            volumeSeries = await _query.GetVolumeOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
            uniqueSessionsSeries = await _query.GetUniqueSessionsOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
            zeroResultCountSeries = await _query.GetZeroResultCountOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
            latencyPercentileSeries = await _query.GetLatencyPercentilesOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
        }

        // Inline top-pages + top-blocks cards: fetch the first page of each reader so the view
        // can render two top-10 tables alongside top-queries and top-zero-result. Total row
        // counts decide whether the "View all top X by search impressions →" link renders on
        // either card. Kinds are split at the query layer so the pages card is genuinely
        // pages-only (blocks used to leak in as plain-text mid-list rows).
        var (topPages, topPagesTotal) = await _query.GetTopPagesAsync(fromUtc, toUtc, page: 1, pageSize: TopNLimit, ct);
        var (topBlocks, topBlocksTotal) = await _query.GetTopBlocksAsync(fromUtc, toUtc, page: 1, pageSize: TopNLimit, ct);

        // Round-7 additions: request-timings scatter (sampled), weekday × hour heatmap,
        // zero-result outcome funnel, prior-window summary for anomaly chips. All four
        // ride the same window bounds as the summary + top-N reads above so the page tells
        // one consistent story about "this window".
        var requestTimings = await _query.GetRequestTimingsAsync(fromUtc, toUtc, RequestTimingsSampleCap, ct);
        var weekdayHourGrid = await _query.GetSearchesByWeekdayAndHourAsync(fromUtc, toUtc, ct);
        var zeroResultOutcomes = await _query.GetZeroResultOutcomeFunnelAsync(fromUtc, toUtc, ct);
        var summaryDeltas = await _query.GetSummaryDeltasAsync(fromUtc, toUtc, ct);
        var recoveryStats = await _query.GetZeroResultRecoveryStatsAsync(fromUtc, toUtc, ct);

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
            TopBlocks = topBlocks,
            TopBlocksTotalCount = topBlocksTotal,
            RequestTimings = requestTimings,
            RequestTimingsTotalCount = totalCount,
            WeekdayHourGrid = weekdayHourGrid,
            ZeroResultOutcomes = zeroResultOutcomes,
            SummaryDeltas = summaryDeltas,
            RecoveryStats = recoveryStats,
            AggregateMode = aggregateMode,
        });
    }

    // Paged drill-in for the request-timings scatter. Shares the shared time-window
    // filter partial + the shared pager so the drill-in matches every other admin
    // paged surface exactly. Reached from the scatter chart's "open the paged view"
    // link when the sample-limit kicks in.
    [HttpGet("RequestTimings")]
    public async Task<IActionResult> RequestTimings(
        string? range,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Request timings";
        ViewData["AdminWide"] = true;

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        var (rows, total) = await _query.GetPagedRequestTimingsAsync(fromUtc, toUtc, page, pageSize, ct);

        return View("~/Views/Admin/Search/RequestTimings.cshtml", new SearchAnalyticsRequestTimingsViewModel
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

    // Zero-result recovery journeys drill-in. Renders one card per session with a full
    // chronological chain of the searches that session ran; only sessions with at least
    // one zero-result event appear. Editors use this to spot common corrections and
    // decide whether to add synonyms, rename content, or create missing pages.
    [HttpGet("ZeroResultJourneys")]
    public async Task<IActionResult> ZeroResultJourneys(
        string? range,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Zero-result recovery journeys";
        ViewData["AdminWide"] = true;

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        var stats = await _query.GetZeroResultRecoveryStatsAsync(fromUtc, toUtc, ct);
        var (rows, total) = await _query.GetZeroResultJourneysAsync(fromUtc, toUtc, page, pageSize, ct);

        return View("~/Views/Admin/Search/ZeroResultJourneys.cshtml", new SearchAnalyticsZeroResultJourneysViewModel
        {
            Rows = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
            RecoveryStats = stats,
        });
    }

    // Heatmap cell drill-in. Reached from clicking a (weekday, hour) cell on the "When
    // people search" heatmap. Renders one row per matching search event inside the
    // current time window with the same shape as the request-timings paged drill-in
    // (timestamp, latency, query, results, session link). Weekday is ISO 8601 (1 = Mon).
    [HttpGet("HeatmapCell")]
    public async Task<IActionResult> HeatmapCell(
        string? range,
        DateTime? from,
        DateTime? to,
        int weekday = 1,
        int hour = 0,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["AdminWide"] = true;

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        // Clamp defensively so a hand-edited URL still lands on a sensible surface.
        if (weekday < 1 || weekday > 7) weekday = 1;
        if (hour < 0 || hour > 23) hour = 0;

        var (rows, total) = await _query.GetPagedEventsInWeekdayHourBucketAsync(
            fromUtc, toUtc, weekday, hour, page, pageSize, ct);

        var weekdayLabel = weekday switch
        {
            1 => "Monday", 2 => "Tuesday", 3 => "Wednesday", 4 => "Thursday",
            5 => "Friday", 6 => "Saturday", 7 => "Sunday", _ => "Monday",
        };
        var hourEnd = (hour + 1) % 24;
        var hourLabel = $"{hour:00}:00–{hourEnd:00}:00 UTC";

        ViewData["Title"] = $"Searches on {weekdayLabel} at {hourLabel}";

        return View("~/Views/Admin/Search/HeatmapCell.cshtml", new SearchAnalyticsHeatmapCellViewModel
        {
            Rows = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
            Weekday = weekday,
            Hour = hour,
            WeekdayLabel = weekdayLabel,
            HourLabel = hourLabel,
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

    // Sibling of the Pages drill-in filtered to block hits. Reuses the same view because the
    // table shape is identical — the view already renders non-URL result_key values as plain
    // text (via the LooksLikeUrl helper), so a block-only result set naturally reads as a
    // list of block keys. Title copy is the only per-action tweak.
    [HttpGet("Blocks")]
    public async Task<IActionResult> Blocks(
        string? range,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = "Top content blocks by search impressions";
        ViewData["AdminWide"] = true;

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        var (rows, total) = await _query.GetTopBlocksAsync(fromUtc, toUtc, page, pageSize, ct);

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

    // Four sibling drill-in actions, one per chart series. Kept as four discrete action names
    // rather than one /Series/{key} passthrough so the URLs read cleanly in server logs and the
    // "View all" link on the landing page can point at a self-describing path — a reader
    // browsing browser history should see /admin/Search/Volume, not /admin/Search/Series/volume.
    [HttpGet("Volume")]
    public Task<IActionResult> Volume(
        string? range, DateTime? from, DateTime? to, string? bucket = null, int page = 1,
        CancellationToken ct = default) =>
        SeriesDrillInAsync("volume", "Search volume over time", range, from, to, bucket, page, ct);

    [HttpGet("UniqueUsers")]
    public Task<IActionResult> UniqueUsers(
        string? range, DateTime? from, DateTime? to, string? bucket = null, int page = 1,
        CancellationToken ct = default) =>
        SeriesDrillInAsync("unique-users", "Unique sessions over time", range, from, to, bucket, page, ct);

    [HttpGet("ZeroResultsOverTime")]
    public Task<IActionResult> ZeroResultsOverTime(
        string? range, DateTime? from, DateTime? to, string? bucket = null, int page = 1,
        CancellationToken ct = default) =>
        SeriesDrillInAsync("zero-results", "Zero-result searches over time", range, from, to, bucket, page, ct);

    [HttpGet("LatencyOverTime")]
    public Task<IActionResult> LatencyOverTime(
        string? range, DateTime? from, DateTime? to, string? bucket = null, int page = 1,
        CancellationToken ct = default) =>
        SeriesDrillInAsync("latency", "Latency percentiles over time", range, from, to, bucket, page, ct);

    private async Task<IActionResult> SeriesDrillInAsync(
        string seriesKey,
        string title,
        string? range,
        DateTime? from,
        DateTime? to,
        string? bucket,
        int page,
        CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.SearchAnalytics;
        ViewData["Title"] = title;
        ViewData["AdminWide"] = true;

        var (fromUtc, toUtc, rangeKey) = ResolveWindow(range, from, to);
        var (bucketSize, bucketKey) = ResolveBucketSize(bucket, toUtc - fromUtc);
        var pageSize = await ResolvePageSizeAsync();
        if (page < 1) page = 1;

        IReadOnlyList<VolumeBucket> chartVolume = Array.Empty<VolumeBucket>();
        IReadOnlyList<LatencyBucket> chartLatency = Array.Empty<LatencyBucket>();
        IReadOnlyList<VolumeBucket> volumeSlice = Array.Empty<VolumeBucket>();
        IReadOnlyList<LatencyBucket> latencySlice = Array.Empty<LatencyBucket>();
        int total;

        // One code path per series so each call reads exactly the series it needs — no
        // wasted queries. Chart always fetches the FULL unpaged spine so the trend matches
        // the landing card; the paged slice underneath drives the drill-in table + pager.
        switch (seriesKey)
        {
            case "volume":
                chartVolume = await _query.GetVolumeOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
                (volumeSlice, total) = await _query.GetPagedVolumeOverTimeAsync(fromUtc, toUtc, bucketSize, page, pageSize, ct);
                break;
            case "unique-users":
                chartVolume = await _query.GetUniqueSessionsOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
                (volumeSlice, total) = await _query.GetPagedUniqueSessionsOverTimeAsync(fromUtc, toUtc, bucketSize, page, pageSize, ct);
                break;
            case "zero-results":
                chartVolume = await _query.GetZeroResultCountOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
                (volumeSlice, total) = await _query.GetPagedZeroResultCountOverTimeAsync(fromUtc, toUtc, bucketSize, page, pageSize, ct);
                break;
            case "latency":
                chartLatency = await _query.GetLatencyPercentilesOverTimeAsync(fromUtc, toUtc, bucketSize, ct);
                (latencySlice, total) = await _query.GetPagedLatencyPercentilesOverTimeAsync(fromUtc, toUtc, bucketSize, page, pageSize, ct);
                break;
            default:
                return NotFound();
        }

        return View("~/Views/Admin/Search/SeriesDrillIn.cshtml", new SearchAnalyticsSeriesDrillInViewModel
        {
            SeriesKey = seriesKey,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RangeKey = rangeKey,
            BucketKey = bucketKey,
            ChartVolumeSeries = chartVolume,
            ChartLatencySeries = chartLatency,
            VolumeSlice = volumeSlice,
            LatencySlice = latencySlice,
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

    // Reads CMS:PageLength for the drill-in tables. The admin setting is trusted verbatim
    // (floor 1 only, no upper clamp): admin editors own the value and drill-ins render it,
    // with no URL override. Falls back to DefaultPageSize when the stored value is missing
    // or non-positive.
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

    // Merges the volume-aggregated and unique-sessions-aggregated readers into a single
    // series that mirrors non-aggregate mode's shape: each bucket carries the total-events
    // count in SearchCount and the distinct-sessions count in UniqueSessionCount. Both
    // aggregate readers return the same 168-cell (weekday, hour) spine in the same order,
    // so a positional merge is safe. The dual-axis volume chart reads BOTH fields from
    // the same bucket - without this merge the aggregate branch's right-Y unique-sessions
    // line renders flat at zero because the volume reader leaves UniqueSessionCount = 0.
    private static IReadOnlyList<VolumeBucket> MergeAggregateVolumeAndUniqueSeries(
        IReadOnlyList<VolumeBucket> volumeCells,
        IReadOnlyList<VolumeBucket> uniqueCells)
    {
        if (volumeCells.Count != uniqueCells.Count)
        {
            // Defensive: both readers must return an identical spine. If they ever
            // diverge (schema drift, new bucket dimension) fall back to volume-only
            // so the chart still renders rather than throwing on a shape mismatch.
            return volumeCells;
        }

        var merged = new VolumeBucket[volumeCells.Count];
        for (var i = 0; i < volumeCells.Count; i++)
        {
            merged[i] = new VolumeBucket(
                volumeCells[i].BucketStart,
                volumeCells[i].SearchCount,
                uniqueCells[i].SearchCount);
        }
        return merged;
    }
}
