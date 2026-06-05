namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageBlobListViewModel
{
    public string AccountKey { get; init; } = string.Empty;
    public string AccountDisplayName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public IReadOnlyList<StorageBlobItemViewModel> Blobs { get; init; } = [];
}

public sealed class StorageBlobItemViewModel
{
    public string Name { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}
