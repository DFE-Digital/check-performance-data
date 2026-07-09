using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The anonymised share link and the wallboard are the security-sensitive surfaces: they are
// reachable without the admin cookie but gated by an opaque token. A valid token renders the
// aggregate-only view (banner, charts + tables, no admin actions, no pupil data); a missing or
// invalid token returns 404 — never a redirect into the DfE OIDC challenge. The admin generates
// tokens from the role-gated /admin/share surface. All assertions are DOM-level.
[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class ShareLinkTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private const string Banner =
        "Read-only view. Figures are aggregated and contain no pupil information.";

    // --- An invalid share token returns 404, not a redirect into the OIDC challenge ---

    [Fact]
    public async Task Share_WithInvalidToken_Returns404_NotOidcRedirect()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_fixture.BaseUrl}/share/this-token-does-not-exist");

        var response = await TestHttpClients.NoRedirect.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Never a 302/401 that would bounce an uninvited viewer into the sign-in flow.
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- An invalid wallboard token returns 404 too ---

    [Fact]
    public async Task Wallboard_WithInvalidToken_Returns404_NotOidcRedirect()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_fixture.BaseUrl}/wallboard/this-token-does-not-exist");

        var response = await TestHttpClients.NoRedirect.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- A non-admin cannot reach the token-management surface (obfuscated as 404) ---

    [Fact]
    public async Task ShareAdmin_AsNonAdmin_Returns_NotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/share");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- An admin generates a token and the share link renders the aggregate-only view ---

    [Fact]
    public async Task Admin_GeneratesToken_AndShareLinkRendersAggregateOnlyView()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            var token = await GenerateShareTokenAsync("E2E share");
            Assert.False(string.IsNullOrWhiteSpace(token));

            // The share link is reachable without the admin cookie (anonymous client).
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/share/{token}");
            var response = await TestHttpClients.NoRedirect.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();

            // The read-only banner and the charts render; there are no admin/inspect/destructive
            // affordances and no pupil-identifier text on the page.
            Assert.Contains(Banner, body);
            Assert.Contains("Throughput", body);
            Assert.Contains("Decision mix", body);
            Assert.DoesNotContain("Inspect", body);
            Assert.DoesNotContain("Redrive", body);
            Assert.DoesNotContain("Purge", body);
            Assert.DoesNotContain("Upn", body);
            Assert.DoesNotContain("Surname", body);

            // The anonymised page must not render any admin chrome: no admin nav rail (which would
            // leak the admin URL topology), no Google Tag Manager (which would exfiltrate the
            // token-in-URL to a third party), and no dev sign-in/impersonation affordances.
            Assert.DoesNotContain("admin-rail", body);
            Assert.DoesNotContain("googletagmanager", body);
            Assert.DoesNotContain("/dev/impersonate", body);
            Assert.DoesNotContain("Administration", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- The wallboard renders chrome-less, large-type, auto-refreshing ---

    [Fact]
    public async Task Admin_GeneratesWallboardToken_AndWallboardRendersChromeLess()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            var token = await GenerateShareTokenAsync("E2E wallboard", surface: "Wallboard");
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/wallboard/{token}");
            var response = await TestHttpClients.NoRedirect.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains(Banner, body);
            Assert.Contains("wallboard", body);
            // The auto-refresh hook is present.
            Assert.Contains("http-equiv=\"refresh\"", body);
            // Chrome-less: the admin layout's rail/wordmark is not rendered.
            Assert.DoesNotContain("admin-rail", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // Posts to the role-gated admin generate endpoint (with antiforgery) and returns the one-time
    // plaintext token from the resulting share-admin page.
    private async Task<string> GenerateShareTokenAsync(string label, string surface = "Share")
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            UseCookies = true,
            AllowAutoRedirect = true,
            CookieContainer = new System.Net.CookieContainer(),
        })
        {
            BaseAddress = new Uri(_fixture.BaseUrl),
        };

        var impersonation = TestHttpClients.ImpersonationCookieHeader;

        var (antiforgeryToken, antiforgeryCookie) =
            await AntiforgeryHelpers.ScrapeAsync(client, "/admin/share");

        using var post = new HttpRequestMessage(HttpMethod.Post, "/admin/share/generate")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["label"] = label,
                ["surface"] = surface,
            }),
        };

        var cookies = antiforgeryCookie;
        if (!string.IsNullOrEmpty(impersonation))
            cookies = impersonation + "; " + antiforgeryCookie;
        post.Headers.Add("Cookie", cookies);

        var postResponse = await client.SendAsync(post);
        postResponse.EnsureSuccessStatusCode();

        var body = await postResponse.Content.ReadAsStringAsync();

        // The plaintext token is shown once on the page as /share/{token} or /wallboard/{token}.
        var marker = surface == "Wallboard" ? "/wallboard/" : "/share/";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += marker.Length;
        var end = start;
        while (end < body.Length && body[end] is not ('<' or '"' or ' ' or '\n' or '\r'))
            end++;

        return body[start..end];
    }
}
