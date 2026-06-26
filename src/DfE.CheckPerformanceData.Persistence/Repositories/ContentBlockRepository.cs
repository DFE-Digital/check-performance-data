using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class ContentBlockRepository(IPortalDbContext context) : IContentBlockRepository
{
    // Queries — use ProjectToDto so EF translates the mapping to SQL

    public async Task<List<ContentBlockDto>> GetAllAsync() =>
        await context.ContentBlocks
            .AsNoTracking()
            .OrderBy(b => b.Key)
            .ProjectToDto()
            .ToListAsync();

    public async Task<ContentBlockDto?> GetByKeyAsync(string key) =>
        await context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.Key == key)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<ContentBlockDto?> GetByContentIdAsync(Guid contentId) =>
        await context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.ContentId == contentId)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<int> GetMaxVersionNumberAsync(int contentBlockId) =>
        await context.ContentBlockVersions
            .Where(v => v.ContentBlockId == contentBlockId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

    public async Task<ContentBlockVersionDto?> GetVersionByIdAsync(int versionId) =>
        await context.ContentBlockVersions
            .AsNoTracking()
            .Where(v => v.Id == versionId)
            .ProjectToVersionDto()
            .FirstOrDefaultAsync();

    public async Task<List<ContentBlockVersionDto>> GetVersionsByKeyAsync(string key) =>
        await context.ContentBlockVersions
            .AsNoTracking()
            .Where(v => v.ContentBlock.Key == key)
            .OrderByDescending(v => v.VersionNumber)
            .ProjectToVersionDto()
            .ToListAsync();

    // Commands — work with tracked entities internally

    public async Task<ContentBlockDto> AddBlockAsync(string key, string blockType, string value, Guid? contentId = null)
    {
        var entity = new ContentBlock
        {
            Key = key,
            BlockType = blockType,
            Value = value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Preserve a supplied cross-environment identity; otherwise the DB default generates one.
        if (contentId is { } id && id != Guid.Empty)
            entity.ContentId = id;

        context.ContentBlocks.Add(entity);
        await context.SaveChangesAsync();

        return ContentBlockMapper.ToDto(entity);
    }

    // Content-staging Replace: overwrite an existing block in place — including a Key/type change
    // (a "rename") and reconciling its cross-environment identity — matched by row id.
    public async Task UpdateForStagingAsync(int id, string key, string blockType, string value, Guid contentId)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.Key = key;
        entity.BlockType = blockType;
        entity.Value = value;
        if (contentId != Guid.Empty)
            entity.ContentId = contentId;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    public async Task AddVersionAsync(int contentBlockId, string value, int versionNumber)
    {
        var version = new ContentBlockVersion
        {
            ContentBlockId = contentBlockId,
            Value = value,
            VersionNumber = versionNumber,
            CreatedAt = DateTime.UtcNow
        };

        context.ContentBlockVersions.Add(version);
        await context.SaveChangesAsync();
    }

    // Records where a block was last rendered. Called from the editable view components only when
    // the path actually changed, so this rarely writes; matched by Key (a no-op if the block has
    // no saved row yet).
    public async Task SetLastSeenAsync(string key, string path, DateTime seenAt)
    {
        var entity = await context.ContentBlocks.FirstOrDefaultAsync(b => b.Key == key);
        if (entity is null) return;

        entity.LastSeenPath = path;
        entity.LastSeenAt = seenAt;
        await context.SaveChangesAsync();
    }

    public async Task UpdateValueAsync(int id, string newValue)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.Value = newValue;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    public async Task SaveChangesAsync() =>
        await context.SaveChangesAsync();

    public async Task ExecuteInTransactionAsync(Func<Task> work) =>
        await context.ExecuteInTransactionAsync(work);
}
