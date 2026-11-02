using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.ExerciseDecisions;

/// <inheritdoc cref="IExerciseDecisionPullService"/>
public sealed class ExerciseDecisionPullService(
    IExerciseDecisionPullRepository repository,
    IEgressTicketSource tickets,
    ICurrentUserService user,
    ILogger<ExerciseDecisionPullService> logger) : IExerciseDecisionPullService
{
    public async Task<ExerciseDecisionPullPreview> PreviewAsync(Guid windowId, Guid exerciseId, CancellationToken ct)
    {
        var rows = await repository.GetSubmittedRowsAsync(windowId, exerciseId, ct);
        return new ExerciseDecisionPullPreview(rows.Count, rows.Count(r => TicketIds.Parse(r.CrmId) is not null));
    }

    public async Task<ExerciseDecisionPullResult> PullAsync(Guid windowId, Guid exerciseId, CancellationToken ct)
    {
        var rows = await repository.GetSubmittedRowsAsync(windowId, exerciseId, ct);
        if (rows.Count == 0)
            return await SaveAsync(windowId, exerciseId, [], Result.Zero(), ct);

        var ticketIds = rows.Select(r => TicketIds.Parse(r.CrmId)).OfType<long>().ToList();

        IReadOnlyDictionary<long, EgressTicketDecision> decisions;
        try
        {
            decisions = await tickets.GetTicketDecisionsAsync(ticketIds, ct);
        }
        catch (EgressTicketSourceException ex)
        {
            // Fail closed: the banner carries the source's reason and nothing is written.
            logger.LogWarning(ex, "The decision pull for window {WindowId} exercise {ExerciseId} could not read Zendesk", windowId, exerciseId);
            return new ExerciseDecisionPullResult
            {
                Accepted = 0, Rejected = 0, Unchanged = 0, NoTicket = 0, TicketNotFound = 0,
                FailureReason = ex.Message
            };
        }

        var accepted = 0;
        var rejected = 0;
        var unchanged = 0;
        var noTicket = 0;
        var ticketNotFound = 0;
        var saved = new List<SavedDecision>(rows.Count);
        foreach (var row in rows)
        {
            // A row with no ticket id was never sent to Zendesk: "no ticket". A row whose stored
            // id we cannot read thinks it has a ticket we cannot use: "not found".
            if (string.IsNullOrWhiteSpace(row.CrmId)) { noTicket++; continue; }
            var ticketId = TicketIds.Parse(row.CrmId);
            if (ticketId is null) { ticketNotFound++; continue; }
            if (!decisions.TryGetValue(ticketId.Value, out var ticket) || ticket.Decision is null) { ticketNotFound++; continue; }
            var decision = MapDecision(ticket.Decision);
            if (decision is null) { unchanged++; continue; }

            // Only a final read is stored, so a later scrutiny read can never clear a stored
            // decision; a later final read overwrites decision and reason together (latest wins).
            if (decision == ZendeskDecision.Accepted) accepted++;
            else rejected++;
            saved.Add(new SavedDecision(row.ChangeRequestId, decision.Value, ticket.Reason));
        }

        return await SaveAsync(windowId, exerciseId, saved, new ExerciseDecisionPullResult
        {
            Accepted = accepted, Rejected = rejected, Unchanged = unchanged,
            NoTicket = noTicket, TicketNotFound = ticketNotFound
        }, ct);
    }

    // The deliberate audit row is written on every completed pull — including an all-zero one —
    // because the admin's action (and what Zendesk showed at the time) is what is being recorded.
    private async Task<ExerciseDecisionPullResult> SaveAsync(
        Guid windowId, Guid exerciseId, IReadOnlyList<SavedDecision> saved,
        ExerciseDecisionPullResult result, CancellationToken ct)
    {
        await repository.SaveDecisionsAsync(saved, new DecisionsPulledAudit
        {
            WindowId = windowId,
            ExerciseId = exerciseId,
            Accepted = result.Accepted,
            Rejected = result.Rejected,
            Unchanged = result.Unchanged,
            NoTicket = result.NoTicket,
            TicketNotFound = result.TicketNotFound,
            RanAtUtc = DateTime.UtcNow,
            UserId = user.UserId
        }, ct);
        return result;
    }

    // approved / auto_approved / rejected / auto_rejected are the final values; anything else
    // (scrutiny, a blank, a value we do not know) is not a decision yet.
    private static ZendeskDecision? MapDecision(string decision) =>
        decision switch
        {
            ZendeskTicketFieldOptions.DecisionStatus.Approved or ZendeskTicketFieldOptions.DecisionStatus.AutoApproved => ZendeskDecision.Accepted,
            ZendeskTicketFieldOptions.DecisionStatus.Rejected or ZendeskTicketFieldOptions.DecisionStatus.AutoRejected => ZendeskDecision.Rejected,
            _ => null
        };

    private static class Result
    {
        public static ExerciseDecisionPullResult Zero() => new()
        {
            Accepted = 0, Rejected = 0, Unchanged = 0, NoTicket = 0, TicketNotFound = 0
        };
    }
}
