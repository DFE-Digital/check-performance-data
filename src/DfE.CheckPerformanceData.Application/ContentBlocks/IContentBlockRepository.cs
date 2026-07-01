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

    // Commands
    Task<ContentBlockDto> AddBlockAsync(string key, string blockType, string value, Guid? contentId = null);
    Task AddVersionAsync(int contentBlockId, string value, int versionNumber);
    Task UpdateValueAsync(int id, string newValue);
    Task UpdateForStagingAsync(int id, string key, string blockType, string value, Guid contentId);
    Task SetLastSeenAsync(string key, string path, DateTime seenAt);

    Task SaveChangesAsync();
    Task ExecuteInTransactionAsync(Func<Task> work);
}
