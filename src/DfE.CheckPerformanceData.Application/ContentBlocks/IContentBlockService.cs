namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public interface IContentBlockService
{
    Task<ContentBlockDto?> GetByKeyAsync(string key);
    Task<List<ContentBlockDto>> GetAllAsync();
    Task RecordLastSeenAsync(string key, string path);
    Task<ContentBlockDto> SaveAsync(SaveContentBlockDto dto);
    Task<List<ContentBlockVersionDto>> GetVersionsAsync(string key);
    Task<ContentBlockDto> RevertToVersionAsync(string key, int versionId);

    /// <summary>
    /// Returns the block with <paramref name="key"/>, creating it with <paramref name="defaultValue"/>
    /// and <paramref name="blockType"/> if it does not yet exist. Always records the block as last
    /// seen on <paramref name="path"/> so the admin content-blocks tree lists it under the page
    /// that renders it.
    /// </summary>
    Task<ContentBlockDto> EnsureAsync(string key, string blockType, string defaultValue, string path);
}
