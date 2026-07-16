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

    /// <summary>Soft-deleted pages listed for the admin "Deleted pages" screen.</summary>
    Task<List<PageNodeDto>> GetDeletedAsync();

    /// <summary>Restores a soft-deleted page (clears DeletedDate). No-op if already live.</summary>
    Task RestoreAsync(Guid nodeId, string? userId);

    /// <summary>
    /// Reparents <paramref name="nodeId"/> under <paramref name="newParentId"/> (null = root)
    /// and positions it at <paramref name="newSortOrder"/> among its new siblings. Handles the
    /// path cascade for descendants and reorders the affected sibling sets.
    /// </summary>
    Task<MoveNodeResult> MoveNodeAsync(
        Guid nodeId, Guid? newParentId, int newSortOrder, string? userId);

    /// <summary>Sets the public menu visibility flag on <paramref name="nodeId"/>.</summary>
    Task SetShowInMenuAsync(Guid nodeId, bool showInMenu, string? userId);

    /// <summary>Sets the search visibility flag on <paramref name="nodeId"/>.</summary>
    Task SetAppearInSearchAsync(Guid nodeId, bool appearInSearch, string? userId);

    /// <summary>Sets the free-text search Keywords on <paramref name="nodeId"/>.</summary>
    Task SetKeywordsAsync(Guid nodeId, string? keywords, string? userId);

    /// <summary>
    /// Moves the node up or down among its siblings by swapping <c>SortOrder</c> with the
    /// adjacent sibling in the requested <paramref name="direction"/> ("up" or "down").
    /// No-ops silently when the node is already at the end of the list.
    /// </summary>
    Task MoveAsync(Guid nodeId, string direction);

    /// <summary>
    /// Publishes the working draft (the highest-VersionId version with no PublishFrom)
    /// by setting its publish window to now–open-ended. No-op if no draft exists.
    /// </summary>
    Task PublishDraftAsync(Guid nodeId, string? userId);

    /// <summary>
    /// Unpublishes the currently-live version by clearing its publish window so it becomes
    /// an unscheduled draft again. No-op if no version is currently live.
    /// </summary>
    Task UnpublishAsync(Guid nodeId, string? userId);

    /// <summary>Returns true if any version is currently marked as live (IsCurrent).</summary>
    Task<bool> IsPublishedAsync(Guid nodeId);

    /// <summary>
    /// Renames a node's Segment and/or Title. When the Segment changes, the node's Path — and
    /// every descendant's Path — is rewritten atomically so all sub-page URLs stay valid.
    /// Returns <c>RenameNodeResult.Ok</c> on success, or a specific failure reason so the caller
    /// can surface a helpful message.
    /// </summary>
    Task<RenameNodeResult> RenameNodeAsync(
        Guid id, string newSegment, string newTitle, string? newSubtitle, string? newPageName, string? userId);

    /// <summary>
    /// Clones a page as a sibling of the source with a fresh Guid, all versions copied verbatim
    /// (content + publish windows + IsCurrent flag preserved), and a <c>-copy</c>-suffixed
    /// segment and title so the two rows are distinguishable in the tree. If the target segment
    /// is already taken (a prior copy exists) the suffix is bumped to <c>-copy-2</c>, <c>-copy-3</c>
    /// and so on. Returns the new node, or <c>null</c> if the source doesn't exist.
    /// </summary>
    Task<PageNodeDto?> CopyPageAsync(Guid sourceId, string? userId);
}

/// <summary>Outcome of <see cref="IPageNodeService.RenameNodeAsync"/>.</summary>
public enum RenameNodeResult
{
    Ok,
    NotFound,
    /// <summary>The segment is empty or otherwise invalid.</summary>
    InvalidSegment,
    /// <summary>Another live node already occupies the target path.</summary>
    PathConflict
}

/// <summary>Outcome of <see cref="IPageNodeService.MoveNodeAsync"/>.</summary>
public enum MoveNodeResult
{
    Ok,
    NotFound,
    /// <summary>The move would place the node under itself or one of its descendants.</summary>
    Cycle,
    /// <summary>Another live node already occupies the target path.</summary>
    PathConflict,
}
