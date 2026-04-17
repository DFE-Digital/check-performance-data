namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public interface IContentBlockRepository
{
    // Queries
    Task<ContentBlockDto?> GetByKeyAsync(string key);
    Task<int> GetMaxVersionNumberAsync(int contentBlockId);
    Task<ContentBlockVersionDto?> GetVersionByIdAsync(int versionId);
    Task<List<ContentBlockVersionDto>> GetVersionsByKeyAsync(string key);

    // Commands
    Task<ContentBlockDto> AddBlockAsync(string key, string blockType, string value);
    Task AddVersionAsync(int contentBlockId, string value, int versionNumber);
    Task UpdateValueAsync(int id, string newValue);

    Task SaveChangesAsync();
    Task ExecuteInTransactionAsync(Func<Task> work);
}
