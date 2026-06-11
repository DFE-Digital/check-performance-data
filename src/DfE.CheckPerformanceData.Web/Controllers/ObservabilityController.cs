using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The always-on observability dashboard: a server-rendered health strip, plain-English status
// sentence, big-number tiles and accessible SVG charts, plus the per-message journey timeline
// and the live SSE snapshot stream that refreshes the board. Every action is role-gated
// cypmd_admin — the stream included, so there is never an unauthenticated firehose. The
// throughput action validates its granularity against the server-side allow-list before any
// aggregation runs.
public sealed class ObservabilityController : Controller
{
    private readonly IMetricsQueryService _query;
    private readonly IQueueAdminService _queueAdmin;
    private readonly IHealthEvaluator _health;
    private readonly StatusSentenceBuilder _sentence;
    private readonly ISettingService? _settings;

    public ObservabilityController(
        IMetricsQueryService query,
        IQueueAdminService queueAdmin,
        IHealthEvaluator health,
        StatusSentenceBuilder sentence,
        ISettingService? settings = null)
    {
        _query = query;
        _queueAdmin = queueAdmin;
        _health = health;
        _sentence = sentence;
        _settings = settings;
    }

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
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var from = now - DefaultWindow;

        var thresholds = await ResolveThresholdsAsync();

        var depths = await _queueAdmin.GetQueueDepthsAsync(cancellationToken);
        var dlqCount = await _queueAdmin.GetDlqCountAsync(cancellationToken);

        var queueHealth = new List<QueueHealth>();
        foreach (var (queueName, displayName) in Queues)
        {
            var depth = depths.FirstOrDefault(d => d.QueueName == queueName);
            var inputs = new HealthInputs(depth?.Depth ?? 0, depth?.OldestMessageAge, dlqCount);
            queueHealth.Add(new QueueHealth(queueName, displayName, _health.Evaluate(inputs, thresholds)));
        }

        // The overall light is the worst of the per-queue states so the at-a-glance answer never
        // looks healthier than its unhappiest queue.
        var overall = queueHealth
            .Select(q => q.State)
            .OrderByDescending(s => (int)s.Level)
            .FirstOrDefault()
            ?? _health.Evaluate(new HealthInputs(0, null, dlqCount), thresholds);

        var throughput = await _query.GetThroughputAsync(
            QueueOptions.ZendeskQueue, ThroughputGranularity.Hour, from, now, cancellationToken);
        var decisionMix = await _query.GetDecisionMixAsync(from, now, cancellationToken);
        var dwell = await _query.GetDwellByStageAsync(from, now, cancellationToken);
        var markers = await _query.GetDeployMarkersAsync(from, now, cancellationToken);

        var processedToday = throughput.Sum(b => b.Count);
        var typicalEndToEnd = dwell.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(dwell.Sum(d => d.AverageLatencyMs));

        var sentence = _sentence.Build(overall.Level, processedToday, typicalEndToEnd);

        var model = new DashboardViewModel
        {
            QueueHealth = queueHealth,
            OverallHealth = overall,
            StatusSentence = sentence,
            ProcessedToday = processedToday,
            TypicalEndToEnd = typicalEndToEnd,
            Depths = depths
                .Select(d => new QueueDepthSnapshot(d.QueueName, d.Depth, d.OldestMessageAge))
                .ToList(),
            Throughput = throughput,
            DecisionMix = decisionMix,
            Dwell = dwell,
            DeployMarkers = markers,
            RefreshedAtUtc = now,
        };

        return View(model);
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/observability/throughput")]
    public async Task<IActionResult> Throughput(
        string granularity,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        // The granularity must be one of the allow-list values; anything else is rejected before
        // any aggregation runs, so a hostile string never reaches the date_trunc mapping.
        if (!Enum.TryParse<ThroughputGranularity>(granularity, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return BadRequest($"Unsupported granularity '{granularity}'.");
        }

        var toUtc = AsUtc(to ?? DateTime.UtcNow);
        var fromUtc = from is null ? toUtc - DefaultWindow : AsUtc(from.Value);

        try
        {
            var buckets = await _query.GetThroughputAsync(
                QueueOptions.ZendeskQueue, parsed, fromUtc, toUtc, cancellationToken);
            return Json(buckets);
        }
        catch (ArgumentException ex)
        {
            // An over-wide range or an unknown granularity that slipped through is a client error.
            return BadRequest(ex.Message);
        }
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
        var configured = HttpContext?.RequestServices
            .GetService(typeof(IConfiguration)) as IConfiguration;
        var seconds = configured?.GetValue("Observability:HeartbeatSeconds", MaxHeartbeatSeconds)
            ?? MaxHeartbeatSeconds;
        if (seconds <= 0 || seconds > MaxHeartbeatSeconds)
            seconds = MaxHeartbeatSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

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
