using System.Globalization;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The shared synthetic-request driver behind the dev pipeline trigger AND the HAT console's
// drive-traffic buttons. Both surfaces inject the same RequestDocument shape onto the rules-engine
// queue through this one path, so there is a single source of truth for what a dev request looks
// like and no copy-paste between the two controllers. Returns the reference it minted so a caller
// can remember it (the HAT "open journey for last reference" shortcut).
public sealed class DevPipelineRunner
{
    private readonly IPortalDbContext _dbContext;
    private readonly IQueueService _queueService;
    private readonly SubmittedMetricRecorder? _submittedMetrics;

    public DevPipelineRunner(
        IPortalDbContext dbContext,
        IQueueService queueService,
        SubmittedMetricRecorder? submittedMetrics = null)
    {
        _dbContext = dbContext;
        _queueService = queueService;
        _submittedMetrics = submittedMetrics;
    }

    // A stable dev checking window the synthetic requests hang off; upserted on demand so the
    // ChangeRequest foreign key is always satisfied without manual seeding.
    private static readonly Guid DevWindowId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    public sealed record DriveResult(string Reference, string PresetName, string ExpectedDecision);

    // Creates one synthetic ChangeRequest for the resolved preset and enqueues its RequestDocument
    // for the rules consumer, recording the journey's first (Submitted) metric. Returns the minted
    // reference and the preset's expected outcome.
    public async Task<DriveResult> SubmitAsync(string? outcome, CancellationToken cancellationToken)
    {
        var preset = OutcomePresets.Resolve(outcome);
        var reference = $"DEV-{Guid.NewGuid():N}"[..16];

        await EnsureCheckingWindowAsync(cancellationToken);

        _dbContext.ChangeRequests.Add(new ChangeRequest
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The queue stores a string payload verbatim (it only JSON-serialises non-strings), so the
        // pre-built RequestDocument JSON reaches the consumer exactly as shaped here.
        var messageBody = BuildMessageJson(reference, preset);
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
        // Look the dev window up by primary key (FindAsync) rather than a LINQ AnyAsync: the result
        // is identical against the real context, and the by-key lookup is unit-testable without an
        // async query provider.
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

    // Builds the same queue-shaped RequestDocument the rules consumer expects. The Answers array
    // drives the rule evaluation, so the preset's answers determine the outcome.
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
