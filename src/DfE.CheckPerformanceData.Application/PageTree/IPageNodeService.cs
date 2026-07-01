namespace DfE.CheckPerformanceData.Application.PageTree;

// Orchestrates page-tree editing over IPageNodeRepository. Handles path computation, version seeding,
// working-draft targeting, publish scheduling, and soft-delete with children guard.
public interface IPageNodeService
{
    /// <summary>
    /// Creates a node, computing its path from parent + segment.
    /// Seeds an empty draft version for "content" and "wiki" types; "folder" gets no version.
    /// </summary>
    Task<PageNodeDto> CreatePageAsync(
        Guid? parentId, string segment, string title, string pageType, string? userId);

    /// <summary>Returns the node with <paramref name="id"/>, or null if not found or deleted.</summary>
    Task<PageNodeDto?> GetNodeByIdAsync(Guid id);

    /// <summary>All live (non-deleted) nodes. Caller assembles into a tree via ParentId links.</summary>
    Task<List<PageNodeTreeItemDto>> GetTreeAsync();

    /// <summary>
    /// Returns the node at <paramref name="path"/>, or null if not found.
    /// Unlike <see cref="GetLivePageAsync"/>, this does NOT require a live version,
    /// so it can resolve folder nodes (which are never versioned).
    /// </summary>
    Task<PageNodeDto?> GetNodeByPathAsync(string path);

    /// <summary>
    /// Resolves the node at <paramref name="path"/> and its currently-live version.
    /// Returns null if the path is not found or no version is live at <paramref name="nowUtc"/>.
    /// </summary>
    Task<LivePageResult?> GetLivePageAsync(string path, DateTime nowUtc);

    /// <summary>
    /// Edits the working (draft) version: the highest-VersionId version with no PublishFrom.
    /// Creates a new draft if every existing version is scheduled.
    /// </summary>
    Task SaveWorkingContentAsync(
        Guid nodeId, string content, string bodyPlainText, string? userId);

    /// <summary>
    /// Sets the publish window on version <paramref name="versionId"/> and recomputes IsCurrent.
    /// <paramref name="userId"/> is recorded as the publisher on the version row.
    /// </summary>
    Task PublishAsync(Guid nodeId, int versionId, DateTime? from, DateTime? to, string? userId);

    Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId);

    /// <summary>
    /// Returns the working draft's content if a draft exists (PublishFrom is null),
    /// otherwise returns the latest published version's content.
    /// Returns null if the node has no versions at all.
    /// </summary>
    Task<string?> GetWorkingOrLatestContentAsync(Guid nodeId);

    /// <summary>
    /// Soft-deletes the node. Throws if the node has children (safe default; caller must
    /// delete or reparent children first).
    /// </summary>
    Task DeleteAsync(Guid nodeId, string? userId);

    /// <summary>
    /// Moves the node up or down among its siblings by swapping <c>SortOrder</c> with the
    /// adjacent sibling in the requested <paramref name="direction"/> ("up" or "down").
    /// No-ops silently when the node is already at the end of the list.
    /// </summary>
    Task MoveAsync(Guid nodeId, string direction, string? userId);
}
