using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
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

    public DevUatController(
        IConfiguration configuration,
        IQueueService queueService,
        DevPipelineRunner runner,
        IHostEnvironment? hostEnvironment = null,
        IMetricsSink? metricsSink = null,
        IDemoTrafficPurger? demoPurger = null)
    {
        _configuration = configuration;
        _queueService = queueService;
        _runner = runner;
        _hostEnvironment = hostEnvironment;
        _metricsSink = metricsSink;
        _demoPurger = demoPurger;
    }

    // The "seed messages" spread: a couple of months of synthetic history, 10–100 submissions a day,
    // so a fresh dev environment's charts look full. Cumulative — each click adds another batch.
    private const int SeedDays = 60;
    private const int SeedMinPerDay = 10;
    private const int SeedMaxPerDay = 100;

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
        for (var i = 0; i < batch; i++)
        {
            var result = await _runner.SubmitAsync(outcome, cancellationToken);
            lastReference = result.Reference;
        }

        if (lastReference is not null)
            _lastReference = lastReference;

        if (IsAjax)
            return Json(new { ok = true, reference = lastReference });

        return Redirect(DashboardUrl);
    }

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

    // Seed a couple of months of synthetic pipeline history so a fresh dev environment's charts and
    // counters look full without driving thousands of requests by hand. Cumulative: each click adds
    // another batch of 10–100 backdated submissions per day. Writes only synthetic metric events to
    // the dev database via the bulk sink; gated exactly like the other /dev/* tooling.
    [HttpPost("dev/uat/seed-messages")]
    public async Task<IActionResult> SeedMessages(CancellationToken cancellationToken)
    {
        if (!IsAllowed || _metricsSink is null)
            return NotFound();

        var events = PipelineMetricsSeeder.Generate(
            DateTime.UtcNow, SeedDays, SeedMinPerDay, SeedMaxPerDay, new Random());

        await _metricsSink.RecordManyAsync(events, cancellationToken);

        if (IsAjax)
            return Json(new { ok = true, count = events.Count });

        return Redirect(DashboardUrl);
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
