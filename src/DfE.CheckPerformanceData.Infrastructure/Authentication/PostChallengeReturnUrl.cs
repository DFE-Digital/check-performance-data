using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Infrastructure.Authentication;

// Where DfE Sign-in should send a user back to when the request that needed them to sign in was
// a POST.
//
// By default the OIDC handler returns the user to the URL that was challenged. For a POST that
// is a POST-only action such as /CheckYourPupilData/{id}/nextstep: the browser comes back to it
// with a GET, nothing serves that, and the user lands on "Page not found" having just signed in.
// The form's own page is the right place instead — the user sees where they were and submits
// again. The form data itself cannot survive the round trip through DfE Sign-in.
public static class PostChallengeReturnUrl
{
    private const string Fallback = "/LandingPage";

    // Null for anything but a POST: the handler's default already returns to a URL that can be
    // fetched again. For a POST, the Referer's path and query when it is this site (same scheme,
    // host and port — never an open redirect), otherwise the landing page. Referrer-Policy is
    // strict-origin-when-cross-origin, so a same-origin form post carries its full URL.
    public static string? For(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method)) return null;

        var referer = request.Headers.Referer.ToString();
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return Fallback;

        var sameSite = string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);

        return sameSite ? uri.PathAndQuery : Fallback;
    }
}
