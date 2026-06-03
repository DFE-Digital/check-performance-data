namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageBlobPreviewViewModel
{
    public string AccountKey { get; init; } = string.Empty;
    public string AccountDisplayName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string BlobName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public string? Content { get; init; }
}
