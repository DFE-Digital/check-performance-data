namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public interface IContentBlockService
{
    Task<ContentBlockDto?> GetByKeyAsync(string key);
    Task<ContentBlockDto> SaveAsync(SaveContentBlockDto dto);
    Task<List<ContentBlockVersionDto>> GetVersionsAsync(string key);
    Task<ContentBlockDto> RevertToVersionAsync(string key, int versionId);
}
