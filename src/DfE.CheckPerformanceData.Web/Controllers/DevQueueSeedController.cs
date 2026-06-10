using DfE.CheckPerformanceData.Application.Queue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only endpoint that seeds a single dead-lettered message so the queue admin surface
// has something to act on during manual testing and E2E runs. Returns 404 in Production —
// belt-and-braces alongside the environment guard — and never reaches a real queue there.
// [AllowAnonymous] mirrors the dev impersonation controller: callers reach it before they
// hold any auth cookie, and it only manipulates the local dev database.
[AllowAnonymous]
public sealed class DevQueueSeedController(IHostEnvironment env, IQueueService queueService) : Controller
{
    private bool IsAllowed => !env.IsProduction();

    [HttpGet("dev/queues/seed-dlq")]
    [HttpPost("dev/queues/seed-dlq")]
    public async Task<IActionResult> SeedDeadLetter(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var reference = $"e2e-dlq-{Guid.NewGuid():N}";
        await queueService.EnqueueAsync(
            QueueOptions.RulesEngineQueue,
            new { Reference = reference },
            cancellationToken);

        var taken = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue, cancellationToken);
        if (taken is null)
            return StatusCode(500, "Could not dequeue the seeded message.");

        await queueService.DeadLetterAsync(taken.Id, "Seeded for admin testing.", cancellationToken);

        return Json(new { id = taken.Id, reference });
    }
}
