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

    public Task RecordLastSeenAsync(string key, string path) =>
        repository.SetLastSeenAsync(key, path, DateTime.UtcNow);

    public async Task<ContentBlockDto> EnsureAsync(string key, string blockType, string defaultValue, string path)
    {
        var existing = await repository.GetByKeyAsync(key);
        if (existing is null)
        {
            ContentBlockDto? created = null;
            await repository.ExecuteInTransactionAsync(async () =>
            {
                created = await repository.AddBlockAsync(key, blockType, defaultValue, PlainTextOf(defaultValue));
                await repository.AddVersionAsync(created.Id, defaultValue, 1);
                await repository.SetLastSeenAsync(key, path, DateTime.UtcNow);
            });
            return EnrichDto(created!);
        }

        if (!string.Equals(existing.LastSeenPath, path, StringComparison.Ordinal))
        {
            await repository.SetLastSeenAsync(key, path, DateTime.UtcNow);
        }
        return EnrichDto(existing);
    }

    public async Task<List<ContentBlockDto>> GetAllAsync()
    {
        var blocks = await repository.GetAllAsync();
        return blocks.Select(EnrichDto).ToList();
    }

    public async Task<ContentBlockDto> SaveAsync(SaveContentBlockDto dto)
    {
        var existing = await repository.GetByKeyAsync(dto.Key);
        var normalisedKeywords = string.IsNullOrWhiteSpace(dto.Keywords) ? null : dto.Keywords.Trim();

        if (existing == null)
        {
            ContentBlockDto? created = null;
            var hasBaseline = !string.IsNullOrEmpty(dto.OriginalValue) && dto.OriginalValue != dto.Value;
            var plain = PlainTextOf(dto.Value);

            await repository.ExecuteInTransactionAsync(async () =>
            {
                created = await repository.AddBlockAsync(
                    dto.Key, dto.BlockType, dto.Value, plain,
                    appearInSearch: dto.AppearInSearch,
                    keywords: normalisedKeywords);
                if (hasBaseline)
                {
                    await repository.AddVersionAsync(created.Id, dto.OriginalValue!, 1);
                    await repository.AddVersionAsync(created.Id, dto.Value, 2);
                }
                else
                {
                    await repository.AddVersionAsync(created.Id, dto.Value, 1);
                }
            });
            return EnrichDto(created!);
        }

        // AppearInSearch + Keywords are metadata — persist a change to them even when the value
        // is unchanged, so the editor can toggle them on their own without editing content too.
        if (existing.AppearInSearch != dto.AppearInSearch)
        {
            await repository.SetAppearInSearchAsync(existing.Id, dto.AppearInSearch);
        }
        if (!string.Equals(existing.Keywords, normalisedKeywords, StringComparison.Ordinal))
        {
            await repository.SetKeywordsAsync(existing.Id, normalisedKeywords);
        }

        if (existing.Value == dto.Value)
        {
            var reloaded = await repository.GetByKeyAsync(dto.Key) ?? existing;
            return EnrichDto(reloaded);
        }

        await repository.ExecuteInTransactionAsync(async () =>
        {
            var maxVersion = await repository.GetMaxVersionNumberAsync(existing.Id);
            await repository.UpdateValueAsync(existing.Id, dto.Value, PlainTextOf(dto.Value));

            if (maxVersion == 0)
            {
                await repository.AddVersionAsync(existing.Id, existing.Value, 1);
                await repository.AddVersionAsync(existing.Id, dto.Value, 2);
            }
            else
            {
                await repository.AddVersionAsync(existing.Id, dto.Value, maxVersion + 1);
            }
        });

        var updated = await repository.GetByKeyAsync(dto.Key)
            ?? throw new InvalidOperationException($"Content block '{dto.Key}' not found after update.");

        return EnrichDto(updated);
    }

    public async Task<List<ContentBlockVersionDto>> GetVersionsAsync(string key)
    {
        var versions = await repository.GetVersionsByKeyAsync(key);

        return versions.Select(v => new ContentBlockVersionDto
        {
            Id = v.Id,
            VersionNumber = v.VersionNumber,
            Value = v.Value,
            ValueHtml = htmlRenderer.RenderHtml(v.Value),
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        }).ToList();
    }

    public async Task<ContentBlockDto> RevertToVersionAsync(string key, int versionId)
    {
        var block = await repository.GetByKeyAsync(key)
            ?? throw new InvalidOperationException($"Content block '{key}' not found.");

        var version = await repository.GetVersionByIdAsync(versionId)
            ?? throw new InvalidOperationException($"Content block version {versionId} not found.");

        await repository.UpdateValueAsync(block.Id, version.Value, PlainTextOf(version.Value));

        var maxVersion = await repository.GetMaxVersionNumberAsync(block.Id);
        await repository.AddVersionAsync(block.Id, version.Value, maxVersion + 1);

        var updated = await repository.GetByKeyAsync(key)
            ?? throw new InvalidOperationException($"Content block '{key}' not found after revert.");

        return EnrichDto(updated);
    }

    private ContentBlockDto EnrichDto(ContentBlockDto block) => new()
    {
        Id = block.Id,
        ContentId = block.ContentId,
        Key = block.Key,
        BlockType = block.BlockType,
        Value = block.Value,
        ValueHtml = block.BlockType == "Content" ? htmlRenderer.RenderHtml(block.Value) : null,
        LastSeenPath = block.LastSeenPath,
        LastSeenAt = block.LastSeenAt,
        AppearInSearch = block.AppearInSearch,
        Keywords = block.Keywords,
        CreatedAt = block.CreatedAt,
        UpdatedAt = block.UpdatedAt
    };

    // Render → strip in one call so ValuePlainText mirrors the exact plaintext an end user
    // would see (script/style already gone, entities decoded). Callers store it alongside
    // Value so the SearchVector generated column has clean word tokens to index.
    private string PlainTextOf(string? html) =>
        htmlRenderer.StripTagsToPlainText(htmlRenderer.RenderHtml(html));
}
