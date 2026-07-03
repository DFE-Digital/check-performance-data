using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class PageNodeRepository(IPortalDbContext context) : IPageNodeRepository
{
    public Task<List<PageNodeTreeItemDto>> GetTreeAsync() =>
        context.PageNodes
            .AsNoTracking()
            .OrderBy(n => n.ParentId)
            .ThenBy(n => n.SortOrder)
            .ThenBy(n => n.CreatedDate)
            .Select(n => new PageNodeTreeItemDto
            {
                Id = n.Id,
                ParentId = n.ParentId,
                Segment = n.Segment,
                Path = n.Path,
                SortOrder = n.SortOrder,
                CreatedDate = n.CreatedDate,
                Title = n.Title,
                Subtitle = n.Subtitle,
                PageName = n.PageName,
                PageType = n.PageType,
                HasLiveVersion = n.Versions.Any(v => v.IsCurrent)
            })
            .ToListAsync();

    public Task<PageNodeDto?> GetByPathAsync(string path) =>
        context.PageNodes
            .AsNoTracking()
            .Where(n => n.Path == path)
            .Select(NodeProjection)
            .FirstOrDefaultAsync();

    public Task<List<PageSearchHitRaw>> SearchPagesAsync(string term, string? scopePath, int max)
    {
        // ILIKE via EF.Functions.ILike is Postgres-specific but this project pins Postgres
        // everywhere (Testcontainers + prod), so it's safe. Wildcards are added at the edges;
        // callers pass raw terms.
        var pattern = "%" + term + "%";
        var scopePrefix = string.IsNullOrEmpty(scopePath) ? null : scopePath + "/";

        var query = context.PageNodes
            .AsNoTracking()
            .Where(n => n.DeletedDate == null)
            .Where(n => scopePath == null || n.Path == scopePath || n.Path.StartsWith(scopePrefix!))
            .Select(n => new
            {
                Node = n,
                Live = n.Versions
                    .Where(v => v.IsCurrent)
                    .Select(v => new { v.BodyPlainText })
                    .FirstOrDefault()
            })
            .Where(x =>
                EF.Functions.ILike(x.Node.Title, pattern)
                || (x.Node.Subtitle != null && EF.Functions.ILike(x.Node.Subtitle, pattern))
                || (x.Live != null && EF.Functions.ILike(x.Live.BodyPlainText, pattern)))
            .OrderBy(x => x.Node.Path)
            .Take(max)
            .Select(x => new PageSearchHitRaw
            {
                PageId = x.Node.Id,
                Path = x.Node.Path,
                Title = x.Node.Title,
                Subtitle = x.Node.Subtitle,
                BodyPlainText = x.Live != null ? x.Live.BodyPlainText : string.Empty,
            });

        return query.ToListAsync();
    }

    public Task<PageNodeDto?> GetByIdAsync(Guid id) =>
        context.PageNodes
            .AsNoTracking()
            .Where(n => n.Id == id)
            .Select(NodeProjection)
            .FirstOrDefaultAsync();

    public async Task<PageNodeDto> CreateNodeAsync(
        Guid? parentId, string segment, string path, string title, string pageType, string? userId)
    {
        var now = DateTime.UtcNow;

        // New sibling gets max(existing sibling SortOrder) + 1, so siblings always have distinct
        // increasing orders and swap/position-reassign operations produce visible changes.
        var maxOrder = await context.PageNodes
            .Where(n => n.ParentId == parentId)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync() ?? -1;

        var entity = new PageNode
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Segment = segment,
            Path = path,
            SortOrder = maxOrder + 1,
            Title = title,
            PageType = pageType,
            CreatedDate = now,
            UpdatedDate = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };
        context.PageNodes.Add(entity);
        await context.SaveChangesAsync();
        return ToNodeDto(entity);
    }

    public async Task<int> AddVersionAsync(
        Guid nodeId, string content, string bodyPlainText,
        DateTime? publishFrom, DateTime? publishTo, string? userId)
    {
        var max = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .MaxAsync(v => (int?)v.VersionId) ?? 0;
        var versionId = max + 1;

        context.PageNodeVersions.Add(new PageNodeVersion
        {
            Id = Guid.NewGuid(),
            PageNodeId = nodeId,
            VersionId = versionId,
            MinorVersion = 1,               // all new versions are drafts
            Content = content,
            BodyPlainText = bodyPlainText,
            PublishFrom = publishFrom,
            PublishTo = publishTo,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            CreatedBy = userId,
            UpdatedBy = userId
        });
        await context.SaveChangesAsync();
        await RecomputeCurrentAsync(nodeId, DateTime.UtcNow);
        return versionId;
    }

    public async Task UpdateVersionContentAsync(
        Guid nodeId, int versionId, string content, string bodyPlainText, string? userId)
    {
        var entity = await context.PageNodeVersions
            .FirstOrDefaultAsync(v => v.PageNodeId == nodeId && v.VersionId == versionId)
            ?? throw new InvalidOperationException(
                $"Version {versionId} for node {nodeId} not found.");

        entity.Content = content;
        entity.BodyPlainText = bodyPlainText;
        entity.MinorVersion += 1;           // each working-draft save bumps the minor counter
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task UpdateVersionWindowAsync(
        Guid nodeId, int versionId, DateTime? publishFrom, DateTime? publishTo, string? userId)
    {
        var entity = await context.PageNodeVersions
            .FirstOrDefaultAsync(v => v.PageNodeId == nodeId && v.VersionId == versionId)
            ?? throw new InvalidOperationException(
                $"Version {versionId} for node {nodeId} not found.");

        entity.PublishFrom = publishFrom;
        entity.PublishTo = publishTo;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        // Publishing or scheduling zeros the minor counter → version becomes an integer version.
        // Unpublishing (publishFrom == null) leaves minor unchanged — it becomes a past draft again.
        if (publishFrom is not null)
            entity.MinorVersion = 0;
        await context.SaveChangesAsync();
    }

    public Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId) =>
        context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId)
            .OrderByDescending(v => v.VersionId)
            .Select(VersionProjection)
            .ToListAsync();

    public async Task<PageNodeVersionDto?> GetLiveVersionAsync(Guid nodeId, DateTime nowUtc)
    {
        var windows = await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId)
            .Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo))
            .ToListAsync();

        var liveId = LiveVersionResolver.Resolve(windows, nowUtc);
        if (liveId is null) return null;

        return await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId && v.VersionId == liveId)
            .Select(VersionProjection)
            .FirstOrDefaultAsync();
    }

    public async Task RecomputeCurrentAsync(Guid nodeId, DateTime nowUtc)
    {
        var windows = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo))
            .ToListAsync();

        var liveId = LiveVersionResolver.Resolve(windows, nowUtc);

        var all = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .ToListAsync();

        foreach (var v in all)
            v.IsCurrent = v.VersionId == liveId;

        await context.SaveChangesAsync();
    }

    public Task<bool> HasChildrenAsync(Guid nodeId) =>
        context.PageNodes.AnyAsync(n => n.ParentId == nodeId);

    public async Task SoftDeleteAsync(Guid nodeId, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(nodeId)
            ?? throw new InvalidOperationException($"Page node {nodeId} not found.");

        entity.DeletedDate = DateTime.UtcNow;
        entity.DeletedBy = userId;
        await context.SaveChangesAsync();
    }

    public Task<List<PageNodeDto>> GetDeletedAsync() =>
        context.PageNodes
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(n => n.DeletedDate != null)
            .OrderByDescending(n => n.DeletedDate)
            .Select(NodeProjection)
            .ToListAsync();

    public async Task RestoreAsync(Guid nodeId, string? userId)
    {
        var entity = await context.PageNodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == nodeId)
            ?? throw new InvalidOperationException($"Page node {nodeId} not found.");

        entity.DeletedDate = null;
        entity.DeletedBy = null;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task SwapSortOrderAsync(Guid nodeId, Guid otherNodeId)
    {
        // FindAsync bypasses the global query filter — that is intentional here, since the
        // controller has already confirmed both nodes are live before calling this method.
        var nodeA = await context.PageNodes.FindAsync(nodeId)
            ?? throw new InvalidOperationException($"Page node {nodeId} not found.");
        var nodeB = await context.PageNodes.FindAsync(otherNodeId)
            ?? throw new InvalidOperationException($"Page node {otherNodeId} not found.");

        (nodeA.SortOrder, nodeB.SortOrder) = (nodeB.SortOrder, nodeA.SortOrder);
        await context.SaveChangesAsync();
    }

    public async Task SetSiblingOrderAsync(IReadOnlyList<(Guid Id, int SortOrder)> orders)
    {
        await context.ExecuteInTransactionAsync(async () =>
        {
            foreach (var (id, sortOrder) in orders)
            {
                var node = await context.PageNodes.FindAsync(id)
                    ?? throw new InvalidOperationException($"Page node {id} not found.");
                node.SortOrder = sortOrder;
            }
            await context.SaveChangesAsync();
        });
    }

    public Task ExecuteInTransactionAsync(Func<Task> work) =>
        context.ExecuteInTransactionAsync(work);

    public async Task RenameNodeAndCascadeAsync(
        Guid id, string newSegment, string newPath, string newTitle, string? newSubtitle, string? newPageName, string? userId)
    {
        await context.ExecuteInTransactionAsync(async () =>
        {
            var node = await context.PageNodes.FindAsync(id)
                ?? throw new InvalidOperationException($"Page node {id} not found.");

            var oldPath = node.Path;
            var now = DateTime.UtcNow;

            node.Segment = newSegment;
            node.Path = newPath;
            node.Title = newTitle;
            node.Subtitle = newSubtitle;
            node.PageName = newPageName;
            node.UpdatedDate = now;
            node.UpdatedBy = userId;

            // Only cascade when the path actually changed.
            if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                var oldPrefix = oldPath + "/";
                var descendants = await context.PageNodes
                    .Where(n => n.Id != id && n.Path.StartsWith(oldPrefix))
                    .ToListAsync();

                foreach (var d in descendants)
                {
                    d.Path = newPath + "/" + d.Path[oldPrefix.Length..];
                    d.UpdatedDate = now;
                    d.UpdatedBy = userId;
                }
            }

            await context.SaveChangesAsync();
        });
    }

    public async Task SetPageTypeAsync(Guid id, string pageType, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.PageType = pageType;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    // ── Staging (import) ────────────────────────────────────────────────────

    public async Task<PageNodeDto> CreateNodeForStagingAsync(
        Guid id, Guid? parentId, string segment, string path,
        string title, string? subtitle, string? pageName, string pageType, int sortOrder, string? userId)
    {
        var now = DateTime.UtcNow;
        var entity = new PageNode
        {
            Id = id,
            ParentId = parentId,
            Segment = segment,
            Path = path,
            SortOrder = sortOrder,
            Title = title,
            Subtitle = subtitle,
            PageName = pageName,
            PageType = pageType,
            CreatedDate = now,
            UpdatedDate = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };
        context.PageNodes.Add(entity);
        await context.SaveChangesAsync();
        return ToNodeDto(entity);
    }

    public async Task UpdateNodeForStagingAsync(
        Guid id, string segment, string path, string title, string? subtitle, string? pageName, int sortOrder, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.Segment = segment;
        entity.Path = path;
        entity.Title = title;
        entity.Subtitle = subtitle;
        entity.PageName = pageName;
        entity.SortOrder = sortOrder;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task ReplaceAllVersionsForStagingAsync(
        Guid nodeId, IReadOnlyList<PageNodeVersionDto> versions, string? userId)
    {
        var now = DateTime.UtcNow;

        // Wipe existing versions in one round-trip; then add the bundle versions with their
        // original identities. IsCurrent is recomputed from windows below.
        var existing = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .ToListAsync();
        context.PageNodeVersions.RemoveRange(existing);

        foreach (var v in versions)
        {
            context.PageNodeVersions.Add(new PageNodeVersion
            {
                Id = Guid.NewGuid(),
                PageNodeId = nodeId,
                VersionId = v.VersionId,
                MinorVersion = v.MinorVersion,
                Content = v.Content,
                BodyPlainText = v.BodyPlainText,
                PublishFrom = v.PublishFrom,
                PublishTo = v.PublishTo,
                IsCurrent = false,      // RecomputeCurrentAsync sets this from windows
                CreatedDate = now,
                UpdatedDate = now,
                CreatedBy = userId,
                UpdatedBy = userId
            });
        }
        await context.SaveChangesAsync();
        await RecomputeCurrentAsync(nodeId, now);
    }

    // ── projections ──────────────────────────────────────────────────────────

    private static readonly System.Linq.Expressions.Expression<Func<PageNode, PageNodeDto>> NodeProjection =
        n => new PageNodeDto
        {
            Id = n.Id,
            ParentId = n.ParentId,
            Segment = n.Segment,
            Path = n.Path,
            SortOrder = n.SortOrder,
            Title = n.Title,
            Subtitle = n.Subtitle,
            PageName = n.PageName,
            PageType = n.PageType,
            DeletedDate = n.DeletedDate,
            DeletedBy = n.DeletedBy,
        };

    private static readonly System.Linq.Expressions.Expression<Func<PageNodeVersion, PageNodeVersionDto>> VersionProjection =
        v => new PageNodeVersionDto
        {
            Id = v.Id,
            VersionId = v.VersionId,
            MinorVersion = v.MinorVersion,
            IsCurrent = v.IsCurrent,
            PublishFrom = v.PublishFrom,
            PublishTo = v.PublishTo,
            Content = v.Content,
            BodyPlainText = v.BodyPlainText,
            CreatedDate = v.CreatedDate,
            CreatedBy = v.CreatedBy,
            UpdatedDate = v.UpdatedDate,
            UpdatedBy = v.UpdatedBy
        };

    private static PageNodeDto ToNodeDto(PageNode n) => new()
    {
        Id = n.Id,
        ParentId = n.ParentId,
        Segment = n.Segment,
        Path = n.Path,
        SortOrder = n.SortOrder,
        Title = n.Title,
        Subtitle = n.Subtitle,
        PageName = n.PageName,
        PageType = n.PageType
    };
}
