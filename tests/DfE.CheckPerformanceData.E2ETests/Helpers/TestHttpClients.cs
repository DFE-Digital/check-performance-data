namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// One no-redirect HttpClient per fixture (the fixture's SeedClient) for the lifetime of the
// test process. Building a fresh HttpClient + handler per test or per seed call is the
// pattern HttpClient docs warn against — socket exhaustion on tight loops, and pointless
// garbage in a normal test run. The instance is intentionally never disposed before the
// fixture tears down; its lifetime is bounded by the fixture.
//
// No BaseAddress is set; callers either build absolute URIs or resolve relative request
// URIs against the fixture's BaseUrl. UseCookies=false is deliberate so consumers manage
// cookie headers explicitly (the seed flow does this because it threads antiforgery
// cookies through every POST).
//
// The impersonation cookie is per-Fixture state, not process-global. The suite runs more
// than one collection against one app instance; a shared static would let one collection's
// temporarily-swapped impersonation (admin / unprivileged / cleared) leak into every other
// collection's requests mid-run. Each fixture owns one TestHttpClient and writes only its
// own cookie.
//
// SendAsync is shadowed (not overridden) because HttpClient's overloads that take an
// HttpCompletionOption are not virtual in this framework version. All suite call sites use
// SendAsync(request) / SendAsync(request, ct) on a TestHttpClient-typed reference, so the
// shadows attach the impersonation cookie on every seed/CRUD send. Inherited convenience
// shorthands like GetAsync take the base 3-arg path and skip the cookie — acceptable, since
// the only GetAsync call is the anonymous /healthcheck readiness probe.
public sealed class TestHttpClient : HttpClient
{
    public TestHttpClient()
        : base(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
    {
    }

    // Set by AuthHelpers.ImpersonateAsEditorAsync (via the owning fixture); threaded onto
    // every request sent through this client. The no-redirect handler has UseCookies=false
    // so test cookie state is explicit — we can't rely on a CookieContainer to carry the
    // impersonation cookie automatically.
    public string? ImpersonationCookieHeader { get; set; }

    public new Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        => SendAsync(request, CancellationToken.None);

    public new Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => SendCoreAsync(request, cancellationToken);

    public new Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
        => SendCoreAsync(request, cancellationToken);

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AttachImpersonationCookie(request);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void AttachImpersonationCookie(HttpRequestMessage request)
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