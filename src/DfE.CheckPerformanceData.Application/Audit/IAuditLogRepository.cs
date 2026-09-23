using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>Audit log filters (AB#294592); null means "all". They are cumulative (AND).</summary>
public sealed record AuditLogFilter(string? Activity, Guid? WindowId, AuditOutcome? Outcome)
{
    public bool IsEmpty => Activity is null && WindowId is null && Outcome is null;
}

/// <summary>
/// One audit log row. Deliberately carries no OldValues/NewValues/ChangedColumns: the generic
/// capture stores pupil-bearing entities' values there and the log must never render them.
/// UserName is known only for egress rows (the payload's transferredBy); WindowTitle is null when
/// the row has no window or the window cannot be named; OutputTypes are the raw enum names.
/// </summary>
public sealed record AuditLogRow(
    long Id,
    DateTime TimestampUtc,
    string? UserId,
    string? UserName,
    string EntityType,
    string EntityId,
    string Action,
    Guid? WindowId,
    string? WindowTitle,
    IReadOnlyList<string> OutputTypes,
    AuditOutcome? Outcome);

/// <summary>One page of the log. Page is 1-based and already clamped to [1, TotalPages].</summary>
public sealed record AuditLogPage(IReadOnlyList<AuditLogRow> Rows, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}

public interface IAuditLogRepository
{
    /// <summary>Filtered, newest Timestamp first (Id descending on a tie), one page. An out-of-range page clamps; a pageSize below 1 is treated as 1.</summary>
    Task<AuditLogPage> ListAsync(AuditLogFilter filter, int page, int pageSize, CancellationToken ct);
    /// <summary>The export: every row the same filter and order would page through, streamed.</summary>
    IAsyncEnumerable<AuditLogRow> StreamAsync(AuditLogFilter filter, CancellationToken ct);
    /// <summary>The distinct EntityType values present, always including "EgressRun", ordered by label.</summary>
    Task<IReadOnlyList<string>> ListActivitiesAsync(CancellationToken ct);
}
