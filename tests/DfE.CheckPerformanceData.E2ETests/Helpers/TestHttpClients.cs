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

    // A deployed environment sits behind an ingress that may close a kept-alive connection
    // just as the next request goes out on it. The handler resends a GET by itself, but not a
    // request whose body it has already written, so a seed POST surfaces as "The response
    // ended prematurely". That error means no response bytes arrived at all, so the request is
    // sent again on a fresh connection, up to MaxSendAttempts times. Should the first send
    // have reached the app after all, the resend fails loudly on the caller's status check
    // (a duplicate segment, say) rather than passing quietly.
    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AttachImpersonationCookie(request);
        // A sent HttpRequestMessage cannot be sent twice, so keep what a copy needs.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            var toSend = attempt == 1 ? request : Copy(request, body);
            try
            {
                return await base.SendAsync(toSend, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxSendAttempts && ConnectionClosedUnanswered(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public const int MaxSendAttempts = 3;

    private static bool ConnectionClosedUnanswered(HttpRequestException ex) =>
        ex.HttpRequestError == HttpRequestError.ResponseEnded
        || ex.InnerException is HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded };

    private static HttpRequestMessage Copy(HttpRequestMessage original, byte[]? body)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (body is not null && original.Content is not null)
        {
            copy.Content = new ByteArrayContent(body);
            foreach (var header in original.Content.Headers)
            {
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        return copy;
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