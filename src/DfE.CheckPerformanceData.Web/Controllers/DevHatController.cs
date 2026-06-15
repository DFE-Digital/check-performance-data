using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Models.Dev;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The Debug HAT test console: one dev/test-only page that lets a human exercise every
// human-checkable item from the 03.10 / 03.13 acceptance inventory with one click, tick each
// pass/fail, and see the automated-only items as live coverage status. It is gated exactly like
// the other /dev/* surfaces (Dev:ToolsEnabled AND a hard production guard) and adds no new
// pipeline machinery: the drive buttons reuse DevPipelineRunner and the failure buttons reuse the
// existing seed/dead-letter path. Every POST redirects back to the console so the watcher stays
// on the page and sees the board react. HAT verdicts are never stored server-side — they persist
// client-side in localStorage. [AllowAnonymous] mirrors the sibling dev controllers: the surface
// is reached before any auth cookie and only manipulates the local dev database.
[AllowAnonymous]
public sealed class DevHatController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IQueueService _queueService;
    private readonly DevPipelineRunner _runner;
    private readonly IHostEnvironment? _hostEnvironment;

    public DevHatController(
        IConfiguration configuration,
        IQueueService queueService,
        DevPipelineRunner runner,
        IHostEnvironment? hostEnvironment = null)
    {
        _configuration = configuration;
        _queueService = queueService;
        _runner = runner;
        _hostEnvironment = hostEnvironment;
    }

    private const string ConsoleUrl = "/dev/hat";

    // The reference of the most recently driven request, remembered per-instance for the runner's
    // "open journey for last reference" shortcut. Static so it survives across requests within the
    // process; this is a single-user dev aid, never a production surface, so a shared field is fine.
    private static string? _lastReference;

    // Gated on the config flag AND a hard production guard: even if a production deploy leaves
    // Dev:ToolsEnabled true, IsProduction short-circuits to 404.
    private bool IsAllowed =>
        _configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && _hostEnvironment?.IsProduction() != true;

    [HttpGet("dev/hat")]
    public IActionResult Index()
    {
        if (!IsAllowed)
            return NotFound();

        var model = new HatConsoleViewModel
        {
            Interactive = HatCatalog.Interactive,
            AutomatedCoverageIds = HatCatalog.AutomatedCoverageIds,
            DevToolsEnabled = _configuration.GetValue<bool>(SettingKeys.DevToolsEnabled),
            FakeZendesk = _configuration.GetValue<bool>(SettingKeys.ZendeskUseFake),
            LastReference = _lastReference,
        };

        return View(model);
    }

    // Drive a small batch of synthetic requests for the chosen preset through the shared runner.
    // Count defaults to one and is clamped to a sane upper bound so a stray query value cannot
    // flood the local queue. Remembers the last reference for the journey shortcut, then redirects
    // back to the console.
    [HttpPost("dev/hat/drive")]
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

        return Redirect(ConsoleUrl);
    }

    // The failure-and-recovery demo. Composes the existing seed/dead-letter path (one synthetic
    // failing message) rather than adding any new failure machinery, then redirects back so the
    // watcher sees it fail on the board and can redrive it from the DLQ.
    [HttpPost("dev/hat/inject-failure")]
    public async Task<IActionResult> InjectFailure(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        await SeedFailedMessageAsync(
            $"demo-fail-{Guid.NewGuid():N}"[..20],
            "Synthetic failing message injected for the failure-and-recovery demonstration.",
            cancellationToken);

        return Redirect(ConsoleUrl);
    }

    [HttpPost("dev/hat/seed-dlq")]
    public async Task<IActionResult> SeedDlq(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        await SeedFailedMessageAsync(
            $"hat-dlq-{Guid.NewGuid():N}"[..16], "Seeded from the HAT console for admin testing.", cancellationToken);

        return Redirect(ConsoleUrl);
    }

    // The "open journey for the last driven reference" shortcut: redirect to the always-on journey
    // timeline for the most recent drive, or back to the console if nothing has been driven yet.
    [HttpGet("dev/hat/last-journey")]
    public IActionResult LastJourney()
    {
        if (!IsAllowed)
            return NotFound();

        if (string.IsNullOrEmpty(_lastReference))
            return Redirect(ConsoleUrl);

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
