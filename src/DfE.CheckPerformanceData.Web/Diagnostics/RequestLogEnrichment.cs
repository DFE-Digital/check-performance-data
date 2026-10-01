using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Diagnostics;

// Values added to the one-line-per-request Serilog log, so a request can be tied to a user and a
// redirect to its destination. Without them, a 302 from an auth challenge looks the same as a
// 302 from a controller, and no request can be traced back to the person who made it.
public static class RequestLogEnrichment
{
    // Where a 3xx response sends the browser, without its query string or fragment. The query
    // is dropped on purpose: the DfE Sign-in authorize URL carries the OIDC state and nonce, and
    // other redirects may carry search terms. Origin and path are enough to tell an auth
    // challenge from an in-app redirect.
    public static string? RedirectTarget(HttpResponse response)
    {
        if (response.StatusCode is < 300 or > 399) return null;

        var location = response.Headers.Location.ToString();
        if (string.IsNullOrEmpty(location)) return null;

        if (Uri.TryCreate(location, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            return absolute.GetLeftPart(UriPartial.Path);
        }

        var end = location.IndexOfAny(['?', '#']);
        return end < 0 ? location : location[..end];
    }

    // The DfE Sign-in user id of a signed-in user, or null. The same id is already logged by the
    // sign-in and claims-enrichment warnings, so the request log can be joined to them.
    public static string? UserId(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;
}
