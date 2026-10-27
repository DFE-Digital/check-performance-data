using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Antiforgery;
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

// AntiforgeryOptions.Cookie.SecurePolicy = Always is only safe when the request is known to be
// HTTPS, otherwise DefaultAntiforgery.CheckSSLConfig throws. These prove both halves over the
// real pipeline: the cookie comes out Secure when the edge's X-Forwarded-Proto says https, and
// the plain-HTTP 500 guard is live (which is exactly why the trust fix must land first).
public sealed class AntiforgeryCookieSecureTests
{
    [Fact]
    public async Task ForwardedHttpsRequest_MintsASecureAntiforgeryCookie()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery-probe");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "public.example");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("secure", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlainHttpRequest_IsRefusedByTheSecureCookieGuard()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/antiforgery-probe");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(nameof(InvalidOperationException), body);
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.AddCpdCoreWeb();
        builder.Services.AddDataProtection();
        builder.WebHost.UseTestServer();

        builder.Services.AddAuthentication(AlwaysSignedInHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AlwaysSignedInHandler>(
                AlwaysSignedInHandler.SchemeName, _ => { });

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
            await next();
        });
        app.UseForwardedHeaders();

        app.Run(async context =>
        {
            try
            {
                context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                await context.Response.WriteAsync(context.Response.Headers.SetCookie.ToString());
            }
            catch (Exception ex)
            {
                await context.Response.WriteAsync(ex.GetType().Name);
            }
        });

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
