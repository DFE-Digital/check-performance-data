using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The local stack runs in Development over plain HTTP. The cookie Secure policy is forced only
// outside Development, so the antiforgery cookie minted here must stay usable over http — if it
// were forced Secure the browser would drop it and every form POST in the suite would fail
// antiforgery validation with a 400. This pins the Development downgrade end to end, against the
// running app, rather than only against the options object.
[Collection("E2E")]
public sealed class CookieSecureFlagLocalHttpTests(PlaywrightFixture fixture)
{
    private const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";
    private const string Path = "/dev/antiforgery-token";

    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task DevelopmentStack_DoesNotForceSecureOnTheAntiforgeryCookie()
    {
        var setCookie = await AntiforgerySetCookieAsync();

        Assert.StartsWith(AntiforgeryCookiePrefix, setCookie, StringComparison.Ordinal);

        // Look only at the attributes, not the (base64) value, so an incidental substring in the
        // payload can never make this pass or fail.
        var attributes = setCookie.Split(';').Skip(1).Select(a => a.Trim());
        Assert.DoesNotContain("secure", attributes, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> AntiforgerySetCookieAsync()
    {
        var baseAddress = _fixture.SeedClient.BaseAddress
            ?? throw new InvalidOperationException("SeedClient has no BaseAddress.");

        // The token route is editor-gated, so carry the fixture's dev impersonation cookie.
        // Read the raw Set-Cookie rather than using the scrape helper, which keeps only the
        // name=value pair and therefore discards the attributes this test exists to check.
        var cookieContainer = new CookieContainer();
        var impersonation = _fixture.SeedClient.ImpersonationCookieHeader;
        if (!string.IsNullOrEmpty(impersonation))
        {
            var equalsIndex = impersonation.IndexOf('=');
            if (equalsIndex > 0)
            {
                cookieContainer.Add(baseAddress, new Cookie(
                    impersonation[..equalsIndex],
                    impersonation[(equalsIndex + 1)..]) { Path = "/" });
            }
        }

        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { BaseAddress = baseAddress };

        using var response = await client.GetAsync(Path);
        response.EnsureSuccessStatusCode();

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            throw new InvalidOperationException($"No Set-Cookie was issued by {Path}.");
        }

        return setCookies.First(c => c.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal));
    }
}
