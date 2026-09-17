namespace DfE.CheckPerformanceData.Application.Analytics;

// Which search surfaces the dashboard is currently looking at.
//
// Ambient rather than a parameter on all thirty-odd read methods, following the same shape as
// ISearchDebugOptions: the value is a property of the request, every query wants it, and
// threading it through each signature would add a parameter that no caller ever varies within
// one request.
//
// The default is every surface. A reader that does not care must see everything rather than
// silently see only site searches.
public interface ISearchSurfaceFilter
{
    IReadOnlyList<string> Surfaces { get; }
}

public sealed class AllSearchSurfaces : ISearchSurfaceFilter
{
    public IReadOnlyList<string> Surfaces { get; } = SearchSurfaces.All;
}
