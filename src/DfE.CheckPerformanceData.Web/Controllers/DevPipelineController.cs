using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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
    IPupilDataBlobClient pupilBlob,
    IHostEnvironment? hostEnvironment = null,
    SubmittedMetricRecorder? submittedMetrics = null) : Controller
{
    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && hostEnvironment?.IsProduction() != true;

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
        string? requestType = null,
        string? laestab = null,
        string? userEmail = null,
        Guid? userId = null)
    {
        if (!IsAllowed)
            return NotFound();

        var runner = new DevPipelineRunner(dbContext, queueService, pupilBlob, submittedMetrics);
        var result = await runner.SubmitAsync(outcome, windowId, urn, cancellationToken, pupilId, pupilUpn, pupilFirstName, pupilSurname, requestType, laestab, userEmail, userId);

        return Json(new
        {
            referenceNumber = result.Reference,
            outcome = result.PresetName,
            expectedDecision = result.ExpectedDecision,
            message = $"Submitted request {result.Reference} (preset '{result.PresetName}', expecting {result.ExpectedDecision}). " +
                      "Watch the worker process it and view /dev/zendesk/outbox for the captured ticket.",
        });
    }

    [HttpPost("dev/queues/cleanup-e2e-requests")]
    public async Task<IActionResult> CleanupE2eRequests(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        // EF Core/Npgsql cannot translate StartsWith in either SELECT or ExecuteDelete.
        // Use EF.Functions.Like which maps to PostgreSQL's LIKE operator, then delete
        // by the fetched IDs using Contains which EF Core can translate as IN (...).
        var devIds = await dbContext.ChangeRequests
            .Where(r => EF.Functions.Like(r.ReferenceNumber, "DEV-%"))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var deleted = devIds.Count;
        if (devIds.Count > 0)
        {
            deleted = await dbContext.ChangeRequests
                .Where(r => devIds.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return Json(new { deleted });
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
