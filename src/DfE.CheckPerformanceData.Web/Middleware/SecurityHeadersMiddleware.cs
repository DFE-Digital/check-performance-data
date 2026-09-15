using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Middleware;

/// <summary>
/// Sets the response security headers, and refuses TRACE.
/// </summary>
/// <remarks>
/// The header set was already most of the way there — HSTS, a content-security policy and
/// X-Frame-Options were in place — with a few gaps. These headers only do anything if they are on
/// every response, so they belong in one middleware rather than spread across controllers where a
/// new endpoint silently misses them.
///
/// The content-security policy moved here unchanged. Its <c>'unsafe-inline'</c> and
/// <c>'unsafe-eval'</c> in <c>script-src</c> are a known weakness, but removing them means
/// threading a per-request nonce through every inline script and style, including those the
/// frontend toolkit and the analytics tags emit. That is a change with real regression risk and
/// deserves its own testing rather than riding along with a header sweep.
///
/// It also hides every non-production environment from search engines (#444). The QA site was
/// ranking in the top Google results for the service: the anonymous pages are crawlable on any
/// host and nothing told a crawler to stay away. <c>X-Robots-Tag</c> is a header rather than a
/// meta tag because it covers static assets, PDFs and JSON as well as HTML, and it is the one
/// signal that gets a page already in the index removed. <c>robots.txt</c> (see
/// <c>RobotsController</c>) is the second layer, not a substitute: a crawler told not to fetch a
/// page never sees the noindex on it. Keyed on <c>!IsProduction()</c> rather than a list of
/// hidden names, so an environment nobody thought to list defaults to hidden.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms; " +
        "style-src 'self' 'unsafe-inline' https://*.googletagmanager.com https://fonts.googleapis.com; " +
        "img-src 'self' data: blob: https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms https://fonts.gstatic.com; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "connect-src 'self' https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms; " +
        "frame-src 'self' https://*.googletagmanager.com; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    // The service asks for none of these. Denying them means an injected frame or script cannot
    // ask on its behalf either.
    private const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=()";

    public async Task InvokeAsync(HttpContext context)
    {
        // Refused before anything else runs: TRACE has no use in this service, and a method that
        // is going to be rejected should not first be routed, authorised and handled.
        if (HttpMethods.IsTrace(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        // Assignment rather than Append: appending to a header something upstream already set
        // produces two of it, and a browser given two conflicting security headers is entitled to
        // pick the one we did not want.
        Set(context, "Content-Security-Policy", ContentSecurityPolicy);
        Set(context, "X-Content-Type-Options", "nosniff");
        Set(context, "Referrer-Policy", "strict-origin-when-cross-origin");
        Set(context, "Permissions-Policy", PermissionsPolicy);

        if (!environment.IsProduction())
        {
            Set(context, "X-Robots-Tag", "noindex, nofollow");
        }

        await next(context);
    }

    private static void Set(HttpContext context, string name, string value) =>
        context.Response.Headers[name] = value;
}
