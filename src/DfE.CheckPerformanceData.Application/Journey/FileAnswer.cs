namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class FileAnswer
{
    public required string StoredFileName { get; init; }
    public required string OriginalFileName { get; init; }
    public int PageCount { get; init; }
    public long FileSizeBytes { get; init; }
}
