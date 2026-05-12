namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// One no-redirect HttpClient shared across the suite for the lifetime of the test
// process. Building a fresh HttpClient + handler per test or per seed call is the
// pattern HttpClient docs warn against — socket exhaustion on tight loops, and
// pointless garbage in a normal test run. The instance is intentionally never
// disposed; its lifetime is bounded by process exit.
//
// No BaseAddress is set; callers either build absolute URIs or rely on the
// caller-supplied HttpClient's BaseAddress when sending. UseCookies=false is
// deliberate so consumers manage cookie headers explicitly (the seed flow does
// this because it threads antiforgery cookies through every POST).
internal static class TestHttpClients
{
    public static HttpClient NoRedirect { get; } = new(
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });

    // Set once by AuthHelpers.ImpersonateAsEditorAsync; threaded onto every request
    // sent via SendAsync below. The no-redirect handler has UseCookies=false so test
    // cookie state is explicit — we can't rely on a CookieContainer to carry the
    // impersonation cookie automatically.
    public static string? ImpersonationCookieHeader { get; set; }

    // Drop-in replacement for NoRedirect.SendAsync that auto-attaches the
    // impersonation cookie (if set). Use this from any seed/CRUD call that targets
    // an editor-gated endpoint.
    public static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        AttachImpersonationCookie(request);
        return NoRedirect.SendAsync(request);
    }

    public static void AttachImpersonationCookie(HttpRequestMessage request)
    {
        var impersonation = ImpersonationCookieHeader;
        if (string.IsNullOrEmpty(impersonation))
        {
            return;
        }

        if (request.Headers.TryGetValues("Cookie", out var existing))
        {
            var joined = string.Join("; ", existing) + "; " + impersonation;
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", joined);
        }
        else
        {
            request.Headers.Add("Cookie", impersonation);
        }
    }
}
