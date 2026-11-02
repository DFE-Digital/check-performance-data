using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.ExerciseDecisions;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <inheritdoc cref="IExerciseDecisionPullRepository"/>
public sealed class ExerciseDecisionPullRepository(IPortalDbContext db) : IExerciseDecisionPullRepository
{
    // Read back by the audit log with the same (camelCase) options.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    // The exercise's submitted rows, whatever the school and whatever the request type: the
    // admin pulls one exercise, not one school. A row with no CrmId is returned as-is — "no
    // ticket" is a bucket the service counts, not a row this hides.
    public async Task<IReadOnlyList<ExerciseDecisionPullRow>> GetSubmittedRowsAsync(Guid windowId, Guid exerciseId, CancellationToken ct) =>
        await db.ChangeRequests.AsNoTracking()
            .Where(r => r.WindowId == windowId
                && r.CheckingExerciseId == exerciseId
                && r.Status == RequestStatus.Submitted)
            .OrderBy(r => r.Submitted)
            .Select(r => new ExerciseDecisionPullRow(r.Id, r.ReferenceNumber, r.CrmId, r.ZendeskDecision, r.ZendeskDecisionReason))
            .ToListAsync(ct);

    // One transaction for the row updates and the audit row: the pull's write is all-or-nothing,
    // so a failure can never leave decisions stored without their audit trail (or vice versa).
    public async Task<int> SaveDecisionsAsync(IReadOnlyList<SavedDecision> saved, DecisionsPulledAudit audit, CancellationToken ct)
    {
        await db.ExecuteInTransactionAsync(async () =>
        {
            // The execution strategy may run this delegate again after a transient failure; an
            // AuditEntry still tracked from the failed attempt would then be saved twice.
            db.ChangeTracker.Clear();

            if (saved.Count > 0)
            {
                var ids = saved.Select(s => s.ChangeRequestId).ToList();
                var rows = await db.ChangeRequests
                    .Where(r => r.WindowId == audit.WindowId
                        && r.CheckingExerciseId == audit.ExerciseId
                        && ids.Contains(r.Id))
                    .ToListAsync(ct);
                var byId = rows.ToDictionary(r => r.Id);
                foreach (var decision in saved)
                {
                    if (!byId.TryGetValue(decision.ChangeRequestId, out var row))
                        continue;
                    row.ZendeskDecision = decision.Decision;
                    row.ZendeskDecisionReason = decision.Reason;
                }
            }

            // Looked up here, as in CloseExerciseEarlyAsync: the payload records what the database
            // says, and the window filter matches on EntityId without reading the JSON.
            var title = await db.CheckingWindows
                .Where(w => w.Id == audit.WindowId)
                .Select(w => w.Title)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
            var exerciseName = await db.CheckingExercises
                .Where(e => e.Id == audit.ExerciseId)
                .Select(e => e.Name)
                .FirstOrDefaultAsync(ct);

            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = AuditActivities.WindowAdmin,
                EntityId = audit.WindowId.ToString(),
                Action = AuditActivities.DecisionsPulledAction,
                Timestamp = audit.RanAtUtc,
                UserId = audit.UserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    audit.WindowId,
                    WindowTitle = title,
                    audit.ExerciseId,
                    ExerciseName = exerciseName,
                    audit.Accepted,
                    audit.Rejected,
                    audit.Unchanged,
                    audit.NoTicket,
                    audit.TicketNotFound,
                    audit.RanAtUtc
                }, AuditJson)
            });
            await db.SaveChangesAsync(ct);
        }, ct);
        return saved.Count;
    }
}
