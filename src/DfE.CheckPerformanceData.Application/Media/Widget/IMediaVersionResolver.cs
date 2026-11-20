namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>
/// Finds the image version that a page shows when it renders. A live, draft or preview page shows
/// each image's current version, so a replaced image changes on every page. A historic page version
/// shows the version recorded for it when it was saved (see media_usages), or the current version
/// when nothing is recorded.
/// </summary>
/// <remarks>
/// One instance serves one request and keeps what it loads. A page resolves all its images in one
/// call first, so each image on the page then costs no further query.
/// </remarks>
public interface IMediaVersionResolver
{
    /// <param name="pageVersionId">
    /// The page version (its row id) whose recorded versions to use, or null for the current versions.
    /// </param>
    /// <returns>The images found, by id. An image that is not in the library is left out.</returns>
    Task<IReadOnlyDictionary<Guid, ResolvedMedia>> ResolveAsync(
        IEnumerable<Guid> assetIds, Guid? pageVersionId, CancellationToken ct = default);
}
