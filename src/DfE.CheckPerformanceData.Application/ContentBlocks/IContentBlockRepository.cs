namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public interface IContentBlockRepository
{
    // Queries
    Task<List<ContentBlockDto>> GetAllAsync();
    Task<ContentBlockDto?> GetByKeyAsync(string key);
    Task<List<ContentBlockDto>> SearchAsync(string query, int take);
    Task<ContentBlockDto?> GetByContentIdAsync(Guid contentId);
    Task<int> GetMaxVersionNumberAsync(int contentBlockId);
    Task<ContentBlockVersionDto?> GetVersionByIdAsync(int versionId);
    Task<List<ContentBlockVersionDto>> GetVersionsByKeyAsync(string key);

    // Commands. Callers always pass valuePlainText alongside value so ValuePlainText stays in
    // sync with Value — the service layer strips HTML once via IHtmlRenderingService and hands
    // both to the repo, avoiding a second HTML-parse inside the repository.
    Task<ContentBlockDto> AddBlockAsync(string key, string blockType, string value, string valuePlainText, Guid? contentId = null, bool appearInSearch = true, string? keywords = null);
    Task AddVersionAsync(int contentBlockId, string value, int versionNumber);
    Task UpdateValueAsync(int id, string newValue, string newValuePlainText);
    Task SetAppearInSearchAsync(int id, bool appearInSearch);
    Task SetKeywordsAsync(int id, string? keywords);
    Task UpdateForStagingAsync(int id, string key, string blockType, string value, string valuePlainText, Guid contentId, bool appearInSearch, string? keywords);
    Task SetLastSeenAsync(string key, string path, DateTime seenAt);

    Task SaveChangesAsync();
    Task ExecuteInTransactionAsync(Func<Task> work);
}
