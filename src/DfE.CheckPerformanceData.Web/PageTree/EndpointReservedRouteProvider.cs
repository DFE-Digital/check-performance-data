using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// Walks all registered <see cref="EndpointDataSource"/> instances and collects the first
/// literal path segment of every <see cref="RouteEndpoint"/>.  Only literal first segments
/// are collected; parameter and catch-all first segments (e.g. <c>{controller}</c>,
/// <c>{*path}</c>) are skipped so the page-tree catch-all route does not accidentally
/// reserve every segment.
/// </summary>
public sealed class EndpointReservedRouteProvider : IReservedRouteProvider
{
    private readonly IEnumerable<EndpointDataSource> _sources;

    public EndpointReservedRouteProvider(IEnumerable<EndpointDataSource> sources)
        => _sources = sources;

    public IReadOnlySet<string> ReservedFirstSegments()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _sources)
        {
            foreach (var endpoint in source.Endpoints)
            {
                if (endpoint is not RouteEndpoint re)
                    continue;

                var segments = re.RoutePattern.PathSegments;
                if (segments.Count == 0)
                    continue;

                // A segment can contain multiple parts (e.g. "prefix-{param}"); we only
                // care about segments whose FIRST part is a plain literal.
                var firstPart = segments[0].Parts.FirstOrDefault();
                if (firstPart is RoutePatternLiteralPart literal)
                    result.Add(literal.Content.ToLowerInvariant());
            }
        }

        return result;
    }
}
