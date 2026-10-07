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
    //
    // A deployed environment sits behind an ingress that may close a kept-alive connection
    // just as the next request goes out on it. The handler resends a GET by itself, but not a
    // request whose body it has already written, so a seed POST surfaces as "The response
    // ended prematurely". That error means no response bytes arrived at all, so the request is
    // sent again on a fresh connection, up to MaxSendAttempts times. Should the first send
    // have reached the app after all, the resend fails loudly on the caller's status check
    // (a duplicate segment, say) rather than passing quietly.
    public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        AttachImpersonationCookie(request);
        // A sent HttpRequestMessage cannot be sent twice, so keep what a copy needs.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync();

        for (var attempt = 1; ; attempt++)
        {
            var toSend = attempt == 1 ? request : Copy(request, body);
            try
            {
                return await NoRedirect.SendAsync(toSend);
            }
            catch (HttpRequestException ex) when (attempt < MaxSendAttempts && ConnectionClosedUnanswered(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
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
