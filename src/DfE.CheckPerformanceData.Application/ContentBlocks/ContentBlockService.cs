using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockService(
    IContentBlockRepository repository,
    IHtmlRenderingService htmlRenderer) : IContentBlockService
{
    public async Task<ContentBlockDto?> GetByKeyAsync(string key)
    {
        var block = await repository.GetByKeyAsync(key);
        return block == null ? null : EnrichDto(block);
    }

    public async Task<ContentBlockDto> SaveAsync(SaveContentBlockDto dto)
    {
        var existing = await repository.GetByKeyAsync(dto.Key);

        if (existing == null)
        {
            await repository.BeginTransactionAsync();
            try
            {
                var block = await repository.AddBlockAsync(dto.Key, dto.BlockType, dto.Value);
                await repository.AddVersionAsync(block.Id, dto.Value, 1);
                await repository.CommitTransactionAsync();
                return EnrichDto(block);
            }
            catch
            {
                await repository.RollbackTransactionAsync();
                throw;
            }
        }

        if (existing.Value == dto.Value)
        {
            return EnrichDto(existing);
        }

        await repository.UpdateValueAsync(existing.Id, dto.Value);

        var maxVersion = await repository.GetMaxVersionNumberAsync(existing.Id);
        await repository.AddVersionAsync(existing.Id, dto.Value, maxVersion + 1);

        var updated = await repository.GetByKeyAsync(dto.Key)
            ?? throw new InvalidOperationException($"Content block '{dto.Key}' not found after update.");

        return EnrichDto(updated);
    }

    public async Task<List<ContentBlockVersionDto>> GetVersionsAsync(string key)
    {
        return await repository.GetVersionsByKeyAsync(key);
    }

    public async Task<ContentBlockDto> RevertToVersionAsync(string key, int versionId)
    {
        var block = await repository.GetByKeyAsync(key)
            ?? throw new InvalidOperationException($"Content block '{key}' not found.");

        var version = await repository.GetVersionByIdAsync(versionId)
            ?? throw new InvalidOperationException($"Content block version {versionId} not found.");

        await repository.UpdateValueAsync(block.Id, version.Value);

        var maxVersion = await repository.GetMaxVersionNumberAsync(block.Id);
        await repository.AddVersionAsync(block.Id, version.Value, maxVersion + 1);

        var updated = await repository.GetByKeyAsync(key)
            ?? throw new InvalidOperationException($"Content block '{key}' not found after revert.");

        return EnrichDto(updated);
    }

    private ContentBlockDto EnrichDto(ContentBlockDto block) => new()
    {
        Id = block.Id,
        Key = block.Key,
        BlockType = block.BlockType,
        Value = block.Value,
        ValueHtml = block.BlockType == "Content" ? htmlRenderer.RenderHtml(block.Value) : null
    };
}
