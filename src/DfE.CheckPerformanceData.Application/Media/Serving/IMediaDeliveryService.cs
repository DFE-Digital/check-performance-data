namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Finds what a public media URL serves: the stored file of a version, or a signed resized or
/// cropped copy of it. Anyone can call it, so it reads no unsigned settings and never shows a
/// version that has no record.
/// </summary>
public interface IMediaDeliveryService
{
    /// <param name="query">The URL's query string as name and value pairs, repeats included.</param>
    Task<MediaDelivery> GetAsync(Guid assetId, int version, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken ct = default);
}
