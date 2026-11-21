namespace DfE.CheckPerformanceData.Application.ContentStaging;

// One version of a library image in a bundle. The file itself is the MediaFiles entry with the
// same Sha256. ContentType, Width, Height and IsAnimated describe the file as exported; import
// takes them from the file again rather than trusting them.
public sealed record MediaAssetVersionBundleItem
{
    public int Version { get; init; }

    private readonly string _sha256 = string.Empty;
    public string Sha256 { get => _sha256; init => _sha256 = BundleMemberDefaults.OrEmpty(value); }

    private readonly string _contentType = string.Empty;
    public string ContentType { get => _contentType; init => _contentType = BundleMemberDefaults.OrEmpty(value); }

    public int? Width { get; init; }
    public int? Height { get; init; }
    public long SizeBytes { get; init; }
    public bool IsAnimated { get; init; }
}
