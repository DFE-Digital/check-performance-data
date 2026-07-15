using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Models.Dev;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only trigger that injects a synthetic request into the rules-engine pipeline: it
// creates a ChangeRequest row and enqueues a RequestDocument the rules consumer can parse
// and evaluate. Paired with the dev Zendesk outbox viewer, it lets the whole pipeline be
// driven and observed locally with no real Zendesk. Returns 404 in Production — a real
// environment guard alongside the Dev:ToolsEnabled flag, so a production deploy that leaves
// the flag on still never exposes the surface — and never reaches a real queue there.
// [AllowAnonymous] mirrors the dev impersonation/queue-seed controllers: callers reach it
// before they hold any auth cookie, and it only manipulates the local dev database.
[AllowAnonymous]
public sealed class DevPipelineController(
    IConfiguration configuration,
    IPortalDbContext dbContext,
    IQueueService queueService,
    IHostEnvironment? hostEnvironment = null,
    SubmittedMetricRecorder? submittedMetrics = null) : Controller
{
    // The surface is gated on the config flag AND a hard production guard: even if a
    // production deploy leaves Dev:ToolsEnabled true, IsProduction short-circuits to 404.
    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && hostEnvironment?.IsProduction() != true;

    // The Zendesk-styled preview additionally requires the fake Zendesk path to be active, so a
    // captured outbox row is only ever rendered while no real Zendesk push is happening.
    private bool IsFakeZendesk => configuration.GetValue<bool>(SettingKeys.ZendeskUseFake);

    [HttpGet("dev/queues/submit-request")]
    [HttpPost("dev/queues/submit-request")]
    public async Task<IActionResult> SubmitRequest(
        string? outcome,
        Guid? windowId,
        long? urn,
        CancellationToken cancellationToken,
        Guid? pupilId = null,
        string? pupilUpn = null,
        string? pupilFirstName = null,
        string? pupilSurname = null,
        string? requestType = null)
    {
        if (!IsAllowed)
            return NotFound();

        var runner = new DevPipelineRunner(dbContext, queueService, submittedMetrics);
        var result = await runner.SubmitAsync(outcome, windowId, urn, cancellationToken, pupilId, pupilUpn, pupilFirstName, pupilSurname, requestType);

        return Json(new
        {
            referenceNumber = result.Reference,
            outcome = result.PresetName,
            expectedDecision = result.ExpectedDecision,
            message = $"Submitted request {result.Reference} (preset '{result.PresetName}', expecting {result.ExpectedDecision}). " +
                      "Watch the worker process it and view /dev/zendesk/outbox for the captured ticket.",
        });
    }

    [HttpGet("dev/zendesk/outbox")]
    public async Task<IActionResult> Outbox(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var rows = await dbContext.DevZendeskTickets
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return View("Outbox", rows);
    }

    // Renders one captured outbox row as a faithful Zendesk-styled simulation (subject,
    // requester, priority/status badges, body, custom fields, tags, attachments) rather than
    // raw JSON — a "what we'll send" artefact until real Zendesk is wired. Gated on
    // Dev:ToolsEnabled AND Zendesk:UseFake so it is only reachable when the pipeline is faking
    // Zendesk; it reaches no real Zendesk instance.
    [HttpGet("dev/zendesk/preview/{id:guid}")]
    public async Task<IActionResult> ZendeskPreview(Guid id, CancellationToken cancellationToken)
    {
        if (!IsAllowed || !IsFakeZendesk)
            return NotFound();

        var ticket = await dbContext.DevZendeskTickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
            return NotFound();

        return View("~/Views/Dev/ZendeskTicketPreview.cshtml", ZendeskTicketPreviewViewModel.FromTicket(ticket));
    }
}
