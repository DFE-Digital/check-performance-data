using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only endpoint that seeds a single dead-lettered message so the queue admin surface
// has something to act on during manual testing and E2E runs. Returns 404 in Production —
// belt-and-braces alongside the environment guard — and never reaches a real queue there.
// [AllowAnonymous] mirrors the dev impersonation controller: callers reach it before they
// hold any auth cookie, and it only manipulates the local dev database.
[AllowAnonymous]
public sealed class DevQueueSeedController(IConfiguration configuration, IQueueService queueService) : Controller
{
    private bool IsAllowed => configuration.GetValue<bool>(SettingKeys.DevToolsEnabled);

    [HttpGet("dev/queues/seed-dlq")]
    [HttpPost("dev/queues/seed-dlq")]
    public async Task<IActionResult> SeedDeadLetter(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var (id, reference) = await SeedFailedMessageAsync(
            $"e2e-dlq-{Guid.NewGuid():N}", "Seeded for admin testing.", cancellationToken);
        if (id is null)
            return StatusCode(500, "Could not dequeue the seeded message.");

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
        if (id is null)
            return StatusCode(500, "Could not dequeue the injected message.");

        return Json(new
        {
            id,
            reference,
            message = "Injected one synthetic failing message. Watch it fail on the board, then " +
                      "redrive it from the dead-letter queue to return to green.",
        });
    }

    // Shared seed: enqueue a synthetic message, dequeue it, and dead-letter it. Both the admin
    // seed and the failure-recovery demo run through this one path so there is a single failure
    // mechanism.
    private async Task<(Guid? Id, string Reference)> SeedFailedMessageAsync(
        string reference, string reason, CancellationToken cancellationToken)
    {
        await queueService.EnqueueAsync(
            QueueOptions.RulesEngineQueue,
            new { Reference = reference },
            cancellationToken);

        var taken = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue, cancellationToken);
        if (taken is null)
            return (null, reference);

        await queueService.DeadLetterAsync(taken.Id, reason, cancellationToken);
        return (taken.Id, reference);
    }
}
