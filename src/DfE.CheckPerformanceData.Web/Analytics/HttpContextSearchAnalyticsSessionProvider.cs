using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Analytics;

// Web-project implementation of ISearchAnalyticsSessionProvider: reads the app-owned
// session identity off IHttpContextAccessor. When no request is in flight (background
// service, hosted worker), the session middleware never ran on the current request, or
// no identity has been established yet, GetSessionId returns null — the composite
// decorator uses that as its "skip the sink write" signal.
//
// The identity is read server-side and never from a cookie or the HTML source-comment —
// this defends against form-tampering paths that would inject a foreign session id via
// a client-controlled surface. It is CpdSessionIdentity rather than ASP.NET's Session.Id
// so that the absolute-lifetime cap can rotate it; keying analytics on the framework id
// would attribute every request from a replayed cookie to one unbounded session.
public sealed class HttpContextSearchAnalyticsSessionProvider(IHttpContextAccessor accessor)
    : ISearchAnalyticsSessionProvider
{
    // Peek handles the null-context, not-yet-loaded and no-identity cases uniformly.
    public string? GetSessionId() => CpdSessionIdentity.Peek(accessor.HttpContext?.Session);
}
