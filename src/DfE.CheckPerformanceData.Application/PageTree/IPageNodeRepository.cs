namespace DfE.CheckPerformanceData.Application.PageTree;

public interface IPageNodeRepository
{
    /// <summary>All live (non-deleted) nodes, ordered by ParentId then SortOrder. Caller builds the tree.</summary>
    Task<List<PageNodeTreeItemDto>> GetTreeAsync();

    Task<PageNodeDto?> GetByPathAsync(string path);

    Task<PageNodeDto?> GetByIdAsync(Guid id);

    Task<PageNodeDto> CreateNodeAsync(
        Guid? parentId, string segment, string path, string title, string pageType, string? userId);

    /// <summary>Adds a new version (max+1) and recomputes IsCurrent. Returns the new VersionId.</summary>
    Task<int> AddVersionAsync(
        Guid nodeId, string content, string bodyPlainText,
        DateTime? publishFrom, DateTime? publishTo, string? userId);

    Task UpdateVersionContentAsync(
        Guid nodeId, int versionId, string content, string bodyPlainText, string? userId);

    Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId);

    Task<PageNodeVersionDto?> GetLiveVersionAsync(Guid nodeId, DateTime nowUtc);

    /// <summary>Recomputes IsCurrent on all versions: sets it on the resolver's pick, clears others.</summary>
    Task RecomputeCurrentAsync(Guid nodeId, DateTime nowUtc);

    Task<bool> HasChildrenAsync(Guid nodeId);

    Task SoftDeleteAsync(Guid nodeId, string? userId);

    Task ExecuteInTransactionAsync(Func<Task> work);
}
