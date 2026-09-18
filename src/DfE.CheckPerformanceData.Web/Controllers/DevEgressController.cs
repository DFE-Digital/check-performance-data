using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only fixture for the LDS egress (AB#294553): stages committed requests with a Zendesk ticket
// id, a journey blob and a dev-outbox ticket carrying a chosen decision, so the whole egress can be
// walked locally and by the E2E suite without a worker or a real Zendesk. 404 in Production and
// wherever Dev:ToolsEnabled is off, like DevPipelineController; [AllowAnonymous] for the same
// reason — E2E callers arrive with no cookie and it only touches the local dev database.
[AllowAnonymous]
public sealed class DevEgressController(
    IConfiguration configuration,
    IPortalDbContext dbContext,
    IRequestStateBlobClient journeys,
    IEgressBlobClient egressBlobs,
    IHostEnvironment? hostEnvironment = null) : Controller
{
    private const string ReferencePrefix = "DEV-EGRESS-";
    private static long _nextTicketId = 900_000_000 + DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100_000_000;

    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled) && hostEnvironment?.IsProduction() != true;

    [HttpPost("dev/egress/seed")]
    public async Task<IActionResult> Seed(Guid windowId, string outputType, string decision, int count, string laestab, long urn, string? reason, CancellationToken cancellationToken)
    {
        if (!IsAllowed) return NotFound();
        if (!Enum.TryParse<EgressOutputType>(outputType, ignoreCase: true, out var type)) return BadRequest("outputType must be NewLearners or RemoveLearners");
        count = Math.Clamp(count, 1, 50);

        var window = await dbContext.CheckingWindows.AsNoTracking().FirstOrDefaultAsync(w => w.Id == windowId, cancellationToken);
        if (window is null) return BadRequest("Unknown window");

        var references = new List<string>();
        var ticketIds = new List<long>();
        for (var i = 0; i < count; i++)
        {
            var reference = $"{ReferencePrefix}{Guid.NewGuid():N}"[..(ReferencePrefix.Length + 8)];
            var ticketId = decision == "none" ? (long?)null : Interlocked.Increment(ref _nextTicketId);
            var pupilId = Guid.NewGuid();
            var surname = $"Egress{i + 1}";
            var forename = type == EgressOutputType.NewLearners ? "Newbie" : "Removal";

            dbContext.ChangeRequests.Add(new ChangeRequest
            {
                Id = Guid.NewGuid(), WindowId = windowId, OrganisationUrn = urn, OrganisationLaestab = laestab,
                PupilId = pupilId, PupilUpn = "A860407000011", PupilFirstname = forename, PupilSurname = surname,
                Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), SubmittedById = Guid.NewGuid(),
                SubmittedByName = "Dev Egress Harness", SubmittedByEmail = "dev.egress@education.gov.uk",
                Status = RequestStatus.SubmittedCommitted, ReferenceNumber = reference, RequestType = RequestType.Amendment,
                RequestTypeDescription = type == EgressOutputType.NewLearners ? "Add" : $"Remove - {reason ?? "pupil-died"}",
                AmendmentType = EgressOutputTypes.WhatToChangeFor(type), CrmId = ticketId?.ToString()
            });

            var journey = new RequestState
            {
                SelectedWhatToChange = EgressOutputTypes.WhatToChangeFor(type),
                ReferenceNumber = reference,
                SelectedPupil = new PupilDto
                {
                    Id = pupilId, Firstname = forename, Surname = surname, Sex = "F", DateOfBirth = "07/09/2010", Age = 15,
                    Cypmd_Id = type == EgressOutputType.NewLearners ? "" : $"50{i:D4}", Identifier = "A860407000011",
                    MatchRef = type == EgressOutputType.NewLearners ? 0 : 555000 + i,
                    Laestab = type == EgressOutputType.NewLearners ? "" : laestab
                }
            };
            if (type == EgressOutputType.NewLearners)
            {
                journey.QuestionAnswers["first-name"] = new QuestionAnswer { TextValue = forename };
                journey.QuestionAnswers["last-name"] = new QuestionAnswer { TextValue = surname };
                journey.QuestionAnswers["date-of-birth"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 7, Month = 9, Year = 2010 } };
                journey.QuestionAnswers["sex"] = new QuestionAnswer { TextValue = "F" };
                journey.QuestionAnswers["upn"] = new QuestionAnswer { TextValue = "A860407000011" };
                journey.QuestionAnswers["admission-date"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 4, Month = 9, Year = 2018 } };
                journey.QuestionAnswers["year-group"] = new QuestionAnswer { TextValue = window.CheckingWindowType == CheckingWindowType.KS2 ? "6" : "10" };
                journey.QuestionAnswers["sen-status"] = new QuestionAnswer { TextValue = "N" };
            }
            else
            {
                journey.QuestionAnswers["reason"] = new QuestionAnswer { TextValue = reason ?? "pupil-died" };
                journey.QuestionAnswers["date-removed-from-roll"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 1, Month = 6, Year = 2026 } };
            }
            await journeys.SaveAsync(windowId, reference, journey);

            if (ticketId is { } id)
            {
                var subject = decision switch
                {
                    "auto_approved" => $"CPMD Auto-Approved: Seed ({reference})",
                    "auto_rejected" => $"CPMD Auto-Rejected: Seed ({reference})",
                    _ => $"CPMD Requires Scrutiny: Seed ({reference})"
                };
                var fieldId = configuration.GetValue<long?>("ZendeskTicketFields:DecisionStatusId") is > 0 and var configured
                    ? configured : DevOutboxEgressTicketSource.WellKnownDecisionFieldId;
                var raw = new CreateTicketRequestDto
                {
                    Ticket = new CreateTicketDto { Subject = subject, Status = "open", Priority = "normal", Type = "task",
                        CustomFields = [new CustomFieldDto { Id = fieldId, Value = decision }] }
                };
                dbContext.DevZendeskTickets.Add(new DevZendeskTicket
                {
                    Id = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, ReferenceNumber = reference, Subject = subject,
                    Priority = "normal", Status = "open", TicketId = id, RawJson = JsonSerializer.Serialize(raw)
                });
                ticketIds.Add(id);
            }
            references.Add(reference);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return Json(new { references, ticketIds });
    }

    [HttpPost("dev/egress/cleanup")]
    public async Task<IActionResult> Cleanup(Guid windowId, CancellationToken cancellationToken)
    {
        if (!IsAllowed) return NotFound();

        // Nit: delete only blobs owned by a run this cleanup is actually removing, matched by
        // egressRunId metadata (the M1 sweep helper) — plain by-filename deletion could remove
        // another run's blob if two windows' files collided on name (Q3).
        var owners = await dbContext.EgressRunOutputs.AsNoTracking()
            .Where(o => o.WindowId == windowId && o.FileName != null)
            .Select(o => new { o.RunId, FileName = o.FileName! }).Distinct().ToListAsync(cancellationToken);
        // Best effort, per blob: a reset must reset. This used to throw straight out of the action,
        // so on an environment whose egress account was unreachable every cleanup answered 500 and
        // left the runs behind — and a leftover run is what stopped the dev seeder (and so the pod)
        // starting on the next deploy. A failed delete is reported in the response, not fatal.
        var blobs = 0;
        var blobErrors = 0;
        if (egressBlobs.IsConfigured)
        {
            foreach (var owner in owners)
            {
                try
                {
                    if (await egressBlobs.DeleteIfOwnedByRunAsync(owner.FileName, owner.RunId, cancellationToken)) blobs++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    blobErrors++;
                }
            }
        }

        var runs = await dbContext.EgressRuns.Where(r => r.WindowId == windowId).ExecuteDeleteAsync(cancellationToken);

        var devRequests = await dbContext.ChangeRequests.AsNoTracking()
            .Where(r => r.WindowId == windowId && EF.Functions.Like(r.ReferenceNumber, ReferencePrefix + "%"))
            .Select(r => new { r.Id, r.ReferenceNumber }).ToListAsync(cancellationToken);
        foreach (var r in devRequests) await journeys.DeleteAsync(windowId, r.ReferenceNumber);
        var refs = devRequests.Select(r => r.ReferenceNumber).ToList();
        await dbContext.DevZendeskTickets.Where(t => refs.Contains(t.ReferenceNumber)).ExecuteDeleteAsync(cancellationToken);
        var ids = devRequests.Select(r => r.Id).ToList();
        var requests = ids.Count == 0 ? 0 : await dbContext.ChangeRequests.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);

        return Json(new { runs, requests, blobs, blobErrors });
    }
}
