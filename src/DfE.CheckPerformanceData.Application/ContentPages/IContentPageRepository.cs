namespace DfE.CheckPerformanceData.Application.ContentPages;

// Persistence boundary for content pages and their published-version history. Command methods that
// must be atomic together are composed by the caller inside <see cref="ExecuteInTransactionAsync"/>.
public interface IContentPageRepository
{
    Task<ContentPageDto> CreateAsync(string slug, string title, string? caption, string layout, string draftContent, Guid? contentId = null);

    Task<ContentPageDto?> GetBySlugAsync(string slug);

    Task UpdateDraftAsync(int id, string draftContent);

    Task<int> GetMaxVersionNumberAsync(int contentPageId);

    Task AddVersionAsync(int contentPageId, int versionNumber, string snapshot, string title);

    Task SetPublishedVersionAsync(int id, int versionNumber);

    Task<ContentPageVersionDto?> GetVersionAsync(int contentPageId, int versionNumber);

    Task<List<ContentPageVersionDto>> GetVersionsAsync(int contentPageId);

    Task ExecuteInTransactionAsync(Func<Task> work);
}
