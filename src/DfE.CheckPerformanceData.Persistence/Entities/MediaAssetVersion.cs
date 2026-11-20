namespace DfE.CheckPerformanceData.Persistence.Entities;

// One stored file of a media library image. Written once and never changed: a page version that
// recorded this version must keep showing exactly this file. Keyed by (AssetId, Version).
public sealed class MediaAssetVersion
{
    public Guid AssetId { get; set; }
    public MediaAsset Asset { get; set; } = null!;
    public int Version { get; set; }
    public string BlobKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    // Null when the file has no intrinsic size (an SVG with no width, height or viewBox).
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long SizeBytes { get; set; }
    // Lower-case hex SHA-256 of the bytes as uploaded, used to offer an existing copy.
    public string Sha256 { get; set; } = string.Empty;
    public bool IsAnimated { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string? UploadedBy { get; set; }
}
