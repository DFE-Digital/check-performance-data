namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A library image used by the exported content. Identity is the stable Id, which content stores
// as an Image widget's mediaId and a rich-text image's data-media-id, so import keeps it.
// Versions holds the image's current version (CurrentVersion) and any older version that an
// exported page version is pinned to, oldest first.
public sealed record MediaAssetBundleItem
{
    public Guid Id { get; init; }

    private readonly string _fileName = string.Empty;
    public string FileName { get => _fileName; init => _fileName = BundleMemberDefaults.OrEmpty(value); }

    public string? DefaultAltText { get; init; }
    public string? Caption { get; init; }
    public string? Credit { get; init; }

    private readonly List<string> _tags = [];
    public List<string> Tags { get => _tags; init => _tags = BundleMemberDefaults.NonNullItems(value); }

    public int CurrentVersion { get; init; }

    // The folders that hold the image, from the top of the library down. Empty means the top. A
    // bundle made before the library had folders has none (null): import then leaves an existing
    // image where it is, and puts a new one at the top.
    public List<MediaFolderBundleItem>? FolderPath { get; init; }

    private readonly List<MediaAssetVersionBundleItem> _versions = [];
    public List<MediaAssetVersionBundleItem> Versions
    {
        get => _versions;
        init => _versions = BundleMemberDefaults.NonNullItems(value);
    }
}
