using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Hits the dev-only impersonation endpoint and captures the cookie that authenticates
// subsequent requests as an editor. The cookie value is also exposed via
// TestHttpClients.ImpersonationCookieHeader so calls through the shared no-redirect
// HttpClient (which doesn't keep its own cookie container) can thread it manually.
public static class AuthHelpers
{
    private const string ImpersonationCookieName = "cypd-dev-impersonation";

    public static async Task<string?> ImpersonateAsEditorAsync(PlaywrightFixture fixture)
    {
        var cookie = await CallEndpointAsync(fixture, "/dev/impersonate/editor");
        TestHttpClients.ImpersonationCookieHeader = cookie;
        return cookie;
    }

    public static async Task<string?> ImpersonateAsUnprivilegedUserAsync(PlaywrightFixture fixture)
    {
        var cookie = await CallEndpointAsync(fixture, "/dev/impersonate/user");
        TestHttpClients.ImpersonationCookieHeader = cookie;
        return cookie;
    }

    public static Task<string?> ImpersonateAsAdminAsync(PlaywrightFixture fixture)
    {
        throw new NotImplementedException("Admin impersonation endpoint not wired yet.");
    }

    // Hits the dev-only clear endpoint that deletes the impersonation cookie entirely,
    // restoring true-anonymous state. Distinct from ImpersonateAsUnprivilegedUserAsync
    // (which keeps a synthetic "user" principal). Powers the UI sign-out from the
    // impersonation-only state.
    public static async Task ClearImpersonationAsync(PlaywrightFixture fixture)
    {
        await CallEndpointAsync(fixture, "/dev/impersonate/clear");
        TestHttpClients.ImpersonationCookieHeader = null;
    }

    private static async Task<string?> CallEndpointAsync(PlaywrightFixture fixture, string path)
    {
        // Use the no-redirect client so the 302 response (which carries the Set-Cookie
        // header) doesn't get consumed when the auto-redirect follows to "/". SeedClient
        // would otherwise return the final-hop response whose headers no longer carry
        // the impersonation cookie.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(fixture.SeedClient.BaseAddress!, path));
        using var response = await TestHttpClients.NoRedirect.SendAsync(request);

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
