namespace DfE.CheckPerformanceData.Application.FileStorage;

public interface IFileStorageService
{
    int? GetPdfPageCount(byte[] bytes);
    Task<string> SaveAsync(byte[] bytes);
    Task DeleteAsync(string storedFileName);
}
