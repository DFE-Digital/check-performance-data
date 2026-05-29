using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class RulesConfigVersionRepository(IPortalDbContext context) : IRulesConfigVersionRepository
{
    public async Task<int> GetMaxVersionNumberAsync(RulesConfigType type, CancellationToken ct = default)
    {
        var max = await context.RulesConfigVersions
            .Where(v => v.ConfigType == type)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);
        return max ?? 0;
    }

    public async Task AddVersionAsync(RulesConfigType type, int versionNumber, string content, string? createdBy,
        DateTime createdAt, CancellationToken ct = default)
    {
        context.RulesConfigVersions.Add(new RulesConfigVersion
        {
            ConfigType = type, VersionNumber = versionNumber, Content = content,
            CreatedBy = createdBy, CreatedAt = createdAt
        });
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RulesConfigVersionDto>> ListAsync(RulesConfigType type, CancellationToken ct = default) =>
        await context.RulesConfigVersions
            .Where(v => v.ConfigType == type)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => ToDto(v))
            .ToListAsync(ct);

    public async Task<RulesConfigVersionDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await context.RulesConfigVersions
            .Where(v => v.Id == id)
            .Select(v => ToDto(v))
            .FirstOrDefaultAsync(ct);

    public async Task AddAuditAsync(string entityType, string entityId, string action, string? userId,
        DateTime timestamp, CancellationToken ct = default)
    {
        context.AuditEntries.Add(new AuditEntry
        {
            EntityType = entityType, EntityId = entityId, Action = action, UserId = userId, Timestamp = timestamp
        });
        await context.SaveChangesAsync(ct);
    }

    public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default) =>
        context.ExecuteInTransactionAsync(work, ct);

    private static RulesConfigVersionDto ToDto(RulesConfigVersion v) => new()
    {
        Id = v.Id, ConfigType = v.ConfigType, VersionNumber = v.VersionNumber,
        Content = v.Content, CreatedAt = v.CreatedAt, CreatedBy = v.CreatedBy
    };
}
