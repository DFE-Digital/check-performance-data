using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Locks in the server-side absolute-lifetime enforcement. The middleware:
//   - Commits the ASP.NET session cookie on first access by writing a start-key into the
//     session backing store — reading Session.Id alone would never materialise the cookie
//   - Clears the session when the absolute cap elapses, EVEN under continuous activity
//     (sliding IdleTimeout alone would keep renewing forever)
//   - Clamps the configured cap at a code-level hard max so a misconfigured setting
//     cannot disable the ceiling entirely
public sealed class SessionAbsoluteLifetimeMiddlewareTests
{
    [Fact]
    public async Task FirstRequest_SetsSessionCookie()
    {
        using var host = await BuildProbeHostAsync(absoluteHours: 24);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/_probe/session-id");

        Assert.True(response.Headers.Contains("Set-Cookie"),
            "First request must set the .AspNetCore.Session cookie via the middleware's SetString call.");
        var cookies = string.Join("; ", response.Headers.GetValues("Set-Cookie"));
        Assert.Contains(".AspNetCore.Session", cookies);
    }

    [Fact]
    public async Task SecondRequest_WithSameCookie_WithinLimit_YieldsSameSessionId()
    {
        using var host = await BuildProbeHostAsync(absoluteHours: 1);
        var client = host.GetTestClient();

        var first = await client.GetAsync("/_probe/session-id");
        var firstId = await first.Content.ReadAsStringAsync();
        var cookie = ExtractSessionCookie(first);

        var req = new HttpRequestMessage(HttpMethod.Get, "/_probe/session-id");
        req.Headers.Add("Cookie", cookie);
        var second = await client.SendAsync(req);
        var secondId = await second.Content.ReadAsStringAsync();

        Assert.False(string.IsNullOrEmpty(firstId));
        Assert.Equal(firstId, secondId);
    }

    // Timing test — the fractional-hours override drops the cap into sub-second range so
    // the test can prove the "even under continuous activity" invariant without waiting
    // 24 real hours.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task SecondRequest_AfterAbsoluteLimit_ClearsSessionEvenUnderContinuousActivity()
    {
        // Cap ~ 1.8 seconds (0.0005 h). Delay 2.5 s between requests to cross it.
        using var host = await BuildProbeHostAsync(absoluteHours: 0.0005);
        var client = host.GetTestClient();

        var first = await client.GetAsync("/_probe/session-id");
        var firstId = await first.Content.ReadAsStringAsync();
        var cookie = ExtractSessionCookie(first);

        await Task.Delay(TimeSpan.FromMilliseconds(2500));

        var req = new HttpRequestMessage(HttpMethod.Get, "/_probe/session-id");
        req.Headers.Add("Cookie", cookie);
        var second = await client.SendAsync(req);
        var secondId = await second.Content.ReadAsStringAsync();

        Assert.False(string.IsNullOrEmpty(firstId));
        Assert.False(string.IsNullOrEmpty(secondId));
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void ConfiguredAbsoluteHours_AreClampedAtHardMaximum()
    {
        // Hard max is 168 hours (1 week). Ask for 999 and the middleware should
        // silently clamp — a misconfigured setting cannot disable the cap.
        var clamped = SessionAbsoluteLifetimeMiddleware.ResolveAbsoluteHours(999.0);
        Assert.Equal(168.0, clamped);

        // Below-range values clamp up so a zero or negative setting cannot spin the
        // clear-and-restart loop on every request.
        var clampedLow = SessionAbsoluteLifetimeMiddleware.ResolveAbsoluteHours(-1.0);
        Assert.Equal(0.0001, clampedLow);

        // Reasonable values pass through untouched.
        Assert.Equal(24.0, SessionAbsoluteLifetimeMiddleware.ResolveAbsoluteHours(24.0));
    }

    private static async Task<IHost> BuildProbeHostAsync(double absoluteHours)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();

                web.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SearchAnalytics:SessionAbsoluteHours"] =
                            absoluteHours.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture)
                    });
                });

                web.ConfigureServices((ctx, services) =>
                {
                    // In-memory session backing store keeps the test self-contained (no
                    // Postgres or Redis needed to exercise the middleware).
                    services.AddDistributedMemoryCache();
                    services.AddCpdSession(ctx.Configuration);
                });

                web.Configure(app =>
                {
                    app.UseSession();
                    app.UseMiddleware<SessionAbsoluteLifetimeMiddleware>();
                    app.Run(async context =>
                    {
                        await context.Session.LoadAsync();
                        await context.Response.WriteAsync(context.Session.Id ?? "no-session");
                    });
                });
            })
            .StartAsync();

        return host;
    }

    private static string ExtractSessionCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.Contains("Set-Cookie"), "Expected a Set-Cookie header.");
        foreach (var raw in response.Headers.GetValues("Set-Cookie"))
        {
            if (raw.StartsWith(".AspNetCore.Session=", StringComparison.Ordinal))
            {
                // Cookie header is just name=value; drop everything after the first ";".
                var end = raw.IndexOf(';');
                return end < 0 ? raw : raw[..end];
            }
        }

        throw new Xunit.Sdk.XunitException("No .AspNetCore.Session cookie in response.");
    }
}
