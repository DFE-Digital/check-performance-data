using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.Web;

// The unit test pins the options object; this proves the pipeline actually uses it. A stock
// TestServer request has a null RemoteIpAddress, which makes ForwardedHeadersMiddleware skip
// the known-peer check entirely and hide the bug — so the first middleware sets a
// non-loopback peer, reproducing the deployed ingress hop faithfully. ForwardedHeadersOptions
// come from the production AddCpdCoreWeb, so the test cannot drift from what ships.
public sealed class IngressForwardedHeadersTests
{
    [Fact]
    public async Task ForwardedHttpsFromANonLoopbackPeer_IsTrusted()
    {
        using var host = await BuildHostAsync(Environments.Production, forwardHsts: false);
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "public.example");

        using var response = await client.SendAsync(request);

        Assert.Equal("https|public.example", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ARequestWithNoForwardedHeaders_StaysPlainHttp()
    {
        using var host = await BuildHostAsync(Environments.Production, forwardHsts: false);
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/probe");

        Assert.StartsWith("http|", await response.Content.ReadAsStringAsync());
    }

    // The app only emits its own HSTS once it believes the request is HTTPS; before the trust
    // fix this header was absent in the pod (the dev HSTS came from the edge).
    [Fact]
    public async Task NonDevelopmentPipeline_EmitsItsOwnHstsForForwardedHttps()
    {
        using var host = await BuildHostAsync(Environments.Production, forwardHsts: true);
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "public.example");

        using var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task NonDevelopmentPipeline_DoesNotEmitHstsForAPlainHttpRequest()
    {
        using var host = await BuildHostAsync(Environments.Production, forwardHsts: true);
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/probe");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static async Task<IHost> BuildHostAsync(string environmentName, bool forwardHsts)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.AddCpdCoreWeb();
        builder.WebHost.UseTestServer();

        // AddCpdCoreWeb installs a fallback policy requiring an authenticated user; a scheme
        // that always succeeds satisfies it without dragging DfE Sign-In into the test (same
        // pattern as TempDataStorageTests).
        builder.Services.AddAuthentication(AlwaysSignedInHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AlwaysSignedInHandler>(
                AlwaysSignedInHandler.SchemeName, _ => { });

        var app = builder.Build();

        // The deployed hop: a non-loopback immediate peer, so the known-peer check actually runs.
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
            await next();
        });
        app.UseForwardedHeaders();
        if (forwardHsts)
        {
            app.UseHsts();
        }

        app.Run(async context =>
            await context.Response.WriteAsync($"{context.Request.Scheme}|{context.Request.Host.Value}"));

        await app.StartAsync();
        return app;
    }

    private sealed class AlwaysSignedInHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AlwaysSignedIn";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "probe")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
