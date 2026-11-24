namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>A new version was not the next number for its image, usually because another replace saved first.</summary>
public sealed class MediaVersionConflictException(Guid assetId, int version, Exception? inner = null)
    : InvalidOperationException($"Media item {assetId} cannot take version {version}: it is not the next version.", inner)
{
    public Guid AssetId { get; } = assetId;
    public int Version { get; } = version;
}
