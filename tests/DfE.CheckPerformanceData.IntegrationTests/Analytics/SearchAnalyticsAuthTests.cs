using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// HTTP-level assertions on the search-analytics admin landing route. The RequireAdminSection
// filter chains anonymous requests through the standard sign-in challenge (302 to /sign-in),
// short-circuits editor-only users to 404 (hides the URL from discovery), and passes admins
// through. This test class asserts the two rejection branches at the pipeline level so a future
// refactor cannot silently open the URL to unauthenticated or under-granted users. The
// admin-pass-through branch is covered by SearchAnalyticsControllerTests, which exercises the
// controller action directly and asserts on the returned ViewResult + model — the Web assembly's
// pre-compiled Razor views are not discoverable from the IntegrationTests TestServer without a
// full layout dependency graph, so admin-pass-through does not run through this HostBuilder.
[Collection(nameof(PostgresCollection))]
public sealed class SearchAnalyticsAuthTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsAuthTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Anonymous users are redirected to the sign-in challenge ---

    [Fact]
    public async Task AnonymousRequest_RedirectsToSignIn()
    {
        using var host = await BuildHostAsync(TestPrincipal.Anonymous);
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/admin/Search/");

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect
                or HttpStatusCode.Found
                or HttpStatusCode.TemporaryRedirect,
            $"Expected a 302-family redirect but got {(int)response.StatusCode}.");
    }

    // --- Editor-only users get 404 (hides the URL from discovery) ---

    [Fact]
    public async Task EditorOnlyRequest_Returns404()
    {
        using var host = await BuildHostAsync(TestPrincipal.EditorOnly);
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/admin/Search/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private enum TestPrincipal
    {
        Anonymous,
        EditorOnly,
    }

    private async Task<IHost> BuildHostAsync(TestPrincipal principal)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                // Real DbContext so the controller ctor + query service resolve cleanly.
                services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
                services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
                services.AddScoped<ISearchAnalyticsQueryService, SearchAnalyticsQueryService>();

                services.AddScoped<ISettingService>(_ =>
                {
                    var s = Substitute.For<ISettingService>();
                    s.GetIntAsync(Arg.Any<string>()).Returns(10);
                    return s;
                });

                // Anonymous requests fail the RequireAdminSection filter's identity check and
                // get a ChallengeResult, which needs an authentication scheme to translate into
                // the 302. Register the standard cookie scheme (login path only) so the anonymous
                // branch redirects — it never actually authenticates because the test principal
                // is anonymous.
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.Configure<TestAuthOptions>(TestAuthHandler.SchemeName, o => o.Principal = principal);
                services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
                {
                    o.LoginPath = "/sign-in";
                });
                services.AddAuthorization();

                // Editor-only users must fail the section grant. Stub the policy to say NO for
                // the search-analytics key when the principal is the editor-only one, and YES
                // for anyone else (only used if a future test wires an admin principal here).
                services.AddScoped<IAdminAccessPolicy>(_ =>
                {
                    var policy = Substitute.For<IAdminAccessPolicy>();
                    policy.CanAccessAsync(
                            Arg.Is<ClaimsPrincipal>(p => p.HasClaim(ClaimTypes.Role, "cypmd_editor") && !p.HasClaim(ClaimTypes.Role, "cypmd_admin")),
                            Arg.Any<string>())
                        .Returns(false);
                    policy.CanAccessAsync(
                            Arg.Is<ClaimsPrincipal>(p => p.HasClaim(ClaimTypes.Role, "cypmd_admin")),
                            Arg.Any<string>())
                        .Returns(true);
                    return policy;
                });

                services.AddControllers()
                    .AddApplicationPart(typeof(SearchAnalyticsController).Assembly);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        });

        return await builder.StartAsync();
    }

    private sealed class TestAuthOptions : AuthenticationSchemeOptions
    {
        public TestPrincipal Principal { get; set; }
    }

    // Issues an authenticated editor-only principal when configured; otherwise reports NoResult
    // so the request is anonymous and [RequireAdminSection] short-circuits to a ChallengeResult.
    private sealed class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
    {
        public const string SchemeName = "TestScheme";

        public TestAuthHandler(
            IOptionsMonitor<TestAuthOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.Principal == TestPrincipal.Anonymous)
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = Options.Principal switch
            {
                TestPrincipal.EditorOnly => new[]
                {
                    new Claim(ClaimTypes.Name, "editor"),
                    new Claim(ClaimTypes.Role, "cypmd_editor"),
                },
                _ => Array.Empty<Claim>(),
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // The RequireAdminSection filter returns a ChallengeResult for anonymous requests. The
        // ChallengeResult resolves against the *default* scheme, so redirect the challenge here
        // to a plain 302 pointing at /sign-in — that is the pipeline shape production uses when
        // DfE Sign-In is unavailable; the important assertion is "302 to a sign-in path", not
        // the exact URL.
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.Location = "/sign-in";
            Response.StatusCode = (int)HttpStatusCode.Redirect;
            return Task.CompletedTask;
        }
    }
}
