using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// The page and content block content that the embedded-image move reads and changes. "Current" is the one rule in
// PageVersionStates (live, scheduled and the working draft), as the media library uses elsewhere.
//
// A current version changes in place. A system rewrite that leaves the page looking the same adds
// no version, as the media usage rebuild and a replaced image do: a new version would reorder the
// history and could not sit well between a live version, a scheduled one and a draft. A draft
// saved here bumps its minor number, as an editor's save does; a published or scheduled version
// keeps its number and window. The generic audit capture records each version's old and new
// content, and this store adds one readable entry for the page.
public sealed class EmbeddedImageStore(IPortalDbContext context) : IEmbeddedImageStore
{
    private const string DataUriPattern = "%data:%";
    private const string DataImagePattern = "%data:image%";

    // Read back by the audit log with the same (camelCase) options.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<EmbeddedImagePage>> ListPagesAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var current = PageVersionStates.CurrentVersionIds(context, nowUtc);
        var pages = await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => current.Contains(v.Id) && EF.Functions.ILike(v.Content, DataUriPattern))
            .Select(v => new { v.PageNodeId, v.PageNode.Title, v.PageNode.Path, v.PageNode.PageType })
            .Distinct()
            .ToListAsync(ct);

        return pages
            .OrderBy(p => p.Path, StringComparer.Ordinal)
            .Select(p => new EmbeddedImagePage(p.PageNodeId, p.Title, p.Path, p.PageType))
            .ToList();
    }

    public async Task<IReadOnlyList<EmbeddedImageVersion>> GetCurrentVersionsAsync(Guid nodeId, DateTime nowUtc, CancellationToken ct = default)
    {
        var current = PageVersionStates.CurrentVersionIds(context, nowUtc);
        var versions = await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId && current.Contains(v.Id) && EF.Functions.ILike(v.Content, DataUriPattern))
            .OrderBy(v => v.VersionId)
            .Select(v => new { v.Id, v.VersionId, v.PublishFrom, v.Content })
            .ToListAsync(ct);

        return versions
            .Select(v => new EmbeddedImageVersion(v.Id, v.VersionId, State(v.PublishFrom, nowUtc), v.Content))
            .ToList();
    }

    public Task<int> CountOlderVersionsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var current = PageVersionStates.CurrentVersionIds(context, nowUtc);
        return context.PageNodeVersions
            .CountAsync(v => !current.Contains(v.Id) && EF.Functions.ILike(v.Content, DataImagePattern), ct);
    }

    // Only a Content block shows its value as rich text, so only its images show.
    public async Task<IReadOnlyList<EmbeddedImageBlock>> ListContentBlocksAsync(CancellationToken ct = default) =>
        await context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.BlockType == "Content" && EF.Functions.ILike(b.Value, DataUriPattern))
            .OrderBy(b => b.Key)
            .Select(b => new EmbeddedImageBlock(b.Id, b.Key, b.Value))
            .ToListAsync(ct);

    // A block saved here gets a new version, as an editor's save does, so its history shows the
    // change and a revert can go back to the embedded images. Its plain text does not change.
    public async Task<bool> SaveContentBlockAsync(EmbeddedImageBlockSave save, string? userId, CancellationToken ct = default)
    {
        try
        {
            var block = await context.ContentBlocks.FirstOrDefaultAsync(b => b.Id == save.Block.Id, ct);
            if (block is null || block.Value != save.Block.Content) return false;

            var now = DateTime.UtcNow;
            block.Value = save.Content;
            block.UpdatedAt = now;
            block.UpdatedBy = userId;
            var latest = await context.ContentBlockVersions
                .Where(v => v.ContentBlockId == block.Id)
                .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;
            context.ContentBlockVersions.Add(new ContentBlockVersion
            {
                ContentBlockId = block.Id,
                Value = save.Content,
                VersionNumber = latest + 1,
                CreatedAt = now,
                CreatedBy = userId,
            });

            await MediaUsageWriter.StageBlockAsync(context, block, ct);

            context.AuditEntries.Add(new AuditEntry
            {
                EntityType = AuditActivities.EmbeddedImageMove,
                // The block id, so the entry can be matched to the block's own audit rows.
                EntityId = block.Id.ToString(),
                Action = AuditActivities.EmbeddedImageContentBlockChangedAction,
                Timestamp = now,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    ContentBlockId = block.Id,
                    block.Key,
                    Version = latest + 1,
                    save.ImagesMoved,
                    save.AssetIds
                }, AuditJson)
            });

            await context.SaveChangesAsync(ct);
            return true;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public async Task<bool> SavePageAsync(EmbeddedImagePageSave save, string? userId, CancellationToken ct = default)
    {
        try
        {
            var ids = save.Versions.Select(v => v.VersionRowId).ToList();
            var entities = await context.PageNodeVersions
                .Where(v => v.PageNodeId == save.Page.NodeId && ids.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            // Every version must still hold what was read, or the page is left for the next run.
            if (save.Versions.Any(change =>
                    !entities.TryGetValue(change.VersionRowId, out var entity) || entity.Content != change.ExpectedContent))
                return false;

            var now = DateTime.UtcNow;
            foreach (var change in save.Versions)
            {
                var entity = entities[change.VersionRowId];
                entity.Content = change.Content;
                if (entity.PublishFrom is null) entity.MinorVersion += 1;
                entity.UpdatedDate = now;
                entity.UpdatedBy = userId;
            }

            await MediaUsageWriter.StageAsync(context,
                entities.Values.Select(e => new MediaUsageWriter.VersionContent(e.Id, e.PageNodeId, e.Content)).ToList(),
                keepPins: false, ct);

            context.AuditEntries.Add(new AuditEntry
            {
                EntityType = AuditActivities.EmbeddedImageMove,
                // The page id, so the entry can be matched to the page's own audit rows.
                EntityId = save.Page.NodeId.ToString(),
                Action = AuditActivities.EmbeddedImagePageChangedAction,
                Timestamp = now,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    PageNodeId = save.Page.NodeId,
                    save.Page.Path,
                    Versions = save.Versions
                        .Select(v => entities[v.VersionRowId])
                        .Select(e => new { e.VersionId, State = State(e.PublishFrom, now).ToString() }),
                    save.ImagesMoved,
                    save.AssetIds
                }, AuditJson)
            });

            await context.SaveChangesAsync(ct);
            return true;
        }
        finally
        {
            // A page's content can be large. Nothing tracked is needed after the save, and a
            // failed save must not leave changes that a later save would write.
            context.ChangeTracker.Clear();
        }
    }

    public async Task RecordRunAsync(Guid runId, EmbeddedImageMoveResult result, string? userId, CancellationToken ct = default)
    {
        context.AuditEntries.Add(new AuditEntry
        {
            EntityType = AuditActivities.EmbeddedImageMove,
            EntityId = runId.ToString(),
            Action = AuditActivities.EmbeddedImageRunAction,
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            NewValues = JsonSerializer.Serialize(new
            {
                RunId = runId,
                result.PagesChanged,
                result.ImagesMoved,
                result.ImagesCreated,
                result.DuplicatesReused,
                PagesFailed = result.Failures.Count,
                FailedPageIds = result.Failures.Select(f => f.Page.NodeId),
                result.BlocksChanged,
                BlocksFailed = result.BlockFailures.Count,
                FailedBlockIds = result.BlockFailures.Select(f => f.Block.Id)
            }, AuditJson)
        });
        await context.SaveChangesAsync(ct);
    }

    private static EmbeddedVersionState State(DateTime? publishFrom, DateTime nowUtc) =>
        publishFrom is null ? EmbeddedVersionState.Draft
        : publishFrom > nowUtc ? EmbeddedVersionState.Scheduled
        : EmbeddedVersionState.Live;
}
