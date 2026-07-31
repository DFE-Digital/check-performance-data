using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The shared synthetic-request driver behind the dev pipeline trigger AND the UAT console's
// drive-traffic buttons. Both surfaces inject the same RequestDocument shape onto the rules-engine
// queue through this one path, so there is a single source of truth for what a dev request looks
// like and no copy-paste between the two controllers. Returns the reference it minted so a caller
// can remember it (the UAT "open journey for last reference" shortcut).
public sealed class DevPipelineRunner
{
    private readonly IPortalDbContext _dbContext;
    private readonly IQueueService _queueService;
    private readonly IPupilDataBlobClient _pupilBlob;
    private readonly SubmittedMetricRecorder? _submittedMetrics;

    public DevPipelineRunner(
        IPortalDbContext dbContext,
        IQueueService queueService,
        IPupilDataBlobClient pupilBlob,
        SubmittedMetricRecorder? submittedMetrics = null)
    {
        _dbContext = dbContext;
        _queueService = queueService;
        _pupilBlob = pupilBlob;
        _submittedMetrics = submittedMetrics;
    }

    private static readonly Guid DevWindowId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    public sealed record DriveResult(string Reference, string PresetName, string ExpectedDecision);

    // Creates one synthetic ChangeRequest for the resolved preset and enqueues its RequestDocument
    // for the rules consumer, recording the journey's first (Submitted) metric. Returns the minted
    // reference and the preset's expected outcome.
    // Pupil parameters are optional — when omitted the old hardcoded "Bob Smith"/"UPN1" values
    // are used and PupilId is left null (no conflict matching); supply them to target a real pupil
    // for conflict-detection testing.
    // When pupilUpn is provided together with windowId and laestab, the pupil is looked up from
    // blob storage so PupilId and name fields reflect the real record (overriding any explicit
    // pupilId/pupilFirstName/pupilSurname values).
    public async Task<DriveResult> SubmitAsync(
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
        var preset = OutcomePresets.Resolve(outcome);
        var reference = $"DEV-{Guid.NewGuid():N}"[..16];
        var changeRequestId = Guid.NewGuid();
        var resolvedWindowId = windowId ?? DevWindowId;
        var resolvedUrn = urn ?? 123456;

        // Derive laestab from the UPN when not explicitly provided.
        // UPN format: ALLLEEEESSSSC (13 chars) — A + 3-digit LEA + 4-digit school + serial + check.
        // KS4 only: a Post16 pupil's identifier is a ULN, which has no laestab embedded in it, so
        // callers must pass laestab explicitly for a 16-19 window.
        if (laestab is null && pupilUpn is not null && pupilUpn.Length >= 13)
            laestab = $"{pupilUpn.Substring(1, 3)}/{pupilUpn.Substring(4, 4)}";

        // Look up the real pupil from blob storage so we get the true PupilId (needed for
        // conflict matching), verified name fields, etc. Supports both UPN and name-based
        // matching — the caller can supply either.
        IPupilRecord? matchedPupil = null;
        if (windowId is not null && laestab is not null)
        {
            var pupils = await _pupilBlob.GetPupilsAsync(resolvedWindowId, laestab, CheckingWindowType.KS4June);
            if (pupils is not null)
            {
                if (pupilUpn is not null)
                    matchedPupil = pupils.FirstOrDefault(p =>
                        p.Identifier.Equals(pupilUpn, StringComparison.OrdinalIgnoreCase));
                else if (pupilFirstName is not null && pupilSurname is not null)
                    matchedPupil = pupils.FirstOrDefault(p =>
                        p.Firstname.Equals(pupilFirstName, StringComparison.OrdinalIgnoreCase) &&
                        p.Surname.Equals(pupilSurname, StringComparison.OrdinalIgnoreCase));
            }
        }

        var resolvedPupilId = matchedPupil?.Id ?? pupilId;
        var resolvedPupilUpn = matchedPupil?.Identifier ?? pupilUpn ?? "UPN1";
        var resolvedPupilFirstname = matchedPupil?.Firstname ?? pupilFirstName ?? "Bob";
        var resolvedPupilSurname = matchedPupil?.Surname ?? pupilSurname ?? "Smith";
        var resolvedSubmittedById = userId ?? Guid.NewGuid();
        var resolvedUserEmail = userEmail ?? "dev.harness@education.gov.uk";

        if (windowId is null)
            await EnsureCheckingWindowAsync(cancellationToken);

        _dbContext.ChangeRequests.Add(new ChangeRequest
        {
            Id = changeRequestId,
            WindowId = resolvedWindowId,
            OrganisationUrn = resolvedUrn,
            PupilId = resolvedPupilId,
            PupilUpn = resolvedPupilUpn,
            PupilFirstname = resolvedPupilFirstname,
            PupilSurname = resolvedPupilSurname,
            Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = resolvedSubmittedById,
            SubmittedByName = "Dev Harness",
            SubmittedByEmail = resolvedUserEmail,
            Status = RequestStatus.SubmittedUnCommitted,
            ReferenceNumber = reference,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = requestType ?? preset.WhatToChange
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The queue stores a string payload verbatim (it only JSON-serialises non-strings), so the
        // pre-built RequestDocument JSON reaches the consumer exactly as shaped here.
        var messageBody = BuildMessageJson(reference, preset, changeRequestId, resolvedWindowId, resolvedUrn, resolvedPupilId, resolvedPupilUpn, resolvedPupilFirstname, resolvedPupilSurname);
        var messageId = await _queueService.EnqueueAsync(
            QueueOptions.RulesEngineQueue, messageBody, cancellationToken);

        // The journey timeline's first step. Failure-safe inside the recorder: a metrics hiccup
        // never breaks the submission that just enqueued the message.
        if (_submittedMetrics is not null)
            await _submittedMetrics.RecordAsync(
                QueueOptions.RulesEngineQueue, reference, messageId, cancellationToken);

        return new DriveResult(reference, preset.Name, preset.ExpectedDecision);
    }

    private async Task EnsureCheckingWindowAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.CheckingWindows.FindAsync(new object?[] { DevWindowId }, cancellationToken) is not null)
            return;

        _dbContext.CheckingWindows.Add(new CheckingWindow
        {
            Id = DevWindowId,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Title = "Dev Harness Window",
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildMessageJson(string reference, OutcomePreset preset, Guid changeRequestId, Guid windowId, long urn, Guid? pupilId, string pupilUpn, string pupilFirstname, string pupilSurname)
    {
        var answersJson = string.Join(",\n      ", preset.Answers.Select(a =>
            $"{{ \"QuestionId\": \"{a.QuestionId}\", \"QuestionTitle\": \"{a.QuestionId}\", \"Type\": \"text\", \"Value\": \"{a.Value}\" }}"));

        var windowIdStr = windowId.ToString();
        var submittedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var pupilIdStr = pupilId?.ToString() ?? "p1";

        return $$"""
            {
              "ChangeRequestId": "{{changeRequestId}}",
              "CheckingWindowId": "{{windowIdStr}}",
              "CheckingWindowType": "{{preset.CheckingWindowType}}",
              "RequestTypeCode": "{{preset.WhatToChange}}",
              "School": { "Urn": "{{urn}}", "Name": "Dev Harness School" },
              "SubmittedBy": { "UserId": "dev", "DisplayName": "Dev Harness" },
              "Pupil": { "Id": "{{pupilIdStr}}", "CypmdId": "c1", "Firstname": "{{pupilFirstname}}", "Surname": "{{pupilSurname}}", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": {{preset.PupilAge}}, "Upn": "{{pupilUpn}}", "Pincl": {{preset.Pincl}} },
              "Answers": [
                  {{answersJson}}
              ],
              "ReferenceNumber": "{{reference}}",
              "SubmittedAt": "{{submittedAt}}"
            }
            """;
    }
}
