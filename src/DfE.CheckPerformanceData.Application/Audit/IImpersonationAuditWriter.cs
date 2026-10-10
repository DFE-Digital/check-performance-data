namespace DfE.CheckPerformanceData.Application.Audit;

public sealed record ImpersonationAuditRecord(string UserId, string Laestab, string Urn, string Action, DateTime TimestampUtc);

public interface IImpersonationAuditWriter
{
    Task RecordAsync(ImpersonationAuditRecord record, CancellationToken ct);
}
