using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.Media.Widget;

public static class MediaVersionResolverExtensions
{
    /// <summary>
    /// Loads every library image that a page's content uses, in one call, before the page renders
    /// its widgets. Each widget then finds its image in the resolver without a query.
    /// </summary>
    public static Task PrimeAsync(this IMediaVersionResolver resolver, IReadOnlyList<ContentNode> content,
        Guid? pageVersionId, CancellationToken ct = default)
    {
        var ids = MediaReferenceExtractor.Extract(ContentPageJson.Serialize(content)).Select(r => r.AssetId);
        return resolver.ResolveAsync(ids, pageVersionId, ct);
    }
}
