using DfE.CheckPerformanceData.Application.Analytics;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Analytics;

// Reads the dashboard's surface filter off the current request: ?surface=instant-page, or
// repeated for more than one. Anything absent, empty, or not a surface this app declares
// means every surface, so a mistyped value widens the view rather than silently emptying it.
//
// Scoped, and resolved per request — same shape and the same reason as
// CmsSettingsSearchDebugOptions: never register it as a Singleton, or the first request's
// filter would pin itself to the process for everyone.
public sealed class QueryStringSearchSurfaceFilter(IHttpContextAccessor accessor) : ISearchSurfaceFilter
{
    public const string QueryKey = "surface";

    public IReadOnlyList<string> Surfaces
    {
        get
        {
            var values = accessor.HttpContext?.Request.Query[QueryKey];
            if (values is null || values.Value.Count == 0) return SearchSurfaces.All;

            var chosen = values.Value
                .Where(v => SearchSurfaces.IsKnown(v))
                .Select(v => v!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return chosen.Length == 0 ? SearchSurfaces.All : chosen;
        }
    }
}
