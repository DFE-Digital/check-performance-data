using DfE.CheckPerformanceData.Application.Analytics;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Analytics;

// Web-project implementation of ISearchAnalyticsSessionProvider: reads Session.Id off
// IHttpContextAccessor. When no request is in flight (background service, hosted worker)
// OR the session middleware never ran on the current request, GetSessionId returns null
// — the composite decorator uses that as its "skip the sink write" signal.
//
// Session.Id is read server-side and never from a cookie or the HTML source-comment —
// this defends against form-tampering paths that would inject a foreign session id via
// a client-controlled surface.
public sealed class HttpContextSearchAnalyticsSessionProvider(IHttpContextAccessor accessor)
    : ISearchAnalyticsSessionProvider
{
    public string? GetSessionId()
    {
        var context = accessor.HttpContext;
        var session = context?.Session;
        if (session is null)
        {
            return null;
        }

        // IsAvailable is false until the session middleware has loaded — a request that
        // reaches here before Session has been loaded should be treated as no-session
        // rather than eagerly triggering a load off the ambient scope.
        if (!session.IsAvailable)
        {
            return null;
        }

        return session.Id;
    }
}
