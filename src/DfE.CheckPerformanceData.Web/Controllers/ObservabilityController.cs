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
        CancellationToken cancellationToken = default)
    {
        // Both selections resolve against the server-side allow-list before any query runs;
        // anything unrecognised snaps to the default window rather than erroring, because the
        // form is a GET and a stale or hand-edited query string must still render a dashboard.
        var rangeOption = DashboardRanges.Resolve(range);
        var bucketSize = DashboardRanges.ResolveGranularity(rangeOption, granularity);

        var now = DateTime.UtcNow;
        var from = now - rangeOption.Window;

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

        var throughput = await _query.GetThroughputAsync(
            QueueOptions.ZendeskQueue, bucketSize, from, now, cancellationToken);
        var decisionMix = await _query.GetDecisionMixAsync(from, now, cancellationToken);
        var decisionMixOverTime = await _query.GetDecisionMixOverTimeAsync(
            bucketSize, from, now, cancellationToken);
        var dwell = await _query.GetDwellByStageAsync(from, now, cancellationToken);
        var markers = await _query.GetDeployMarkersAsync(from, now, cancellationToken);

        // The headline tiles and the status sentence always describe the last 24 hours,
        // whatever window the charts are showing; the chart series double as the headline
        // source only when the selected window IS the default. The processed total is
        // granularity-independent (it is a sum over the window), so only the window matters.
        var headlineThroughput = rangeOption.Window == DefaultWindow
            ? throughput
            : await _query.GetThroughputAsync(
                QueueOptions.ZendeskQueue, ThroughputGranularity.Hour, now - DefaultWindow, now, cancellationToken);
        var headlineDwell = rangeOption.Window == DefaultWindow
            ? dwell
            : await _query.GetDwellByStageAsync(now - DefaultWindow, now, cancellationToken);

        var processedToday = headlineThroughput.Sum(b => b.Count);
        var typicalEndToEnd = headlineDwell.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(headlineDwell.Sum(d => d.AverageLatencyMs));

        var sentence = _sentence.Build(overall.Level, processedToday, typicalEndToEnd);

        var model = new DashboardViewModel
        {
            QueueHealth = queueHealth,
            OverallHealth = overall,
            OverallReasons = overallReasons,
            StatusSentence = sentence,
            ProcessedToday = processedToday,
            TypicalEndToEnd = typicalEndToEnd,
            Depths = depths
                .Select(d => new QueueDepthSnapshot(d.QueueName, d.Depth, d.OldestMessageAge))
                .ToList(),
            Throughput = throughput,
            DecisionMix = decisionMix,
            DecisionMixOverTime = decisionMixOverTime,
            Dwell = dwell,
            DeployMarkers = markers,
            SelectedRange = rangeOption.Value,
            SelectedGranularity = bucketSize,
            RangeLabel = rangeOption.Label,
            GranularityLabel = DashboardRanges.Describe(bucketSize),
            GranularityOptions = rangeOption.AllowedGranularities,
            RefreshedAtUtc = now,
            DemoToolsEnabled = DemoToolsEnabled,
        };

        return View(model);
    }

    // The dashboard export: the data BEHIND the charts as CSV, never a chart image. It reads the
    // same MetricsQueryService series the dashboard renders, at the same resolved range/granularity,
    // so the file is accurate by construction. Role-gated cypmd_admin like every other action.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/export.csv")]
    public async Task<IActionResult> Export(
        string? range = null,
        string? granularity = null,
        CancellationToken cancellationToken = default)
    {
        var rangeOption = DashboardRanges.Resolve(range);
        var bucketSize = DashboardRanges.ResolveGranularity(rangeOption, granularity);

        var now = DateTime.UtcNow;
        var from = now - rangeOption.Window;

        var throughput = await _query.GetThroughputAsync(
            QueueOptions.ZendeskQueue, bucketSize, from, now, cancellationToken);
        var decisionMix = await _query.GetDecisionMixAsync(from, now, cancellationToken);
        var dwell = await _query.GetDwellByStageAsync(from, now, cancellationToken);

        // The headline figures always describe the last 24 hours, mirroring the dashboard tiles.
        var headlineThroughput = rangeOption.Window == DefaultWindow
            ? throughput
            : await _query.GetThroughputAsync(
                QueueOptions.ZendeskQueue, ThroughputGranularity.Hour, now - DefaultWindow, now, cancellationToken);
        var headlineDwell = rangeOption.Window == DefaultWindow
            ? dwell
            : await _query.GetDwellByStageAsync(now - DefaultWindow, now, cancellationToken);

        var processedToday = headlineThroughput.Sum(b => b.Count);
        var typicalEndToEnd = headlineDwell.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(headlineDwell.Sum(d => d.AverageLatencyMs));

        var csv = MetricsCsvBuilder.Build(new MetricsCsvData
        {
            RangeLabel = rangeOption.Label,
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

    // The full transactions list: a paged, newest-first table of every recorded queue metric event
    // (timestamp, reference, stage, queue, decision, latency). The "Recent transitions" panel on
    // the dashboard caps at ~10 and links here for the complete history. Paging is by the
    // Wiki:PageLength setting and done in SQL (Skip/Take + COUNT) — the whole table is never loaded
    // into memory. An optional from/to window narrows the list. Role-gated cypmd_admin.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/transactions")]
    public async Task<IActionResult> Transactions(
        int page = 1,
        DateTime? from = null,
        DateTime? to = null,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;

        var pageSize = await ResolvePageSizeAsync();

        var fromUtc = from is null ? (DateTime?)null : AsUtc(from.Value);
        var toUtc = to is null ? (DateTime?)null : AsUtc(to.Value);

        // A blank search box is no filter; trim it so trailing whitespace from a paste does not
        // produce an always-empty result. The filter is carried back onto the model so the search
        // box stays populated and the pager links preserve it across pages.
        var referenceFilter = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();

        var result = await _query.GetTransactionsAsync(
            page, pageSize, fromUtc, toUtc, referenceFilter, cancellationToken);

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
        });
    }

    // The interactive-replay submissions picker: a paged, newest-first list of distinct references
    // that entered the pipeline, each with a checkbox and a Play button. A date/time filter narrows
    // the list; with no filter the picker opens on a recent window (the last DefaultWindow) so it
    // shows the latest submissions rather than every reference ever recorded. Paged by the
    // Wiki:PageLength setting (default 20), in SQL. Role-gated cypmd_admin.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/submissions")]
    public async Task<IActionResult> Submissions(
        int page = 1,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;

        var pageSize = await ResolvePageSizeAsync();

        // Default to a recent window so the picker opens on the latest submissions; an explicit
        // from/to filter overrides it. The default has no upper bound (open 'to') so very recent
        // events still appear.
        var now = DateTime.UtcNow;
        var fromUtc = from is not null ? AsUtc(from.Value)
            : to is null ? now - DefaultWindow : (DateTime?)null;
        var toUtc = to is null ? (DateTime?)null : AsUtc(to.Value);

        var result = await _query.GetSubmissionsAsync(page, pageSize, fromUtc, toUtc, cancellationToken);

        return View(new SubmissionsViewModel
        {
            Rows = result.Rows,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = TotalPages(result.TotalCount, result.PageSize),
            FromUtc = fromUtc,
            ToUtc = toUtc,
        });
    }

    // The followed-replay walkthrough: builds a per-reference stage progression from each selected
    // reference's real recorded events (reusing the journey read), so the page can show the same
    // chosen items moving across the five pipeline stages and single-step them. The progression is
    // the ordered, de-duplicated board-stage keys the reference actually visited. Role-gated
    // cypmd_admin.
    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/replay/walkthrough")]
    public async Task<IActionResult> Walkthrough(
        [FromQuery(Name = "reference")] string[]? reference = null,
        CancellationToken cancellationToken = default)
    {
        var references = (reference ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var items = new List<WalkthroughItem>();
        foreach (var refNumber in references)
        {
            var journey = await _query.GetJourneyAsync(refNumber, cancellationToken);
            if (journey.Count == 0)
                continue;

            // Map each event to its board stage key, in recorded order, collapsing consecutive
            // repeats so the cohort steps one node at a time along the path it really took.
            var stageKeys = new List<string>();
            foreach (var ev in journey.OrderBy(e => e.RecordedAtUtc))
            {
                var key = PipelineStages.KeyForStage(ev.Stage);
                if (stageKeys.Count == 0 || stageKeys[^1] != key)
                    stageKeys.Add(key);
            }

            var latestDecision = journey
                .Where(e => !string.IsNullOrEmpty(e.DecisionStatus))
                .Select(e => e.DecisionStatus)
                .LastOrDefault();

            items.Add(new WalkthroughItem
            {
                ReferenceNumber = refNumber,
                StageKeys = stageKeys,
                LatestDecision = latestDecision,
            });
        }

        return View(new WalkthroughViewModel { Items = items });
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
        var configured = HttpContext.RequestServices
            .GetService(typeof(IConfiguration)) as IConfiguration;
        var seconds = configured?.GetValue("Observability:HeartbeatSeconds", MaxHeartbeatSeconds)
            ?? MaxHeartbeatSeconds;
        if (seconds is <= 0 or > MaxHeartbeatSeconds)
            seconds = MaxHeartbeatSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    // The rows-per-page for the paged admin lists, read from the generic Wiki:PageLength setting
    // (the same setting the deleted-pages list and search use) so all paged lists share one knob.
    // Falls back to 20 when no settings service is wired (bare unit construction) or the stored
    // value is non-positive.
    private const int DefaultPageLength = 20;

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
            DlqRateRed: await _settings.GetIntAsync(SettingKeys.HealthDlqRateRed));
    }

    private static readonly HealthThresholds DefaultThresholds =
        new(DepthAmber: 25, DepthRed: 100, OldestAgeAmberSeconds: 120, OldestAgeRedSeconds: 600, DlqRateRed: 5);
}
