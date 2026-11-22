using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// The page and content block content that the embedded-image move reads and changes. "Current" is the one rule in
// PageVersionStates (live, scheduled and the working draft), as the media library uses elsewhere.
//
// A page change adds versions, as every page change does. Each current version that changes gets a
// new version with the next number: a live version a new published version, a scheduled version a
// new scheduled version, both on the same window, and the draft a new draft. The old version is
// marked as superseded by the new one (SupersededById). It stays in the history with its content,
// but is never live, scheduled or the working draft again, even when an editor re-dates or
// unschedules the new version. The generic audit capture records the new versions, and this store
// adds one readable entry for the page.
public sealed class EmbeddedImageStore(IPortalDbContext context) : IEmbeddedImageStore
{
    private const string DataUriPattern = "%data:%";
    private const string DataImagePattern = "%data:image%";
    private const string VersionNumberIndex = "IX_PageNodeVersions_PageNodeId_VersionId";

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
            var now = DateTime.UtcNow;
            var nodeId = save.Page.NodeId;
            var ids = save.Versions.Select(v => v.VersionRowId).ToList();
            var current = PageVersionStates.CurrentVersionIds(context, now);
            var sources = await context.PageNodeVersions
                .Where(v => v.PageNodeId == nodeId && ids.Contains(v.Id) && current.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            // Every version must still be current and hold what was read, or the page is left for
            // the next run. An editor who saved or published the page meanwhile wins.
            if (save.Versions.Any(change =>
                    !sources.TryGetValue(change.VersionRowId, out var source) || source.Content != change.ExpectedContent))
                return false;

            var windows = await context.PageNodeVersions
                .AsNoTracking()
                .Where(v => v.PageNodeId == nodeId)
                .Select(v => new { v.Id, v.VersionId, v.PublishFrom, v.PublishTo, v.IsCurrent, Superseded = v.SupersededById != null })
                .ToListAsync(ct);

            // New versions are numbered in the order of the versions they replace, so a new
            // draft stays above a new live version, as the editor expects.
            var next = windows.Max(w => w.VersionId);
            var changes = new List<(PageNodeVersion Old, PageNodeVersion New, EmbeddedVersionState State)>();
            foreach (var change in save.Versions.OrderBy(c => sources[c.VersionRowId].VersionId))
            {
                var old = sources[change.VersionRowId];
                var copy = new PageNodeVersion
                {
                    Id = Guid.NewGuid(),
                    PageNodeId = nodeId,
                    VersionId = ++next,
                    // A draft is a new draft (minor 1, as AddVersionAsync). A published or
                    // scheduled version is published (minor 0) on the same window.
                    MinorVersion = old.PublishFrom is null ? 1 : 0,
                    PublishFrom = old.PublishFrom,
                    PublishTo = old.PublishTo,
                    Content = change.Content,
                    BodyPlainText = old.BodyPlainText,
                    CreatedDate = now,
                    UpdatedDate = now,
                    // The person's name, as an editor's save records it. The audit entry keeps their id.
                    CreatedBy = save.ModifiedBy,
                    UpdatedBy = save.ModifiedBy
                };
                context.PageNodeVersions.Add(copy);
                old.SupersededById = copy.Id;
                changes.Add((old, copy, State(old.PublishFrom, now)));
            }

            SetLiveFlags(nodeId, now, sources,
                windows.Select(w => (w.Id, w.VersionId, w.PublishFrom, w.PublishTo, w.IsCurrent,
                    w.Superseded || sources.ContainsKey(w.Id))).ToList(),
                changes.Select(c => c.New).ToList());

            await MediaUsageWriter.StageAsync(context,
                changes.Select(c => new MediaUsageWriter.VersionContent(c.New.Id, nodeId, c.New.Content)).ToList(),
                keepPins: false, ct);

            context.AuditEntries.Add(new AuditEntry
            {
                EntityType = AuditActivities.EmbeddedImageMove,
                // The page id, so the entry can be matched to the page's own audit rows.
                EntityId = nodeId.ToString(),
                Action = AuditActivities.EmbeddedImagePageChangedAction,
                Timestamp = now,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    PageNodeId = nodeId,
                    save.Page.Path,
                    Versions = changes.Select(c => new
                    {
                        State = c.State.ToString(),
                        OldVersionId = c.Old.VersionId,
                        OldId = c.Old.Id,
                        NewVersionId = c.New.VersionId,
                        NewId = c.New.Id
                    }),
                    save.ImagesMoved,
                    save.AssetIds
                }, AuditJson)
            });

            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: VersionNumberIndex })
        {
            // An editor added a version with the same number meanwhile. The next run tries again.
            return false;
        }
        finally
        {
            // A page's content can be large. Nothing tracked is needed after the save, and a
            // failed save must not leave changes that a later save would write.
            context.ChangeTracker.Clear();
        }
    }

    // IsCurrent caches which version is live, as RecomputeCurrentAsync does after a publish. Only
    // the flag changes on an old version. A version that is not loaded is updated by its key alone.
    private void SetLiveFlags(
        Guid nodeId, DateTime now, IReadOnlyDictionary<Guid, PageNodeVersion> loaded,
        IReadOnlyList<(Guid Id, int VersionId, DateTime? PublishFrom, DateTime? PublishTo, bool IsCurrent, bool Superseded)> existing,
        IReadOnlyList<PageNodeVersion> added)
    {
        var liveId = LiveVersionResolver.Resolve(
            existing.Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo, v.Superseded))
                .Concat(added.Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo))),
            now);

        foreach (var version in added)
            version.IsCurrent = version.VersionId == liveId;

        foreach (var version in existing.Where(v => v.IsCurrent != (v.VersionId == liveId)))
        {
            if (!loaded.TryGetValue(version.Id, out var entity))
            {
                entity = new PageNodeVersion { Id = version.Id, PageNodeId = nodeId, IsCurrent = version.IsCurrent };
                context.PageNodeVersions.Attach(entity);
            }
            entity.IsCurrent = !version.IsCurrent;
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
