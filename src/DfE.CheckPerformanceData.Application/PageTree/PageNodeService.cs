namespace DfE.CheckPerformanceData.Application.PageTree;

// Orchestrates page-tree operations: path computation on create, version seeding, working-draft
// resolution, publish scheduling, and soft-delete with children guard.
public sealed class PageNodeService(IPageNodeRepository repository) : IPageNodeService
{
    // Empty content for a brand-new "content" page — matches ContentPageService.EmptyTree ("[]").
    private const string EmptyContentTree = "[]";

    public async Task<PageNodeDto> CreatePageAsync(
        Guid? parentId, string segment, string title, string pageType, string? userId)
    {
        var path = await ComputePathAsync(parentId, segment);
        var node = await repository.CreateNodeAsync(parentId, segment, path, title, pageType, userId);

        if (pageType is "content" or "wiki")
        {
            var emptyContent = pageType == "content" ? EmptyContentTree : string.Empty;
            await repository.AddVersionAsync(node.Id, emptyContent, string.Empty, null, null, userId);
        }

        return node;
    }

    public Task<PageNodeDto?> GetNodeByIdAsync(Guid id) =>
        repository.GetByIdAsync(id);

    public Task<List<PageNodeTreeItemDto>> GetTreeAsync() =>
        repository.GetTreeAsync();

    public Task<PageNodeDto?> GetNodeByPathAsync(string path) =>
        repository.GetByPathAsync(path);

    public async Task<LivePageResult?> GetLivePageAsync(string path, DateTime nowUtc)
    {
        var node = await repository.GetByPathAsync(path);
        if (node is null) return null;

        var version = await repository.GetLiveVersionAsync(node.Id, nowUtc);
        if (version is null) return null;

        return new LivePageResult { Node = node, Version = version };
    }

    public async Task SaveWorkingContentAsync(
        Guid nodeId, string content, string bodyPlainText, string? userId)
    {
        var versions = await repository.GetVersionsAsync(nodeId);

        // Working version = highest-VersionId version with PublishFrom null (never scheduled).
        // GetVersionsAsync returns versions ordered descending by VersionId.
        var working = versions.FirstOrDefault(v => v.PublishFrom is null);

        if (working is not null)
            await repository.UpdateVersionContentAsync(nodeId, working.VersionId, content, bodyPlainText, userId);
        else
            await repository.AddVersionAsync(nodeId, content, bodyPlainText, null, null, userId);
    }

    public async Task PublishAsync(Guid nodeId, int versionId, DateTime? from, DateTime? to, string? userId)
    {
        await repository.ExecuteInTransactionAsync(async () =>
        {
            await repository.UpdateVersionWindowAsync(nodeId, versionId, from, to, userId);
            await repository.RecomputeCurrentAsync(nodeId, DateTime.UtcNow);
        });
    }

    public Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId) =>
        repository.GetVersionsAsync(nodeId);

    public async Task<string?> GetWorkingOrLatestContentAsync(Guid nodeId)
    {
        var versions = await GetVersionsAsync(nodeId);
        // Draft = highest-VersionId version with no publish window (unscheduled).
        // GetVersionsAsync returns versions ordered descending by VersionId.
        var draft = versions.FirstOrDefault(v => v.PublishFrom is null);
        if (draft is not null)
            return draft.Content;
        // No draft: return the latest published version's content (first in desc-ordered list).
        return versions.FirstOrDefault()?.Content;
    }

    public async Task DeleteAsync(Guid nodeId, string? userId)
    {
        if (await repository.HasChildrenAsync(nodeId))
            throw new InvalidOperationException(
                $"Cannot delete page node {nodeId}: it still has child nodes. " +
                "Delete or reparent the children first.");

        await repository.SoftDeleteAsync(nodeId, userId);
    }

    public async Task MoveAsync(Guid nodeId, string direction)
    {
        var node = await repository.GetByIdAsync(nodeId);
        if (node is null) return; // node does not exist; caller should have validated

        var all = await repository.GetTreeAsync();
        var siblings = all
            .Where(n => n.ParentId == node.ParentId)
            .OrderBy(n => n.SortOrder)
            .ToList();

        var idx = siblings.FindIndex(n => n.Id == nodeId);
        if (idx < 0) return; // unexpected: node not in tree

        PageNodeTreeItemDto? sibling = direction == "up"
            ? (idx > 0 ? siblings[idx - 1] : null)
            : (idx < siblings.Count - 1 ? siblings[idx + 1] : null);

        if (sibling is null) return; // already at the end in that direction

        await repository.SwapSortOrderAsync(nodeId, sibling.Id);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<string> ComputePathAsync(Guid? parentId, string segment)
    {
        if (parentId is null)
            return segment;

        var parent = await repository.GetByIdAsync(parentId.Value)
            ?? throw new InvalidOperationException(
                $"Parent page node {parentId} not found.");

        return parent.Path + "/" + segment;
    }
}
