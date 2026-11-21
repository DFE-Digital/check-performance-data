namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Moves images embedded in page content as data: URIs into the media library. The dry run
/// changes nothing. A run uploads each image through the upload pipeline and points the content
/// at the library image. Each page changes completely or not at all.
/// </summary>
public interface IEmbeddedImageMover
{
    Task<EmbeddedImageScan> ScanAsync(CancellationToken ct = default);

    Task<EmbeddedImageMoveResult> MoveAsync(CancellationToken ct = default);
}
