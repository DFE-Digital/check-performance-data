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
    private readonly PayloadRedactor _redactor;

    public ObservabilityController(
        IMetricsQueryService query,
        IQueueAdminService queueAdmin,
        IHealthEvaluator health,
        StatusSentenceBuilder sentence,
        ISettingService? settings = null,
        PayloadRedactor? redactor = null)
    {
        _query = query;
        _queueAdmin = queueAdmin;
        _health = health;
        _sentence = sentence;
        _settings = settings;
        _redactor = redactor ?? new PayloadRedactor();
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

        var toUtc = to ?? DateTime.UtcNow;
        var fromUtc = from ?? toUtc - DefaultWindow;

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

    // The click-to-inspect panel behind a board token. The journey (decision + per-stage queue
    // status) is the always-available view; the message payload is only reachable while the
    // message is still pending on a working queue (ack deletes the row), and is redacted by
    // default — the full payload is shown only when the audited full-payload setting is on,
    // mirroring the working-message detail discipline so this surface never leaks pupil data.
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

        var payload = string.Empty;
        var redacted = false;
        var payloadAvailable = false;

        // A live payload is only reachable while the message is still pending on a working queue.
        // The board token carries the queue-row id when the row is still present; if it parses we
        // look the detail up and redaction-gate it, otherwise only the journey is shown.
        if (Guid.TryParse(reference, out var messageId))
        {
            foreach (var queueName in Queues.Keys)
            {
                var message = await _queueAdmin.GetMessageDetailAsync(queueName, messageId, cancellationToken);
                if (message is null)
                    continue;

                payloadAvailable = true;
                if (await IsFullPayloadEnabledAsync())
                {
                    payload = message.Payload;
                    redacted = false;
                }
                else
                {
                    payload = _redactor.Redact(message.Payload);
                    redacted = true;
                }

                break;
            }
        }

        return View(new InspectViewModel
        {
            ReferenceNumber = reference,
            Decision = decision,
            Stages = stages,
            Payload = payload,
            IsRedacted = redacted,
            PayloadAvailable = payloadAvailable,
        });
    }

    private async Task<bool> IsFullPayloadEnabledAsync()
    {
        if (_settings is null)
            return false;

        var value = await _settings.GetValueAsync(SettingKeys.DlqFullPayloadEnabled);
        return bool.TryParse(value, out var enabled) && enabled;
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
