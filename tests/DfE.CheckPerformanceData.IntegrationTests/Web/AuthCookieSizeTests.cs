using System.Net;
using System.Security.Claims;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.Web;

// The authentication cookie must not grow with the size of the principal.
//
// A DfE Sign-in ticket carries the organisation blob from the userinfo endpoint, a role claim
// per granted role, and (with SaveTokens on) the id, access and refresh tokens. Written straight
// into a cookie that is roughly 4.2 KB, chunked across .AspNetCore.CookiesC1 and C2 — and it
// grows every time the identity provider returns another claim.
//
// That matters because the deployed ingress caps a single request header field at one 8k buffer
// and every cookie for a host travels in one Cookie field. Half that budget going to the auth
// cookie is what left no room for anything else in #406, and nginx rejects the request before
// the app can see it, so the failure cannot be logged or self-healed.
//
// Holding the ticket server-side makes the cookie a short opaque key whose length has nothing
// to do with the principal, which is the property these tests pin.
public sealed class AuthCookieSizeTests
{
    // A signed-in ticket must never approach a cookie chunk. Well under the 4,090-byte chunking
    // threshold, so any regression that puts the principal back in the cookie trips this long
    // before it reaches the ingress and starts producing 400s.
    private const int MaxAuthCookieBytes = 1024;

    [Fact]
    public async Task SignIn_WithALargePrincipal_WritesASmallAuthCookie()
    {
        using var host = await BuildHostAsync();

        using var response = await host.GetTestClient().GetAsync("/sign-me-in");

        var bytes = AuthCookiePairs(response).Sum(pair => pair.Length);
        Assert.True(
            bytes < MaxAuthCookieBytes,
            $"A ticket carrying {FatPrincipalApproximateBytes():N0} bytes of claims put {bytes:N0} "
            + $"bytes of authentication cookies on the client.");
    }

    [Fact]
    public async Task SignIn_WithALargePrincipal_DoesNotChunkTheAuthCookie()
    {
        using var host = await BuildHostAsync();

        using var response = await host.GetTestClient().GetAsync("/sign-me-in");

        var names = AuthCookiePairs(response).Select(pair => pair.Split('=', 2)[0]).ToList();
        Assert.DoesNotContain(names, n => n.StartsWith(AuthCookieName + "C", StringComparison.Ordinal));
        Assert.DoesNotContain("chunks-", string.Join(";", AuthCookiePairs(response)));
    }

    // Growing the principal must not grow the cookie. This is the property that actually removes
    // the ceiling: whatever DfE Sign-in starts returning, the request header does not move.
    [Fact]
    public async Task AuthCookieSize_DoesNotGrowWithThePrincipal()
    {
        using var small = await BuildHostAsync(roleClaims: 1);
        using var large = await BuildHostAsync(roleClaims: 400);

        using var smallResponse = await small.GetTestClient().GetAsync("/sign-me-in");
        using var largeResponse = await large.GetTestClient().GetAsync("/sign-me-in");

        var smallBytes = AuthCookiePairs(smallResponse).Sum(p => p.Length);
        var largeBytes = AuthCookiePairs(largeResponse).Sum(p => p.Length);

        Assert.True(
            largeBytes == smallBytes,
            $"400 extra role claims changed the cookie from {smallBytes:N0} to {largeBytes:N0} bytes — "
            + "the ticket is still travelling to the browser.");
    }

    // The ticket has to survive the round trip, or the cookie is small because it is broken.
    [Fact]
    public async Task TheSignedInPrincipal_IsRestoredOnTheNextRequest()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var signIn = await client.GetAsync("/sign-me-in");
        var cookies = AllCookiePairs(signIn);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/who-am-i");
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("probe@example.gov.uk", await response.Content.ReadAsStringAsync());
    }

    // Signing out must drop the stored ticket, not just the cookie — otherwise the ticket store
    // accumulates a row per sign-in for the lifetime of the cache entry.
    [Fact]
    public async Task SigningOut_EvictsTheStoredTicket()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var signIn = await client.GetAsync("/sign-me-in");
        var cookies = AllCookiePairs(signIn);

        using var signOut = new HttpRequestMessage(HttpMethod.Get, "/sign-me-out");
        signOut.Headers.Add("Cookie", string.Join("; ", cookies));
        using var _ = await client.SendAsync(signOut);

        // Replay the original cookie. The key it carries must no longer resolve to a ticket.
        using var replay = new HttpRequestMessage(HttpMethod.Get, "/who-am-i");
        replay.Headers.Add("Cookie", string.Join("; ", cookies));
        using var response = await client.SendAsync(replay);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private const string AuthCookieName = ".AspNetCore.Cookies";

    private static List<string> AllCookiePairs(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';', 2)[0]).ToList()
            : [];

    private static List<string> AuthCookiePairs(HttpResponseMessage response) =>
        AllCookiePairs(response)
            .Where(p => p.StartsWith(AuthCookieName, StringComparison.Ordinal))
            .ToList();

    private static int FatPrincipalApproximateBytes() =>
        FatClaims(400).Sum(c => c.Type.Length + c.Value.Length);

    // Stands in for a DfE Sign-in principal: the organisation JSON blob the userinfo endpoint
    // returns, the saved OIDC tokens, and a role claim per grant.
    private static List<Claim> FatClaims(int roleClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Email, "probe@example.gov.uk"),
            new("organisation", new string('o', 1200)),
        };
        claims.AddRange(Enumerable.Range(0, roleClaims)
            .Select(i => new Claim(ClaimTypes.Role, $"cypmd_role_{i}")));
        return claims;
    }

    private static async Task<IHost> BuildHostAsync(int roleClaims = 400)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddDistributedMemoryCache();
                    services.AddDataProtection();
                    services.AddCpdAuthenticationTicketStore();
                    services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                        .AddCookie();
                    services.AddAuthorization();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/sign-me-in", async ctx =>
                        {
                            var identity = new ClaimsIdentity(
                                FatClaims(roleClaims), CookieAuthenticationDefaults.AuthenticationScheme);
                            var properties = new AuthenticationProperties();
                            // Mirrors SaveTokens: the OIDC handler stashes these on the ticket.
                            properties.StoreTokens(
                            [
                                new AuthenticationToken { Name = "id_token", Value = new string('j', 900) },
                                new AuthenticationToken { Name = "access_token", Value = new string('a', 900) },
                                new AuthenticationToken { Name = "refresh_token", Value = new string('r', 400) },
                            ]);
                            await ctx.SignInAsync(
                                CookieAuthenticationDefaults.AuthenticationScheme,
                                new ClaimsPrincipal(identity),
                                properties);
                        });

                        endpoints.MapGet("/sign-me-out", async ctx =>
                            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme));

                        endpoints.MapGet("/who-am-i", async ctx =>
                        {
                            if (ctx.User?.Identity?.IsAuthenticated != true)
                            {
                                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                return;
                            }
                            await ctx.Response.WriteAsync(
                                ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? "(no email)");
                        });
                    });
                });
            })
            .StartAsync();

        return host;
    }
}
