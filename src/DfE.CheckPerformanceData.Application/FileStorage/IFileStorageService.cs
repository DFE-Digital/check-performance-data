namespace DfE.CheckPerformanceData.Application.FileStorage;

public interface IFileStorageService
{
    Task<string> SaveAsync(Guid windowId, byte[] bytes);
    Task DeleteAsync(Guid windowId, string blobName);
    Task<byte[]?> GetAsync(Guid windowId, string blobName);
}
