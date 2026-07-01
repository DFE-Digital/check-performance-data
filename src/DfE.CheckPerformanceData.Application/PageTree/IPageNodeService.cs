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
    /// Soft-deletes the node. Throws if the node has children (safe default; caller must
    /// delete or reparent children first).
    /// </summary>
    Task DeleteAsync(Guid nodeId, string? userId);
}
