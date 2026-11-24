namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// The ways that current content shows library images: the live, scheduled and draft versions of
/// pages, and content blocks. It reads and never writes.
/// </summary>
public interface IMediaVariantService
{
    /// <summary>The ways that current content shows the image, the original first.</summary>
    Task<IReadOnlyList<MediaVariant>> GetVariantsAsync(MediaAssetDto asset, CancellationToken ct = default);

    /// <summary>For each image, the number of ways that current content shows it. 0 when unused.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountVariantsAsync(IReadOnlyCollection<MediaAssetDto> assets, CancellationToken ct = default);
}
