namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// Returns the set of first URL path segments already claimed by registered application routes.
/// Abstracts away <see cref="Microsoft.AspNetCore.Routing.EndpointDataSource"/> so the
/// validator remains pure and unit-testable without a real ASP.NET host.
/// </summary>
public interface IReservedRouteProvider
{
    IReadOnlySet<string> ReservedFirstSegments();
}
