namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// A new library image with its first file. The caller chooses the Id, because the blob key of the
/// first version (<see cref="MediaBlobKeys.Original"/>) is built from it before the row exists, and
/// because content staging keeps the same id in every environment.
/// </summary>
public sealed record NewMediaAsset(
    Guid Id,
    string FileName,
    string Slug,
    MediaAssetMetadata Metadata,
    NewMediaAssetVersion FirstVersion);
