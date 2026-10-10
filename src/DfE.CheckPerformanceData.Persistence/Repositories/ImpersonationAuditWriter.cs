using System.Text.Json;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformance.Persistence.Entities;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class ImpersonationAuditWriter(IPortalDbContext db) : IImpersonationAuditWriter
{
    public async Task RecordAsync(ImpersonationAuditRecord record, CancellationToken ct)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            EntityType = "Impersonation", EntityId = $"LAESTAB {record.Laestab}, URN {record.Urn}", Action = record.Action,
            UserId = record.UserId, Timestamp = DateTime.SpecifyKind(record.TimestampUtc, DateTimeKind.Utc),
            NewValues = JsonSerializer.Serialize(new { record.Laestab, record.Urn, Outcome = record.Action == "BlockedChange" ? "Failed" : "Success" })
        });
        await db.SaveChangesAsync(ct);
    }
}
