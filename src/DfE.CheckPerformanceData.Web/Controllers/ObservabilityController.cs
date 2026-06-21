using System.Globalization;
using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The always-on observability dashboard: a server-rendered health strip, plain-English status
// sentence, big-number tiles and accessible SVG charts, plus the per-message journey timeline
// and the live SSE snapshot stream that refreshes the board. Every action is role-gated
// cypmd_admin — the stream included, so there is never an unauthenticated firehose. The
// dashboard's window/granularity selection resolves against the DashboardRanges allow-list
// before any aggregation runs.
public sealed class ObservabilityController : Controller
{
    private readonly IMetricsQueryService _query;
    private readonly IQueueAdminService _queueAdmin;
    private readonly IHealthEvaluator _health;
    private readonly StatusSentenceBuilder _sentence;
    private readonly ISettingService? _settings;
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _hostEnvironment;

    public ObservabilityController(
        IMetricsQueryService query,
        IQueueAdminService queueAdmin,
        IHealthEvaluator health,
        StatusSentenceBuilder sentence,
        ISettingService? settings = null,
        IConfiguration? configuration = null,
        IHostEnvironment? hostEnvironment = null)
    {
        _query = query;
        _queueAdmin = queueAdmin;
        _health = health;
        _sentence = sentence;
        _settings = settings;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
    }

    // The dashboard is always-on admin; its collapsible Demo panel (drive / inject / seed / replay /
    // demo-trickle, folded in from the retired /dev/uat page) is the only dev-gated part. Gate it on
    // Dev:ToolsEnabled AND not-production, mirroring the /dev/* surfaces — even if a production
    // deploy leaves the flag on, IsProduction short-circuits it off. Defaults to off when no config
    // or environment is wired (bare unit construction).
    private bool DemoToolsEnabled =>
        _configuration?.GetValue<bool>(SettingKeys.DevToolsEnabled) == true
        && _hostEnvironment?.IsProduction() != true;

