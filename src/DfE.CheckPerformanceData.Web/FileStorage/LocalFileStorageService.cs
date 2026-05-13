using DfE.CheckPerformanceData.Application.FileStorage;
using UglyToad.PdfPig;

namespace DfE.CheckPerformanceData.Web.FileStorage;

public sealed class LocalFileStorageService(IWebHostEnvironment env) : IFileStorageService
{
    public int? GetPdfPageCount(byte[] bytes)
    {
        try
        {
            using var doc = PdfDocument.Open(bytes);
            return doc.NumberOfPages;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> SaveAsync(byte[] bytes)
    {
        var uploadsPath = Path.Combine(env.ContentRootPath, "Uploads");
        Directory.CreateDirectory(uploadsPath);
        var storedName = Guid.NewGuid().ToString();
        await File.WriteAllBytesAsync(Path.Combine(uploadsPath, storedName), bytes);
        return storedName;
    }

    public Task DeleteAsync(string storedFileName)
    {
        var filePath = Path.Combine(env.ContentRootPath, "Uploads", storedFileName);
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }
}
