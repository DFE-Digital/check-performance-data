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
}
