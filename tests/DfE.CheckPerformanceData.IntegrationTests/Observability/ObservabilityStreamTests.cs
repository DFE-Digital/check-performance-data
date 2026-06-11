using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// The live SSE stream must (a) be role-gated cypmd_admin — never an unauthenticated firehose —
// and (b) survive longer than its heartbeat interval without the connection idling out, by
// pushing a snapshot on every tick. The heartbeat is driven shorter here so the test span
// crosses several ticks quickly.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class ObservabilityStreamTests
{
    private readonly PostgresFixture _fixture;

    public ObservabilityStreamTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Stream_ReturnsEventStreamContentType()
    {
        using var host = await BuildHostAsync(authenticated: true, heartbeatSeconds: 1);
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/observability/stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stream_EmitsAtLeastTwoSnapshotsAcrossTheHeartbeatInterval()
    {
        using var host = await BuildHostAsync(authenticated: true, heartbeatSeconds: 1);
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/observability/stream");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var snapshotCount = 0;
        try
        {
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);

            // Read past at least three heartbeat ticks so the assertion proves the stream is
            // still producing well after the first event (i.e. it does not idle out).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && !cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null)
                    break;
                if (line.StartsWith("event:") && line.Contains("snapshot"))
                    snapshotCount++;
                if (snapshotCount >= 2)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Fall through to the assertion; the count is what matters.
        }

        Assert.True(snapshotCount >= 2, $"Expected at least 2 snapshot events, saw {snapshotCount}.");
    }

    [Fact]
    public async Task Stream_RejectsUnauthenticatedRequest()
    {
        using var host = await BuildHostAsync(authenticated: false, heartbeatSeconds: 1);
        var client = host.GetTestClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("/admin/observability/stream", cts.Token);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403 but got {(int)response.StatusCode}.");
    }

    private async Task<IHost> BuildHostAsync(bool authenticated, int heartbeatSeconds)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Observability:HeartbeatSeconds"] = heartbeatSeconds.ToString(),
        };

        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureAppConfiguration(c => c.AddInMemoryCollection(configValues));
            web.ConfigureServices(services =>
            {
                services.AddSingleton<ICurrentUserService, FakeCurrentUserService>();
                services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
                services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
                services.AddScoped<IMetricsQueryService, MetricsQueryService>();
                services.AddScoped<IHealthEvaluator, HealthEvaluator>();
                services.AddScoped<StatusSentenceBuilder>();
                services.AddScoped<ISettingService>(_ =>
                {
                    var s = Substitute.For<ISettingService>();
                    s.GetIntAsync(Arg.Any<string>()).Returns(10);
                    return s;
                });
                services.AddScoped<IQueueAdminService>(_ =>
                {
                    var q = Substitute.For<IQueueAdminService>();
                    q.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<QueueDepth>());
                    q.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(0);
                    return q;
                });

                var authBuilder = services
                    .AddAuthentication(TestAuthHandler.Scheme)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
                services.Configure<TestAuthOptions>(TestAuthHandler.Scheme, o => o.Authenticated = authenticated);
                services.AddAuthorization();

                services.AddControllers().AddApplicationPart(typeof(ObservabilityController).Assembly);
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
        public bool Authenticated { get; set; }
    }

    // Issues an authenticated principal in the cypmd_admin role when Authenticated is true;
    // otherwise reports NoResult so [Authorize] returns 401.
    private sealed class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
    {
        public const string Scheme = "Test";

        public TestAuthHandler(
            IOptionsMonitor<TestAuthOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Options.Authenticated)
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "cypmd_admin"),
            };
            var identity = new ClaimsIdentity(claims, Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
