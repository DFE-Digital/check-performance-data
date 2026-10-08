using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Hits the dev-only impersonation endpoint and captures the cookie that authenticates
// subsequent requests as an editor. The cookie value is written onto the fixture's own
// TestHttpClient (SeedClient) so calls through that client — which doesn't keep its own
// cookie container — can thread it manually; each collection's fixture isolates its own
// cookie so parallel collections never see each other's temporary impersonation.
public static class AuthHelpers
{
    private const string ImpersonationCookieName = "cypd-dev-impersonation";

    public static async Task<string?> ImpersonateAsEditorAsync(PlaywrightFixture fixture)
    {
        var cookie = await CallEndpointAsync(fixture, "/dev/impersonate/editor");
        fixture.SeedClient.ImpersonationCookieHeader = cookie;
        return cookie;
    }

    public static async Task<string?> ImpersonateAsUnprivilegedUserAsync(PlaywrightFixture fixture)
    {
        var cookie = await CallEndpointAsync(fixture, "/dev/impersonate/user");
        fixture.SeedClient.ImpersonationCookieHeader = cookie;
        return cookie;
    }

    public static async Task<string?> ImpersonateAsAdminAsync(PlaywrightFixture fixture)
    {
        var cookie = await CallEndpointAsync(fixture, "/dev/impersonate/admin");
        fixture.SeedClient.ImpersonationCookieHeader = cookie;
        return cookie;
    }

    // Returns the independent-school impersonation cookie WITHOUT publishing it as the fixture's
    // default, unlike its siblings above. The caller drops it onto its own Playwright context
    // instead: this cookie expresses a different organisation type, and making it the fixture
    // default would retarget every other test in the collection, which shares one fixture and
    // may execute concurrently. Seed calls therefore keep running as the fixture-wide editor.
    public static Task<string?> ImpersonateAsIndependentAsync(PlaywrightFixture fixture)
        => CallEndpointAsync(fixture, "/dev/impersonate/independent");

    // Hits the dev-only clear endpoint that deletes the impersonation cookie entirely,
    // restoring true-anonymous state. Distinct from ImpersonateAsUnprivilegedUserAsync
    // (which keeps a synthetic "user" principal). Powers the UI sign-out from the
    // impersonation-only state.
    public static async Task ClearImpersonationAsync(PlaywrightFixture fixture)
    {
        await CallEndpointAsync(fixture, "/dev/impersonate/clear");
        fixture.SeedClient.ImpersonationCookieHeader = null;
    }

    private static async Task<string?> CallEndpointAsync(PlaywrightFixture fixture, string path)
    {
        // Use the no-redirect, no-impersonation client so the 302 response (which carries
        // the Set-Cookie header) doesn't get consumed when the auto-redirect follows to "/".
        // SeedClient would otherwise both follow the redirect (unless no-redirect) and attach
        // whatever impersonation cookie is currently published, muddying which principal the
        // endpoint sees. AnonymousClient is the impersonation-free half of the fixture.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(fixture.SeedClient.BaseAddress!, path));
        using var response = await fixture.AnonymousClient.SendAsync(request);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var raw in setCookies)
            {
                var first = raw.Split(';', 2)[0].Trim();
                if (first.StartsWith($"{ImpersonationCookieName}=", StringComparison.Ordinal))
                {
                    return first;
                }
            }
        }

        return null;
    }
}
