using System.Diagnostics;
using System.Globalization;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The debug pipeline drive/inject/seed endpoints: dev/test-only POST actions that let a human
// drive synthetic traffic through the pipeline and rehearse failure/recovery. The standalone
// /dev/uat GET console was retired and its controls folded into the Pipeline dashboard's
// collapsible Demo panel, which posts to these endpoints over AJAX. They are gated exactly like
// the other /dev/* surfaces (Dev:ToolsEnabled AND a hard production guard) and add no new pipeline
// machinery: the drive buttons reuse DevPipelineRunner and the failure buttons reuse the existing
// seed/dead-letter path. The AJAX path returns JSON so the board refreshes in place; the no-JS
// fallback redirects to the dashboard. [AllowAnonymous] mirrors the sibling dev controllers: the
// surface is reached before any auth cookie and only manipulates the local dev database.
[AllowAnonymous]
public sealed class DevUatController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IQueueService _queueService;
    private readonly DevPipelineRunner _runner;
    private readonly IHostEnvironment? _hostEnvironment;
    private readonly IMetricsSink? _metricsSink;
    private readonly IDemoTrafficPurger? _demoPurger;
    private readonly IMetricsQueryService? _metricsQuery;

    public DevUatController(
        IConfiguration configuration,
        IQueueService queueService,
        DevPipelineRunner runner,
        IHostEnvironment? hostEnvironment = null,
        IMetricsSink? metricsSink = null,
        IDemoTrafficPurger? demoPurger = null,
        IMetricsQueryService? metricsQuery = null)
    {
        _configuration = configuration;
        _queueService = queueService;
        _runner = runner;
        _hostEnvironment = hostEnvironment;
        _metricsSink = metricsSink;
        _demoPurger = demoPurger;
        _metricsQuery = metricsQuery;
    }

    // The "seed messages" spread: a couple of months of synthetic history, 10–100 submissions a day,
    // so a fresh dev environment's charts look full. Cumulative — each click adds another batch.
    // Seed-messages defaults + caps: the "last couple months" default window and a sensible default
    // density, with a hard per-day cap so a hand-posted value can't fan out unbounded.
    private const int SeedDefaultDays = 60;
    private const int SeedDefaultPerDay = 50;
    private const int SeedMaxPerDayCap = 500;

    // Where a no-JS form post lands after a drive/inject/seed: the Pipeline dashboard, whose Demo
    // panel now hosts these controls (the standalone console it used to return to is gone). The
    // AJAX path returns JSON instead and never redirects.
    private const string DashboardUrl = "/admin/observability";

    // Whether the request came from the Demo panel's fetch() layer (uat-console.js). When true the
    // drive/inject/seed actions return a small JSON result so the page can refresh the board in
    // place; when false (no-JS) they fall back to the form-post + redirect, preserving progressive
    // enhancement.
    private bool IsAjax =>
        Request.Headers["X-Requested-With"] == "XMLHttpRequest"
        || Request.Headers.Accept.Any(h => h is not null && h.Contains("application/json"));

    // The reference of the most recently driven request, remembered per-instance for the runner's
    // "open journey for last reference" shortcut. Static so it survives across requests within the
    // process; this is a single-user dev aid, never a production surface, so a shared field is fine.
    private static string? _lastReference;

    // Gated on the config flag AND a hard production guard: even if a production deploy leaves
    // Dev:ToolsEnabled true, IsProduction short-circuits to 404.
    private bool IsAllowed =>
        _configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && _hostEnvironment?.IsProduction() != true;

    // Drive a small batch of synthetic requests for the chosen preset through the shared runner.
    // Count defaults to one and is clamped to a sane upper bound so a stray query value cannot
    // flood the local queue. Remembers the last reference for the journey shortcut, then redirects
    // back to the dashboard (no-JS) or returns JSON (AJAX).
    [HttpPost("dev/uat/drive")]
    public async Task<IActionResult> Drive(string? outcome, int count, CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var batch = Math.Clamp(count <= 0 ? 1 : count, 1, 20);

        string? lastReference = null;
        string? lastOutcome = null;
        for (var i = 0; i < batch; i++)
        {
            // "random" rolls one of the four outcomes per message (so a batch is a realistic mix);
            // a failure rolls down the dead-letter path, the rest through the rules engine.
            var chosen = ResolveDriveOutcome(outcome);
            if (chosen == FailureOutcome)
            {
                var reference = $"demo-fail-{Guid.NewGuid():N}"[..20];
                await SeedFailedMessageAsync(
                    reference, "Synthetic failing message driven for the demo.", cancellationToken);
                lastReference = reference;
            }
            else
            {
                var result = await _runner.SubmitAsync(chosen, cancellationToken);
                lastReference = result.Reference;
            }

            lastOutcome = chosen;
        }

        if (lastReference is not null)
            _lastReference = lastReference;

        if (IsAjax)
            return Json(new { ok = true, reference = lastReference, outcome = lastOutcome });

        return Redirect(DashboardUrl);
    }

    // The board's failure token value (matches observability-board.js presentDrive()).
    private const string FailureOutcome = "failed";

    // The four outcomes the "Random" drive can produce.
    public static readonly string[] RandomOutcomes = { "approved", "rejected", "scrutiny", FailureOutcome };

    // Resolves the drive outcome: a literal preset name passes through; "random" rolls one of the
    // four. Pure and public so the roll's value-set is unit-tested without the HTTP action.
    public static string ResolveDriveOutcome(string? outcome, Random? random = null) =>
        string.Equals(outcome, "random", StringComparison.OrdinalIgnoreCase)
            ? RandomOutcomes[(random ?? Random.Shared).Next(RandomOutcomes.Length)]
            : (outcome ?? "approved");

    // The failure-and-recovery demo. Composes the existing seed/dead-letter path (one synthetic
    // failing message) rather than adding any new failure machinery, then redirects back so the
    // watcher sees it fail on the board and can redrive it from the DLQ.
    [HttpPost("dev/uat/inject-failure")]
    public async Task<IActionResult> InjectFailure(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var reference = $"demo-fail-{Guid.NewGuid():N}"[..20];
        await SeedFailedMessageAsync(
            reference,
            "Synthetic failing message injected for the failure-and-recovery demonstration.",
            cancellationToken);

        if (IsAjax)
            return Json(new { ok = true, reference });

        return Redirect(DashboardUrl);
    }

    [HttpPost("dev/uat/seed-dlq")]
    public async Task<IActionResult> SeedDlq(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var reference = $"uat-dlq-{Guid.NewGuid():N}"[..16];
        await SeedFailedMessageAsync(
            reference, "Seeded from the UAT console for admin testing.", cancellationToken);

        if (IsAjax)
            return Json(new { ok = true, reference });

        return Redirect(DashboardUrl);
    }

    // Seed synthetic pipeline history so a fresh dev environment's charts and counters look full
    // without driving thousands of requests by hand. The tester chooses the time frame — a named
    // range from the same list the charts offer, or a custom from/to — and how many submissions per
    // day; it defaults to the last couple of months. Cumulative: each post adds another batch.
    // Writes only synthetic metric events to the dev database via the bulk sink; gated exactly like
    // the other /dev/* tooling.
    [HttpPost("dev/uat/seed-messages")]
    public async Task<IActionResult> SeedMessages(
        string? range = null,
        string? from = null,
        string? to = null,
        int perDay = SeedDefaultPerDay,
        CancellationToken cancellationToken = default)
    {
        if (!IsAllowed || _metricsSink is null)
            return NotFound();

        var now = DateTime.UtcNow;

        // A named range reuses the dashboard's calendar/rolling windows for continuity; otherwise the
        // custom from/to apply (to defaults to now, from to the "last couple months").
        DateTime fromUtc, toUtc;
        if (!string.IsNullOrEmpty(range)
            && !string.Equals(range, DashboardRanges.CustomValue, StringComparison.OrdinalIgnoreCase))
        {
            var resolved = DashboardRanges.ResolveWindow(range, null, null, now);
            (fromUtc, toUtc) = (resolved.From, resolved.To);
        }
        else
        {
            toUtc = ParseUtc(to) ?? now;
            fromUtc = ParseUtc(from) ?? toUtc.AddDays(-SeedDefaultDays);
        }

        // Guard an inverted/empty window and clamp the span, mirroring the dashboard's bounds.
        if (toUtc <= fromUtc) fromUtc = toUtc.AddDays(-SeedDefaultDays);
        if (toUtc - fromUtc > DashboardRanges.MaxCustomSpan) fromUtc = toUtc - DashboardRanges.MaxCustomSpan;

        var perDayClamped = Math.Clamp(perDay, 1, SeedMaxPerDayCap);

        var events = PipelineMetricsSeeder.Generate(fromUtc, toUtc, perDayClamped, new Random());

        await _metricsSink.RecordManyAsync(events, cancellationToken);

        if (IsAjax)
            return Json(new { ok = true, count = events.Count });

        return Redirect(DashboardUrl);
    }

    // One level of the self load-test: drive a burst of `rate` random-outcome messages, then wait
    // (polling the metrics for exactly those references) until the whole batch reaches a terminal
    // stage or the timeout elapses — the rules-engine worker draining the queue is what is being
    // measured. Returns the wall-clock throughput (completed / elapsed) and the per-step averages for
    // the batch. The client escalates the rate and finds the knee. Dev-gated; JSON only.
    private const int LoadMaxRate = 200;
    private const int LoadDefaultTimeoutMs = 30000;
    private const int LoadPollMs = 400;

    [HttpPost("dev/uat/load-test")]
    public async Task<IActionResult> LoadTest(int rate, int? timeoutMs, CancellationToken cancellationToken)
    {
        if (!IsAllowed || _metricsQuery is null)
            return NotFound();

        var batch = Math.Clamp(rate <= 0 ? 1 : rate, 1, LoadMaxRate);
        var timeout = Math.Clamp(timeoutMs ?? LoadDefaultTimeoutMs, 1000, 60000);

        var started = Stopwatch.GetTimestamp();

        // Drive the batch and remember exactly which references it produced.
        var references = new List<string>(batch);
        for (var i = 0; i < batch; i++)
        {
            var chosen = ResolveDriveOutcome("random");
            if (chosen == FailureOutcome)
            {
                var reference = $"demo-fail-{Guid.NewGuid():N}"[..20];
                await SeedFailedMessageAsync(reference, "Load-test failing message.", cancellationToken);
                references.Add(reference);
            }
            else
            {
                var result = await _runner.SubmitAsync(chosen, cancellationToken);
                references.Add(result.Reference);
            }
        }

        // Poll until the whole batch completes or the timeout elapses.
        var sample = new LoadSample(0, new StageAverages(null, null, null, null));
        var deadlineTicks = Stopwatch.GetTimestamp() + (long)(timeout / 1000.0 * Stopwatch.Frequency);
        while (true)
        {
            sample = await _metricsQuery.GetLoadSampleAsync(references, cancellationToken);
            if (sample.Completed >= batch)
                break;
            if (Stopwatch.GetTimestamp() >= deadlineTicks)
                break;
            await Task.Delay(LoadPollMs, cancellationToken);
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        var throughput = elapsedMs > 0 ? sample.Completed / (elapsedMs / 1000.0) : 0;

        return Json(new
        {
            ok = true,
            rate = batch,
            driven = batch,
            completed = sample.Completed,
            elapsedMs = Math.Round(elapsedMs),
            throughputPerSec = Math.Round(throughput, 2),
            rulesQueueMs = sample.Averages.RulesQueueMs,
            rulesEngineMs = sample.Averages.RulesEngineMs,
            zendeskQueueMs = sample.Averages.ZendeskQueueMs,
            ticketMs = sample.Averages.TicketMs,
            timedOut = sample.Completed < batch,
        });
    }

    // The load-test results exported as an .xlsx (a throughput-by-rate chart and a step-times chart).
    // The client posts the rows it collected; we render the workbook. Dev-gated like the rest.
    public sealed record LoadTestExportRow(
        int Rate, int Completed, double ThroughputPerSec, double ElapsedMs,
        double? RulesQueueMs, double? RulesEngineMs, double? ZendeskQueueMs, double? TicketMs);

    public sealed record LoadTestExportRequest(IReadOnlyList<LoadTestExportRow>? Rows);

    [HttpPost("dev/uat/load-test/export.xlsx")]
    public IActionResult LoadTestExport([FromBody] LoadTestExportRequest? request)
    {
        if (!IsAllowed)
            return NotFound();

        var rows = (request?.Rows ?? Array.Empty<LoadTestExportRow>())
            .Select(r => new LoadTestRow
            {
                Rate = r.Rate,
                Completed = r.Completed,
                ThroughputPerSec = r.ThroughputPerSec,
                ElapsedMs = r.ElapsedMs,
                RulesQueueMs = r.RulesQueueMs,
                RulesEngineMs = r.RulesEngineMs,
                ZendeskQueueMs = r.ZendeskQueueMs,
                TicketMs = r.TicketMs,
            })
            .ToList();

        var bytes = LoadTestWorkbookBuilder.Build(rows);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "load-test.xlsx");
    }

    // Parses a datetime-local / ISO from-to value to UTC; DateTime does not bind from these query
    // values, so the seed form posts strings and we parse them here (mirrors the dashboard).
    private static DateTime? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }

    // Remove all synthetic demo traffic (drive / seed / inject) from the pipeline tables while
    // keeping real submissions, matched by the well-known demo reference prefixes. Lets a tester
    // reset a demo'd dev environment back to just its real data. Gated like the other dev tooling.
    [HttpPost("dev/uat/purge-demo")]
    public async Task<IActionResult> PurgeDemo(CancellationToken cancellationToken)
    {
        if (!IsAllowed || _demoPurger is null)
            return NotFound();

        var result = await _demoPurger.PurgeAsync(cancellationToken);

        if (IsAjax)
            return Json(new { ok = true, removed = result.Total });

        return Redirect(DashboardUrl);
    }

    // The "open journey for the last driven reference" shortcut: redirect to the always-on journey
    // timeline for the most recent drive, or back to the console if nothing has been driven yet.
    [HttpGet("dev/uat/last-journey")]
    public IActionResult LastJourney()
    {
        if (!IsAllowed)
            return NotFound();

        if (string.IsNullOrEmpty(_lastReference))
            return Redirect(DashboardUrl);

        return Redirect($"/admin/observability/journey/{_lastReference}");
    }

    // Shared seed: enqueue a synthetic message and dead-letter it by its own id, mirroring the
    // existing DevQueueSeedController path. Dead-lettering by id (not by dequeue) matters: dequeuing
    // would claim the OLDEST visible message, dead-lettering a legitimate one with the worker running.
    private async Task SeedFailedMessageAsync(string reference, string reason, CancellationToken cancellationToken)
    {
        var id = await _queueService.EnqueueAsync(
            QueueOptions.RulesEngineQueue, new { Reference = reference }, cancellationToken);
        await _queueService.DeadLetterAsync(id, reason, cancellationToken);
    }
}
