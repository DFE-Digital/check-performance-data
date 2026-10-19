using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The local stack runs in Development over plain HTTP. The cookie Secure policy is forced only
// outside Development, so the antiforgery cookie minted here must stay usable over http — if it
// were forced Secure the browser would drop it and every form POST in the suite would fail
// antiforgery validation with a 400. This pins the Development downgrade end to end, against the
// running app, rather than only against the options object.
//
// Both tests only make sense against that local plain-http stack. Behind the https edge (a
// deployed review app) the forwarded proto is trusted, IsHttps is true, and the cookies are
// correctly Secure — so the suite skips them on any https deployment rather than assert the
// opposite.
[Collection("E2E")]
[Trait("Category", "FullRegression")]
public sealed class CookieSecureFlagLocalHttpTests(PlaywrightFixture fixture)
{
    private const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";
    private const string CorrelationCookiePrefix = ".AspNetCore.Correlation.";
    private const string Path = "/dev/antiforgery-token";

    private readonly PlaywrightFixture _fixture = fixture;

    [SkippableFact]
    public async Task DevelopmentStack_DoesNotForceSecureOnTheAntiforgeryCookie()
    {
        SkipUnlessLocalPlainHttpStack();

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

    private void SkipUnlessLocalPlainHttpStack()
    {
        var baseAddress = _fixture.SeedClient.BaseAddress
            ?? throw new InvalidOperationException("SeedClient has no BaseAddress.");

        // The SameAsRequest Development downgrade only manifests when the app is reached over
        // plain HTTP (IsHttps=false). Behind the https edge the forwarded proto is trusted, so
        // IsHttps=true and the cookies are Secure — asserting the opposite there would be wrong
        // (and did fail the PR-631 review-app run). Skip on any https deployment.
        Skip.IfNot(string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase),
            "Local plain-http Development stack only; behind the https edge the cookie Secure policy follows the trusted forwarded proto.");
    }

    [SkippableFact]
    public async Task Challenge_IssuesASecureCorrelationCookieOverLocalHttp()
    {
        SkipUnlessLocalPlainHttpStack();

        // The OIDC correlation cookie is SameSite=None, which browsers reject unless Secure is set.
        // It must therefore stay Secure even over the local http stack (localhost's secure-context
        // exception accepts it). If the Development downgrade were applied to it — as it is to the
        // antiforgery cookie above — the browser would drop it, the callback would fail correlation
        // validation, and sign-in would end on the NoAccess page. This pins the fix end to end.
        var baseAddress = _fixture.SeedClient.BaseAddress
            ?? throw new InvalidOperationException("SeedClient has no BaseAddress.");

        using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = baseAddress };

        // Any authenticated-only route triggers the OIDC challenge and mints the correlation cookie.
        using var response = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies),
            "The challenge issued no Set-Cookie header.");

        var correlation = setCookies.First(c => c.StartsWith(CorrelationCookiePrefix, StringComparison.Ordinal));

        // Look only at the attributes, not the value, so an incidental substring in the payload
        // can never make this pass or fail.
        var attributes = correlation.Split(';').Skip(1).Select(a => a.Trim());
        Assert.Contains("secure", attributes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", attributes, StringComparer.OrdinalIgnoreCase);
    }
}
