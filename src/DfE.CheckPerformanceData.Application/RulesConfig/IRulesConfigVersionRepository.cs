namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Append-only history of saved config documents, plus the audit write. Implemented in the
/// Persistence layer over IPortalDbContext.
/// </summary>
public interface IRulesConfigVersionRepository
{
    Task<int> GetMaxVersionNumberAsync(RulesConfigType type, CancellationToken ct = default);
    Task AddVersionAsync(RulesConfigType type, int versionNumber, string content, string? createdBy,
        DateTime createdAt, CancellationToken ct = default);
    Task<IReadOnlyList<RulesConfigVersionDto>> ListAsync(RulesConfigType type, CancellationToken ct = default);
    Task<RulesConfigVersionDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAuditAsync(string entityType, string entityId, string action, string? userId,
        DateTime timestamp, CancellationToken ct = default);
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default);
}
