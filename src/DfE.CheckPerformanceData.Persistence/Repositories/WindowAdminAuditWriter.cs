using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <inheritdoc cref="IWindowAdminAuditWriter"/>
public sealed class WindowAdminAuditWriter(IPortalDbContext db) : IWindowAdminAuditWriter
{
    // Read back by WindowAdminAuditPayload with the same (camelCase) options.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task RecordAutomaticHandOverAsync(AutomaticHandOverAudit record, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            EntityType = AuditActivities.WindowAdmin,
            // The window id, so the audit log's window filter matches the row without reading JSON.
            EntityId = record.WindowId.ToString(),
            Action = AuditActivities.RequestsSentAutomaticallyAction,
            Timestamp = record.RanAtUtc,
            // Nobody pressed anything. A null user is what the audit log shows as "System".
            UserId = null,
            NewValues = JsonSerializer.Serialize(new
            {
                record.WindowId,
                record.WindowTitle,
                record.ExerciseId,
                ExerciseType = record.Exercise.ToString(),
                record.ExerciseEnd,
                record.RequestsSent,
                record.DraftsCancelled,
                Automatic = true,
                record.RanAtUtc
            }, AuditJson)
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
