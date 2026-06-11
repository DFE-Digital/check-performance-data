using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only endpoint that seeds a single dead-lettered message so the queue admin surface
// has something to act on during manual testing and E2E runs. Returns 404 in Production —
// a real environment guard alongside the Dev:ToolsEnabled flag, so a production deploy that
// leaves the flag on still never exposes the surface — and never reaches a real queue there.
// [AllowAnonymous] mirrors the dev impersonation controller: callers reach it before they
// hold any auth cookie, and it only manipulates the local dev database.
[AllowAnonymous]
public sealed class DevQueueSeedController(
    IConfiguration configuration,
    IQueueService queueService,
    IHostEnvironment? hostEnvironment = null) : Controller
{
    // The surface is gated on the config flag AND a hard production guard: even if a
    // production deploy leaves Dev:ToolsEnabled true, IsProduction short-circuits to 404.
    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && hostEnvironment?.IsProduction() != true;

    [HttpGet("dev/queues/seed-dlq")]
    [HttpPost("dev/queues/seed-dlq")]
    public async Task<IActionResult> SeedDeadLetter(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var (id, reference) = await SeedFailedMessageAsync(
            $"e2e-dlq-{Guid.NewGuid():N}", "Seeded for admin testing.", cancellationToken);

        return Json(new { id, reference });
    }

    // The failure-and-recovery demo. Composes the EXISTING seed path: it injects one synthetic
    // message keyed off an OutcomePreset and dead-letters it, so the watcher sees it fail on the
    // board, the health strip go amber and the dead-letter count tick up. Recovery is the
    // existing audited redrive on the queue admin surface — this adds no new failure machinery
    // and reaches no real Zendesk. Dev/test only (Dev:ToolsEnabled).
    [HttpPost("dev/queues/inject-failure")]
    public async Task<IActionResult> InjectFailureDemo(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        // Use a known preset so the synthetic failure looks like a real request shape, not an
        // arbitrary blob. The reference carries the preset name for legibility on the board.
        var preset = OutcomePresets.Resolve("scrutiny");
        var reference = $"demo-fail-{preset.Name}-{Guid.NewGuid():N}"[..24];

        var (id, _) = await SeedFailedMessageAsync(
            reference,
            "Synthetic failing message injected for the failure-and-recovery demonstration.",
            cancellationToken);

        return Json(new
        {
            id,
            reference,
            message = "Injected one synthetic failing message. Watch it fail on the board, then " +
                      "redrive it from the dead-letter queue to return to green.",
        });
    }

    // Shared seed: enqueue a synthetic message and dead-letter it by its own id. Both the admin
    // seed and the failure-recovery demo run through this one path so there is a single failure
    // mechanism. Dead-lettering by id matters: dequeuing here would claim the OLDEST visible
    // message on the queue, so with the worker running or real messages pending it would
    // dead-letter a legitimate message instead of the synthetic one.
    private async Task<(Guid Id, string Reference)> SeedFailedMessageAsync(
        string reference, string reason, CancellationToken cancellationToken)
    {
        var id = await queueService.EnqueueAsync(
            QueueOptions.RulesEngineQueue,
            new { Reference = reference },
            cancellationToken);

        await queueService.DeadLetterAsync(id, reason, cancellationToken);
        return (id, reference);
    }
}