    // The default dashboard time window and the queues whose health is shown on the strip.
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    private static readonly IReadOnlyDictionary<string, string> Queues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [QueueOptions.RulesEngineQueue] = "Rules engine queue",
            [QueueOptions.ZendeskQueue] = "Zendesk queue",
        };

    // The heartbeat tick is capped at 30 seconds so the SSE connection is never idle long
    // enough for the AKS ingress proxy-read-timeout (~60s) to close it; a shorter value can be
    // configured but a longer one is clamped back to 30.
    private const int MaxHeartbeatSeconds = 30;

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability")]
    public async Task<IActionResult> Index(
        string? range = null,
        string? granularity = null,
        string? from = null,
        string? to = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // The window resolves against the server-side allow-list before any query runs: a named
        // calendar/rolling range, or a custom from/to (to defaults to now) clamped to the max span.
        // The granularity then resolves against that range's legible pairing. Anything unrecognised
        // snaps to a safe default rather than erroring — the form is a GET and a stale or hand-edited
        // query string must still render a dashboard.
        var resolved = DashboardRanges.ResolveWindow(range, ParseUtc(from), ParseUtc(to), now);
        var rangeOption = resolved.Option;
        var windowFrom = resolved.From;
        var windowTo = resolved.To;
        var bucketSize = DashboardRanges.ResolveGranularity(
            resolved.DefaultGranularity, resolved.AllowedGranularities, granularity);

        // The headline tiles (processed today, the decision counters, typical end-to-end) describe
        // TODAY — since midnight UTC — whatever window the charts show. When Today is the selected
        // range the chart series ARE the headline series, so they are reused rather than re-queried.
        var headlineFrom = now.Date;
        var todaySelected = rangeOption.IsToday;

        var thresholds = await ResolveThresholdsAsync();

        var depths = await _queueAdmin.GetQueueDepthsAsync(cancellationToken);
        var dlqCount = await _queueAdmin.GetDlqCountAsync(cancellationToken);

        var queueHealth = new List<QueueHealth>();
        foreach (var (queueName, displayName) in Queues)
        {
            var depth = depths.FirstOrDefault(d => d.QueueName == queueName);
            var inputs = new HealthInputs(depth?.Depth ?? 0, depth?.OldestMessageAge, dlqCount);
            queueHealth.Add(new QueueHealth(
                queueName, displayName,
                _health.Evaluate(inputs, thresholds),
                _health.Explain(inputs, thresholds)));
        }

        // The overall light is the worst of the per-queue lights so the at-a-glance answer never
        // looks healthier than its unhappiest queue — and it carries that queue's reasons so the
        // headline light explains its own state too.
        var worstQueue = queueHealth
            .OrderByDescending(q => (int)q.State.Level)
            .FirstOrDefault();
        var overall = worstQueue?.State
            ?? _health.Evaluate(new HealthInputs(0, null, dlqCount), thresholds);
        var overallReasons = worstQueue?.Reasons ?? Array.Empty<HealthReason>();

        // When every queue light is identical to the overall — same level and the same set of
        // reasons (the usual case when the only problem is a shared dead-letter backlog that trips
        // them all the same way) — the per-queue blocks just repeat the overall, so suppress them
        // and show the single overall block. Reasons compared as sets (HealthReason is a record).
        var showPerQueueHealth = !(queueHealth.Count > 0 && queueHealth.All(q =>
            q.State.Level == overall.Level
            && q.Reasons.Count == overallReasons.Count
            && !q.Reasons.Except(overallReasons).Any()));

        var throughput = await _query.GetThroughputAsync(
            QueueOptions.ZendeskQueue, bucketSize, windowFrom, windowTo, cancellationToken);
        var decisionMix = await _query.GetDecisionMixAsync(windowFrom, windowTo, cancellationToken);
        var decisionMixOverTime = await _query.GetDecisionMixOverTimeAsync(
            bucketSize, windowFrom, windowTo, cancellationToken);
        var dwell = await _query.GetDwellByStageAsync(windowFrom, windowTo, cancellationToken);
        var markers = await _query.GetDeployMarkersAsync(windowFrom, windowTo, cancellationToken);

        // The real-time per-step averages strip describes the last <stats window> of traffic,
        // independent of the chart window; the strip refreshes itself from the JSON endpoint after.
        var statsWindow = DashboardRanges.DefaultStatsWindow;
        var stageAverages = await _query.GetStageAveragesAsync(
            now - DashboardRanges.LookbackFor(statsWindow), now, cancellationToken);

        // The recent-submissions matrix is server-rendered from the grouped (one-row-per-reference)
        // transactions over the last 24 hours, so the table is populated on load and the board engine
        // seeds its live grid from real history rather than starting empty.
        var recentSubmissions = await _query.GetGroupedTransactionsAsync(
            1, RecentSubmissionsCount, now - DefaultWindow, now, null, cancellationToken: cancellationToken);

        // The headline figures describe today (since midnight); reuse the chart series when Today
        // is the selected window, otherwise take a since-midnight read alongside the chart window.
        var headlineThroughput = todaySelected
            ? throughput
            : await _query.GetThroughputAsync(
                QueueOptions.ZendeskQueue, ThroughputGranularity.Hour, headlineFrom, now, cancellationToken);
        var headlineDwell = todaySelected
            ? dwell
            : await _query.GetDwellByStageAsync(headlineFrom, now, cancellationToken);

        var processedToday = headlineThroughput.Sum(b => b.Count);
        var typicalEndToEnd = headlineDwell.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(headlineDwell.Sum(d => d.AverageLatencyMs));

        // The decision counters, like the processed total, describe today (since midnight). Reuse
        // the chart decision-mix when Today is selected; otherwise take a since-midnight read.
        var headlineDecisionMix = todaySelected
            ? decisionMix
            : await _query.GetDecisionMixAsync(headlineFrom, now, cancellationToken);

        int DecisionCount(string status) => headlineDecisionMix
            .Where(d => string.Equals(d.DecisionStatus, status, StringComparison.OrdinalIgnoreCase))
            .Sum(d => d.Count);

        var sentence = _sentence.Build(overall.Level, processedToday, typicalEndToEnd);

        var model = new DashboardViewModel
        {
            QueueHealth = queueHealth,
            OverallHealth = overall,
            OverallReasons = overallReasons,
            ShowPerQueueHealth = showPerQueueHealth,
            StatusSentence = sentence,
            ProcessedToday = processedToday,
            TypicalEndToEnd = typicalEndToEnd,
            AutoApprovedToday = DecisionCount("AutoApproved"),
            AutoRejectedToday = DecisionCount("AutoRejected"),
            ScrutinyToday = DecisionCount("Scrutiny"),
            DeadLetterCount = dlqCount,
            Depths = depths
                .Select(d => new QueueDepthSnapshot(d.QueueName, d.Depth, d.OldestMessageAge))
                .ToList(),
            RecentSubmissions = recentSubmissions.Rows,
            Throughput = throughput,
            DecisionMix = decisionMix,
            DecisionMixOverTime = decisionMixOverTime,
            Dwell = dwell,
            DeployMarkers = markers,
            SelectedRange = rangeOption.Value,
            SelectedGranularity = bucketSize,
            RangeLabel = RangeLabelFor(rangeOption, windowFrom, windowTo),
            GranularityLabel = DashboardRanges.Describe(bucketSize),
            GranularityOptions = resolved.AllowedGranularities,
            IsCustomRange = rangeOption.Value == DashboardRanges.CustomValue,
            SelectedFromUtc = windowFrom,
            SelectedToUtc = windowTo,
            StageAverages = stageAverages,
            StatsWindow = statsWindow,
            StatsWindowOptions = DashboardRanges.StatsWindowGranularities,
            RefreshedAtUtc = now,
            DemoToolsEnabled = DemoToolsEnabled,
        };

        return View(model);
    }

    // A custom range has no fixed label, so describe it by its dates; named ranges use their label.
    private static string RangeLabelFor(DashboardRangeOption option, DateTime from, DateTime to) =>
        option.Value == DashboardRanges.CustomValue
            ? $"{from:d MMM yyyy HH:mm}–{to:d MMM yyyy HH:mm} UTC"
            : option.Label;

    // The dashboard export: the data BEHIND the charts as CSV, never a chart image. It reads the
    // same MetricsQueryService series the dashboard renders, at the same resolved range/granularity,
    // so the file is accurate by construction. Role-gated cypmd_admin like every other action.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/export.csv")]
    public async Task<IActionResult> Export(
        string? range = null,
        string? granularity = null,
        string? from = null,
        string? to = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var resolved = DashboardRanges.ResolveWindow(range, ParseUtc(from), ParseUtc(to), now);
        var rangeOption = resolved.Option;
        var windowFrom = resolved.From;
        var windowTo = resolved.To;
        var bucketSize = DashboardRanges.ResolveGranularity(
            resolved.DefaultGranularity, resolved.AllowedGranularities, granularity);
        var headlineFrom = now.Date;
        var todaySelected = rangeOption.IsToday;

        var throughput = await _query.GetThroughputAsync(
            QueueOptions.ZendeskQueue, bucketSize, windowFrom, windowTo, cancellationToken);
        var decisionMix = await _query.GetDecisionMixAsync(windowFrom, windowTo, cancellationToken);
        var dwell = await _query.GetDwellByStageAsync(windowFrom, windowTo, cancellationToken);

        // The headline figures describe today (since midnight), mirroring the dashboard tiles.
        var headlineThroughput = todaySelected
            ? throughput
            : await _query.GetThroughputAsync(
                QueueOptions.ZendeskQueue, ThroughputGranularity.Hour, headlineFrom, now, cancellationToken);
        var headlineDwell = todaySelected
            ? dwell
            : await _query.GetDwellByStageAsync(headlineFrom, now, cancellationToken);

        var processedToday = headlineThroughput.Sum(b => b.Count);
        var typicalEndToEnd = headlineDwell.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(headlineDwell.Sum(d => d.AverageLatencyMs));

        var csv = MetricsCsvBuilder.Build(new MetricsCsvData
        {
            RangeLabel = RangeLabelFor(rangeOption, windowFrom, windowTo),
            GranularityLabel = DashboardRanges.Describe(bucketSize),
            ProcessedToday = processedToday,
            TypicalEndToEnd = typicalEndToEnd,
            Throughput = throughput,
            DecisionMix = decisionMix,
            Dwell = dwell,
        });

        var fileName = $"pipeline-dashboard-{now:yyyyMMdd-HHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
    }

    // The Excel export: a single .xlsx with one tab per dashboard chart (throughput, decision mix,
    // decision mix over time, per-stage dwell), each carrying the data AND an embedded native chart
    // matching the on-screen one. Built with the MIT-licensed Open XML SDK from the same query
    // series, at the same resolved range/granularity, so the workbook is accurate by construction.
    // Role-gated cypmd_admin like every other action.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/export.xlsx")]
    public async Task<IActionResult> ExportExcel(
        string? range = null,
        string? granularity = null,
        string? from = null,
        string? to = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var resolved = DashboardRanges.ResolveWindow(range, ParseUtc(from), ParseUtc(to), now);
        var rangeOption = resolved.Option;
        var windowFrom = resolved.From;
        var windowTo = resolved.To;
        var bucketSize = DashboardRanges.ResolveGranularity(
            resolved.DefaultGranularity, resolved.AllowedGranularities, granularity);
        var headlineFrom = now.Date;
        var todaySelected = rangeOption.IsToday;

        var throughput = await _query.GetThroughputAsync(
            QueueOptions.ZendeskQueue, bucketSize, windowFrom, windowTo, cancellationToken);
        var decisionMix = await _query.GetDecisionMixAsync(windowFrom, windowTo, cancellationToken);
        var decisionMixOverTime = await _query.GetDecisionMixOverTimeAsync(
            bucketSize, windowFrom, windowTo, cancellationToken);
        var dwell = await _query.GetDwellByStageAsync(windowFrom, windowTo, cancellationToken);

        // The headline figures describe today (since midnight), mirroring the dashboard tiles.
        var headlineThroughput = todaySelected
            ? throughput
            : await _query.GetThroughputAsync(
                QueueOptions.ZendeskQueue, ThroughputGranularity.Hour, headlineFrom, now, cancellationToken);
        var headlineDwell = todaySelected
            ? dwell
            : await _query.GetDwellByStageAsync(headlineFrom, now, cancellationToken);

        var processedToday = headlineThroughput.Sum(b => b.Count);
        var typicalEndToEnd = headlineDwell.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(headlineDwell.Sum(d => d.AverageLatencyMs));

        var bytes = MetricsWorkbookBuilder.Build(new MetricsWorkbookData
        {
            RangeLabel = RangeLabelFor(rangeOption, windowFrom, windowTo),
            GranularityLabel = DashboardRanges.Describe(bucketSize),
            ProcessedToday = processedToday,
            TypicalEndToEnd = typicalEndToEnd,
            Throughput = throughput,
            DecisionMix = decisionMix,
            DecisionMixOverTime = decisionMixOverTime,
            Dwell = dwell,
        });

        var fileName = $"pipeline-dashboard-{now:yyyyMMdd-HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // The real-time per-step averages for the stats strip, over the last <window> of traffic. The
    // window is a granularity from the same allow-list the charts use, resolved server-side; an
    // unknown value snaps to the default (last hour). Role-gated cypmd_admin.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/stage-averages.json")]
    public async Task<IActionResult> StageAveragesJson(string? window, CancellationToken cancellationToken = default)
    {
        var granularity = DashboardRanges.ResolveStatsWindow(window);
        var now = DateTime.UtcNow;
        var averages = await _query.GetStageAveragesAsync(
            now - DashboardRanges.LookbackFor(granularity), now, cancellationToken);

        return Json(new
        {
            rulesQueueMs = averages.RulesQueueMs,
            rulesEngineMs = averages.RulesEngineMs,
            zendeskQueueMs = averages.ZendeskQueueMs,
            ticketMs = averages.TicketMs,
            window = granularity.ToString(),
        });
    }

    // The full transactions list: a paged, newest-first table of every recorded queue metric event
    // (timestamp, reference, stage, queue, decision, latency). The "Recent transitions" panel on
    // the dashboard caps at ~10 and links here for the complete history. Paging is by the
    // Wiki:PageLength setting and done in SQL (Skip/Take + COUNT) — the whole table is never loaded
    // into memory. An optional from/to window narrows the list. Role-gated cypmd_admin.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/transactions")]
    public async Task<IActionResult> Transactions(
        int page = 1,
        string? from = null,
        string? to = null,
        string? reference = null,
        bool group = false,
        [FromQuery(Name = "stage")] string[]? stage = null,
        string? sort = null,
        string? dir = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;

        var pageSize = await ResolvePageSizeAsync();

        // Parsed explicitly rather than via DateTime model binding, which does not bind these query
        // values (see ParseUtc) — the date-window filter on this page silently never applied before.
        var fromUtc = ParseUtc(from);
        var toUtc = ParseUtc(to);

        // A blank search box is no filter; trim it so trailing whitespace from a paste does not
        // produce an always-empty result. The filter is carried back onto the model so the search
        // box stays populated and the pager links preserve it across pages.
        var referenceFilter = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();

        // The ticked stage filters, narrowed to the known stages; carried back so the boxes stay
        // ticked across paging and sorting.
        var stageFilter = (stage ?? Array.Empty<string>())
            .Where(s => MetricStages.All.Contains(s, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The sort direction is shared by both views; the key resolves against whichever allow-list
        // matches the active view, so a grouped key never reaches the flat ORDER BY and vice versa.
        var descending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

        // Group-by-message switches to one row per reference across the pipeline stages (the
        // dashboard matrix picture); the default is the flat one-row-per-event list.
        if (group)
        {
            // The grouped headers submit grouped sort keys; resolve against the grouped allow-list so
            // the headers render the active caret and the query orders by the chosen column.
            var groupedSortKey = GroupedTransactionSort.Resolve(sort);

            var grouped = await _query.GetGroupedTransactionsAsync(
                page, pageSize, fromUtc, toUtc, referenceFilter, groupedSortKey, descending, cancellationToken);

            return View(new TransactionsViewModel
            {
                Grouped = true,
                GroupedRows = grouped.Rows,
                TotalCount = grouped.TotalCount,
                Page = grouped.Page,
                PageSize = grouped.PageSize,
                TotalPages = TotalPages(grouped.TotalCount, grouped.PageSize),
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Reference = referenceFilter,
                SortKey = groupedSortKey,
                SortDescending = descending,
            });
        }

        // The flat one-row-per-event list: a validated flat sort key (unknown → newest-first).
        var sortKey = TransactionSort.Resolve(sort);

        var result = await _query.GetTransactionsAsync(
            page, pageSize, fromUtc, toUtc, referenceFilter, stageFilter, sortKey, descending, cancellationToken);

        return View(new TransactionsViewModel
        {
            Rows = result.Rows,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = TotalPages(result.TotalCount, result.PageSize),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Reference = referenceFilter,
            Stages = stageFilter,
            SortKey = sortKey,
            SortDescending = descending,
        });
    }

    // The in-dashboard replay submission picker reads its list from here: a newest-first set of
    // distinct references (with submitted time + resolved decision) from a recent window, as JSON.
    // Replaces the standalone submissions page — the picker now lives in the dashboard Demo panel
    // and animates the chosen submissions through the board. The list is a generous recent cap so a
    // client-side search can narrow it without another round trip. Role-gated cypmd_admin.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/submissions.json")]
    public async Task<IActionResult> SubmissionsJson(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var result = await _query.GetSubmissionsAsync(1, 100, now - DefaultWindow, null, cancellationToken);

        var rows = result.Rows.Select(r => new
        {
            referenceNumber = r.ReferenceNumber,
            submittedAtUtc = r.SubmittedAtUtc,
            decision = r.ResolvedDecision,
        });

        return Json(new { rows });
    }

    // The recorded events for the picked submissions, combined and time-ordered, as JSON — so the
    // dashboard board can animate just those references through the stages, obeying the demo slow-mo
    // / single-step. Replaces the standalone walkthrough page (which duplicated the board). The
    // cohort is capped so a hand-edited query string cannot fan out to an unbounded read. Role-gated.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/replay/selected")]
    public async Task<IActionResult> ReplaySelected(
        [FromQuery(Name = "reference")] string[]? reference = null,
        CancellationToken cancellationToken = default)
    {
        var references = (reference ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();

        var events = new List<JourneyEvent>();
        foreach (var refNumber in references)
            events.AddRange(await _query.GetJourneyAsync(refNumber, cancellationToken));

        return Json(events.OrderBy(e => e.RecordedAtUtc));
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/journey/{reference}")]
    public async Task<IActionResult> Journey(string reference, CancellationToken cancellationToken = default)
    {
        var events = await _query.GetJourneyAsync(reference, cancellationToken);

        return View(new JourneyViewModel
        {
            ReferenceNumber = reference,
            Events = events,
        });
    }

    // The click-to-inspect panel behind a board token: the journey only — the decision the
    // message reached plus its per-stage queue status. No payload is shown here: the board keys
    // tokens by reference number, and metrics are only recorded after ack/dead-letter, by which
    // point the queue row (and its payload) is gone. Payload viewing stays on the queue admin
    // surfaces, which carry the redaction and audit discipline.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/inspect/{reference}")]
    public async Task<IActionResult> Inspect(string reference, CancellationToken cancellationToken = default)
    {
        var stages = await _query.GetJourneyAsync(reference, cancellationToken);

        // The decision is whatever the most recent recorded stage carried; null if none decided yet.
        var decision = stages
            .Where(e => !string.IsNullOrEmpty(e.DecisionStatus))
            .Select(e => e.DecisionStatus)
            .LastOrDefault();

        return View(new InspectViewModel
        {
            ReferenceNumber = reference,
            Decision = decision,
            Stages = stages,
        });
    }

    // The always-on board replay window. Returns the recorded stage transitions across the
    // requested window so the board can re-animate real historical traffic through the same
    // engine on a scrubber clock. This is role-gated cypmd_admin ONLY — it carries no
    // Dev:ToolsEnabled gate, because replay is a locked always-on surface present in every
    // environment. The payload is aggregate transition events (queue/stage/reference/decision)
    // with no pupil data, mirroring the snapshot schema.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/replay")]
    public async Task<IActionResult> Replay(
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var toUtc = AsUtc(to ?? DateTime.UtcNow);
        var fromUtc = from is null ? toUtc - DefaultWindow : AsUtc(from.Value);

        try
        {
            var events = await _query.GetReplayWindowAsync(fromUtc, toUtc, cancellationToken);
            return Json(events);
        }
        catch (ArgumentException ex)
        {
            // An over-wide range is a client error, mirroring the throughput guard.
            return BadRequest(ex.Message);
        }
    }

    // Model-bound DateTime values arrive with Kind=Unspecified for an offset-less query string
    // (e.g. ?from=2026-06-01T10:00). The query service binds them to a timestamptz parameter,
    // which throws InvalidCastException for a non-UTC Kind — an exception the ArgumentException
    // guards do not catch, so the request 500s. Treat an unspecified Kind as UTC; convert any
    // local-Kind value to UTC. A value already tagged Utc is returned unchanged.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };

    // Parses a from/to query value (a datetime-local "yyyy-MM-ddTHH:mm", or any ISO-ish form) to
    // UTC. Done explicitly because ASP.NET's default model binder does not bind a DateTime from these
    // query values here — a string parameter always binds, and parsing ourselves keeps the UTC
    // handling unambiguous (a naive value is read as UTC, a zoned value is converted to UTC).
    private static DateTime? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/stream")]
    public IResult Stream(CancellationToken cancellationToken = default)
    {
        var heartbeat = ResolveHeartbeat();
        return Results.ServerSentEvents(
            StreamSnapshotsAsync(heartbeat, cancellationToken),
            eventType: "snapshot");
    }

    // Yields one snapshot immediately, then one on every heartbeat tick. Pushing on a <=30s
    // cadence keeps the long-lived response from ever sitting idle long enough for the ingress
    // to close it; EventSource reconnects automatically if the stream does drop.
    private async IAsyncEnumerable<ObservabilitySnapshot> StreamSnapshotsAsync(
        TimeSpan heartbeat,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await _query.GetCurrentSnapshotAsync(cancellationToken);

        using var timer = new PeriodicTimer(heartbeat);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            yield return await _query.GetCurrentSnapshotAsync(cancellationToken);
        }
    }

    private TimeSpan ResolveHeartbeat()
    {
        var configured = HttpContext?.RequestServices
            .GetService(typeof(IConfiguration)) as IConfiguration;
        var seconds = configured?.GetValue("Observability:HeartbeatSeconds", MaxHeartbeatSeconds)
            ?? MaxHeartbeatSeconds;
        if (seconds <= 0 || seconds > MaxHeartbeatSeconds)
            seconds = MaxHeartbeatSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    // The rows-per-page for the paged admin lists, read from the generic Wiki:PageLength setting
    // (the same setting the deleted-pages list and search use) so all paged lists share one knob.
    // Falls back to 20 when no settings service is wired (bare unit construction) or the stored
    // value is non-positive.
    private const int DefaultPageLength = 20;

    // How many recent submissions the dashboard matrix server-renders (and the board engine seeds
    // its live grid from). Matches the engine's visible scroll depth; the full paged history is on
    // the transactions page.
    private const int RecentSubmissionsCount = 25;

    private async Task<int> ResolvePageSizeAsync()
    {
        if (_settings is null)
            return DefaultPageLength;

        var size = await _settings.GetIntAsync(SettingKeys.WikiPageLength);
        return size > 0 ? size : DefaultPageLength;
    }

    private static int TotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private async Task<HealthThresholds> ResolveThresholdsAsync()
    {
        if (_settings is null)
            return DefaultThresholds;

        return new HealthThresholds(
            DepthAmber: await _settings.GetIntAsync(SettingKeys.HealthDepthAmber),
            DepthRed: await _settings.GetIntAsync(SettingKeys.HealthDepthRed),
            OldestAgeAmberSeconds: await _settings.GetIntAsync(SettingKeys.HealthOldestAgeAmberSeconds),
            OldestAgeRedSeconds: await _settings.GetIntAsync(SettingKeys.HealthOldestAgeRedSeconds),
            DlqRateRed: await _settings.GetIntAsync(SettingKeys.HealthDlqRateRed),
            DlqRateAmber: await _settings.GetIntAsync(SettingKeys.HealthDlqRateAmber));
    }

    private static readonly HealthThresholds DefaultThresholds =
        new(DepthAmber: 25, DepthRed: 100, OldestAgeAmberSeconds: 120, OldestAgeRedSeconds: 600,
            DlqRateRed: 5, DlqRateAmber: 3);
}
