using System.Globalization;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
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

    // A stable dev checking window the synthetic requests hang off; upserted on demand so
    // the ChangeRequest foreign key is always satisfied without manual seeding.
    private static readonly Guid DevWindowId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    [HttpGet("dev/queues/submit-request")]
    [HttpPost("dev/queues/submit-request")]
    public async Task<IActionResult> SubmitRequest(string? outcome, CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            return NotFound();

        var preset = OutcomePresets.Resolve(outcome);
        var reference = $"DEV-{Guid.NewGuid():N}"[..16];

        await EnsureCheckingWindowAsync(cancellationToken);

        dbContext.ChangeRequests.Add(new ChangeRequest
        {
            Id = Guid.NewGuid(),
            WindowId = DevWindowId,
            OrganisationUrn = 123456,
            PupilUpn = "UPN1",
            PupilFirstname = "Bob",
            PupilSurname = "Smith",
            Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Dev Harness",
            Status = RequestStatus.SubmittedUnCommitted,
            ReferenceNumber = reference,
            RequestType = "change",
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // The queue stores a string payload verbatim (it only JSON-serialises non-strings),
        // so the pre-built RequestDocument JSON reaches the consumer exactly as shaped here.
        var messageBody = BuildMessageJson(reference, preset);
        var messageId = await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, messageBody, cancellationToken);

        // The journey timeline's first step. Failure-safe inside the recorder: a metrics
        // hiccup never breaks the submission that just enqueued the message.
        if (submittedMetrics is not null)
            await submittedMetrics.RecordAsync(QueueOptions.RulesEngineQueue, reference, messageId, cancellationToken);

        return Json(new
        {
            referenceNumber = reference,
            outcome = preset.Name,
            expectedDecision = preset.ExpectedDecision,
            message = $"Submitted request {reference} (preset '{preset.Name}', expecting {preset.ExpectedDecision}). " +
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

    private async Task EnsureCheckingWindowAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.CheckingWindows.AnyAsync(w => w.Id == DevWindowId, cancellationToken))
            return;

        dbContext.CheckingWindows.Add(new CheckingWindow
        {
            Id = DevWindowId,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "Dev Harness Window",
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Builds the same queue-shaped RequestDocument the rules consumer expects. The Answers
    // array drives the rule evaluation, so the preset's answers determine the outcome.
    private static string BuildMessageJson(string reference, OutcomePreset preset)
    {
        var answersJson = string.Join(",\n      ", preset.Answers.Select(a =>
            $"{{ \"QuestionId\": \"{a.QuestionId}\", \"QuestionTitle\": \"{a.QuestionId}\", \"Type\": \"text\", \"Value\": \"{a.Value}\" }}"));

        var windowId = DevWindowId.ToString();
        var submittedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        return $$"""
            {
              "CheckingWindowId": "{{windowId}}",
              "CheckingWindowType": "{{preset.CheckingWindowType}}",
              "WhatToChange": "{{preset.WhatToChange}}",
              "School": { "Urn": "123456", "Name": "Dev Harness School" },
              "SubmittedBy": { "UserId": "dev", "DisplayName": "Dev Harness" },
              "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": {{preset.PupilAge}}, "Upn": "UPN1" },
              "Answers": [
                  {{answersJson}}
              ],
              "ReferenceNumber": "{{reference}}",
              "SubmittedAt": "{{submittedAt}}"
            }
            """;
    }
}
