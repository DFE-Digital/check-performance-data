using DfE.CheckPerformanceData.Application.PageTree;
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
            PageType = n.PageType
        };

    private static readonly System.Linq.Expressions.Expression<Func<PageNodeVersion, PageNodeVersionDto>> VersionProjection =
        v => new PageNodeVersionDto
        {
            Id = v.Id,
            VersionId = v.VersionId,
            IsCurrent = v.IsCurrent,
            PublishFrom = v.PublishFrom,
            PublishTo = v.PublishTo,
            Content = v.Content,
            CreatedDate = v.CreatedDate,
            CreatedBy = v.CreatedBy,
            UpdatedDate = v.UpdatedDate
        };

    private static PageNodeDto ToNodeDto(PageNode n) => new()
    {
        Id = n.Id,
        ParentId = n.ParentId,
        Segment = n.Segment,
        Path = n.Path,
        SortOrder = n.SortOrder,
        Title = n.Title,
        PageType = n.PageType
    };
}
