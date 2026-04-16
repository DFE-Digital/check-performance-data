using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class ContentBlockRepository(IPortalDbContext context) : IContentBlockRepository
{
    // Queries — use ProjectToDto so EF translates the mapping to SQL

    public async Task<ContentBlockDto?> GetByKeyAsync(string key) =>
        await context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.Key == key)
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

    public async Task<ContentBlockDto> AddBlockAsync(string key, string blockType, string value)
    {
        var entity = new ContentBlock
        {
            Key = key,
            BlockType = blockType,
            Value = value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.ContentBlocks.Add(entity);
        await context.SaveChangesAsync();

        return ContentBlockMapper.ToDto(entity);
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

    public async Task UpdateValueAsync(int id, string newValue)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.Value = newValue;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    public async Task SaveChangesAsync() =>
        await context.SaveChangesAsync();

    public async Task BeginTransactionAsync() =>
        await context.BeginTransactionAsync();

    public async Task CommitTransactionAsync() =>
        await context.CommitTransactionAsync();

    public async Task RollbackTransactionAsync() =>
        await context.RollbackTransactionAsync();
}
