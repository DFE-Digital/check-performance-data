using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The Pull step (AB#294553). Refuses first, pulls second: every candidate request for the window
/// and output types is returned with the decision Zendesk holds for it — nothing is filtered here,
/// so the ops user can see the pull worked before anything is discarded. The ticket's values are
/// the truth: preprocessing reads each LDS column that has a Zendesk field from the ticket, and
/// reads CYPMD's data only for the columns that have no field (docs/data-egress.md).
/// </summary>
public sealed class EgressRunService(
    IEgressRunRepository repository,
    IEgressTicketSource tickets,
    IRequestStateBlobClient journeys,
    IWindowService windows,
    ILogger<EgressRunService> logger) : IEgressRunService
{
    public async Task<EgressStartResult> StartAsync(Guid windowId, IReadOnlyList<EgressOutputType> outputTypes, EgressActor actor, CancellationToken ct)
    {
        if (outputTypes.Count == 0)
            throw new ArgumentException("At least one output type is required.", nameof(outputTypes));
        var types = outputTypes.Distinct().ToList();

        var blockers = await BlockersAsync(windowId, types, ct);
        if (blockers.Count > 0) return new EgressStartResult.Refused(blockers);

        var window = await windows.GetByIdAsync(windowId, ct);
        if (window is null) return new EgressStartResult.WindowNotFound();

        var outputs = new List<EgressRunOutputCreate>(types.Count);
        try
        {
            foreach (var type in types)
                outputs.Add(new EgressRunOutputCreate(type, await PullAsync(window, type, ct)));
        }
        catch (EgressTicketSourceException ex)
        {
            logger.LogWarning(ex, "Egress pull for window {WindowId} could not read Zendesk tickets", windowId);
            return new EgressStartResult.PullFailed(ex.Message);
        }

        try
        {
            var runId = await repository.CreateRunAsync(new EgressRunCreate(windowId, actor.UserId, actor.DisplayName, actor.Email, outputs), ct);
            return new EgressStartResult.Started(runId);
        }
        catch (EgressRunConflictException)
        {
            // Lost the race to another admin between the check and the insert: tell them who won.
            return new EgressStartResult.Refused(await BlockersAsync(windowId, types, ct));
        }
    }

    private async Task<IReadOnlyList<(EgressOutputType, EgressBlocker)>> BlockersAsync(Guid windowId, IReadOnlyList<EgressOutputType> types, CancellationToken ct)
    {
        var blockers = new List<(EgressOutputType, EgressBlocker)>();
        foreach (var type in types)
        {
            var blocker = await repository.FindBlockerAsync(windowId, type, ct);
            if (blocker is not null) blockers.Add((type, blocker));
        }
        return blockers;
    }

    private async Task<IReadOnlyList<EgressSourceRecord>> PullAsync(CheckingWindowDto window, EgressOutputType type, CancellationToken ct)
    {
        var candidates = await repository.GetCandidateRequestsAsync(window.Id, EgressOutputTypes.WhatToChangeFor(type), ct);
        var ticketIds = candidates.Select(c => TicketId(c.CrmId)).OfType<long>().ToList();
        var found = ticketIds.Count == 0
            ? new Dictionary<long, EgressTicket>()
            : await tickets.GetTicketsAsync(ticketIds, ct);

        var records = new List<EgressSourceRecord>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var ticketId = TicketId(candidate.CrmId);
            var ticket = ticketId is { } id && found.TryGetValue(id, out var t) ? t : null;
            var decision = ticketId is null ? EgressDecisions.NoTicket
                : ticket?.Decision ?? EgressDecisions.NotFound;

            RequestState? journey;
            try { journey = await journeys.GetAsync(window.Id, candidate.ReferenceNumber); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Egress could not read the journey blob for {Reference}", candidate.ReferenceNumber);
                journey = null;
            }
            var pupil = journey?.SelectedPupil;

            records.Add(new EgressSourceRecord
            {
                ChangeRequestId = candidate.ChangeRequestId,
                ReferenceNumber = candidate.ReferenceNumber,
                TicketId = ticketId,
                Decision = decision,
                OutputType = type,
                WindowType = window.CheckingWindowType,
                SubmittedAtUtc = candidate.SubmittedAtUtc,
                OrganisationUrn = candidate.OrganisationUrn,
                OrganisationLaestab = candidate.OrganisationLaestab,
                PupilFirstname = pupil?.Firstname,
                PupilSurname = pupil?.Surname,
                PupilDateOfBirth = pupil?.DateOfBirth,
                PupilSex = pupil?.Sex,
                PupilIdentifier = pupil?.Identifier,
                PupilCypmdId = pupil?.Cypmd_Id,
                PupilMatchRef = pupil?.MatchRef ?? 0,
                PupilLaestab = pupil?.Laestab,
                PupilEntryDate = pupil?.EntryDate,
                JourneyFound = journey is not null,
                Answers = journey is null ? new Dictionary<string, string>() : EgressAnswers.Flatten(journey),
                TicketFields = ticket?.Fields ?? new Dictionary<string, string>()
            });
        }
        return records;
    }

    private static long? TicketId(string? crmId) =>
        long.TryParse(crmId, out var id) && id > 0 ? id : null;

    public Task<EgressRunDto?> GetAsync(Guid runId, CancellationToken ct) => repository.GetRunAsync(runId, ct);
    public Task<IReadOnlyList<EgressRunListItem>> ListAsync(CancellationToken ct) => repository.ListRunsAsync(ct);
    public Task<EgressRunHistoryPage> ListHistoryAsync(EgressRunHistoryFilter filter, int page, int pageSize, CancellationToken ct) =>
        repository.ListHistoryAsync(filter, page, pageSize, ct);
}
