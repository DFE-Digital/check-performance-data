namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageBlobPreviewViewModel
{
    public string ContainerName { get; init; } = string.Empty;
    public string BlobName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public string? Content { get; init; }
}
