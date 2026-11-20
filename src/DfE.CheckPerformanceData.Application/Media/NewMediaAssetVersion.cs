namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// What the upload pipeline knows about a file it has already stored at <see cref="BlobKey"/>.
/// Sha256 is the lower-case hex digest of the stored (cleaned) bytes, used to spot duplicates.
/// </summary>
public sealed record NewMediaAssetVersion(
    string BlobKey,
    string ContentType,
    int? Width,
    int? Height,
    long SizeBytes,
    string Sha256,
    bool IsAnimated);
