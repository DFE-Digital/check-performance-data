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

        // Use (SortOrder, CreatedDate) as the visible order so equal-SortOrder siblings
        // (common in seeded/legacy data where everything was 0) are still reorderable.
        var siblings = all
            .Where(n => n.ParentId == node.ParentId)
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.CreatedDate)
            .ToList();

        var idx = siblings.FindIndex(n => n.Id == nodeId);
        if (idx < 0) return; // unexpected: node not in tree

        var targetIdx = direction == "up" ? idx - 1 : idx + 1;

        // Already at the end in that direction → no-op.
        if (targetIdx < 0 || targetIdx >= siblings.Count) return;

        // Reorder the sibling list then assign zero-based SortOrders to all siblings so
        // the operation is idempotent and robust even when current SortOrders are all equal.
        var item = siblings[idx];
        siblings.RemoveAt(idx);
        siblings.Insert(targetIdx, item);

        var orders = siblings
            .Select((s, i) => (s.Id, SortOrder: i))
            .ToList();

        await repository.SetSiblingOrderAsync(orders);
    }

    public async Task PublishDraftAsync(Guid nodeId, string? userId)
    {
        var versions = await GetVersionsAsync(nodeId);
        // Draft = highest-VersionId version with no publish window.
        // GetVersionsAsync returns versions ordered descending by VersionId.
        var draft = versions.FirstOrDefault(v => v.PublishFrom is null);
        if (draft is null) return;
        await PublishAsync(nodeId, draft.VersionId, DateTime.UtcNow, null, userId);
    }

    public async Task UnpublishAsync(Guid nodeId, string? userId)
    {
        var versions = await GetVersionsAsync(nodeId);
        var live = versions.FirstOrDefault(v => v.IsCurrent);
        if (live is null) return;
        // Clear the publish window — the version becomes an unscheduled draft again.
        await PublishAsync(nodeId, live.VersionId, null, null, userId);
    }

    public async Task<bool> IsPublishedAsync(Guid nodeId)
    {
        var versions = await GetVersionsAsync(nodeId);
        return versions.Any(v => v.IsCurrent);
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
