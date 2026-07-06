namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageBlobListViewModel
{
    public string AccountKey { get; init; } = string.Empty;
    public string AccountDisplayName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>The current folder prefix (e.g. "foo/bar/"), or null at the container root.</summary>
    public string? Prefix { get; init; }

    /// <summary>The parent folder prefix to navigate up to, or null when already at the root.</summary>
    public string? ParentPath { get; init; }

    /// <summary>Virtual sub-folders at this level, each a full prefix ending in "/".</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    public IReadOnlyList<StorageBlobItemViewModel> Blobs { get; init; } = [];
}

public sealed class StorageBlobItemViewModel
{
    public string Name { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}
