namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// One stored file of a library image. Versions are never changed once written, so a page version
/// that recorded version N keeps showing exactly that file after the image is replaced.
/// Width and Height are null when the file has no intrinsic size (an SVG with neither
/// width/height attributes nor a viewBox).
/// </summary>
public sealed record MediaAssetVersionDto(
    Guid AssetId,
    int Version,
    string BlobKey,
    string ContentType,
    int? Width,
    int? Height,
    long SizeBytes,
    string Sha256,
    bool IsAnimated,
    DateTime UploadedAtUtc,
    string? UploadedBy);
