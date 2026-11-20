namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Deletes a library image that no page uses: its rows first, then every stored file of it.
/// </summary>
public interface IMediaDeletionService
{
    /// <summary>
    /// Deletes the image. Refuses (<see cref="MediaDeleteOutcome.InUse"/>) while any page version
    /// uses it. A file that cannot be removed is logged and left; the image is still deleted.
    /// </summary>
    Task<MediaDeleteOutcome> DeleteAsync(Guid assetId, CancellationToken ct = default);
}
