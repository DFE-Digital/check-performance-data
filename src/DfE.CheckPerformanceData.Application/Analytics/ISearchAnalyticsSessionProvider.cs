namespace DfE.CheckPerformanceData.Application.Analytics;

// Best-effort ambient session identifier for the search-analytics decorator. Implemented
// in the Web project against IHttpContextAccessor + Session.Id; a null implementation is
// used when there is no request in flight (background-service caller) so the decorator
// can decide to skip the sink write cleanly. Modelled on ILogRequestContext so the
// Application project stays free of the ASP.NET Core reference.
public interface ISearchAnalyticsSessionProvider
{
    // Returns the ASP.NET session id for the current request, or null when no request /
    // session is available. Callers treat null as "no attributable session — skip the
    // sink write".
    string? GetSessionId();
}
