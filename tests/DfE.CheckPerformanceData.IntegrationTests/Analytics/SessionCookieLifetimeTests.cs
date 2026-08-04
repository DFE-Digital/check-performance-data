using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Locks in the session-cookie shape the analytics work depends on:
//   - IdleTimeout is settings-driven with a 60-minute default (support-team-quotable
//     lifetime; matches the 1-hour idle called out in the storage-foundation plan)
//   - IdleTimeout honours the SearchAnalytics:SessionIdleMinutes override
//   - Cookie.MaxAge is deliberately NOT set — that's a browser hint only; the server-
//     side absolute-lifetime cap lives in SessionAbsoluteLifetimeMiddleware (test 3b)
[Trait("Category", "W0")]
public sealed class SessionCookieLifetimeTests
{
    [Fact]
    public void IdleTimeout_DefaultsToOneHour_WhenNoConfigOverride()
    {
        var options = BuildSessionOptions(new Dictionary<string, string?>());

        Assert.Equal(TimeSpan.FromMinutes(60), options.IdleTimeout);
    }

    [Fact]
    public void IdleTimeout_HonoursConfigOverride()
    {
        var options = BuildSessionOptions(new Dictionary<string, string?>
        {
            ["SearchAnalytics:SessionIdleMinutes"] = "15"
        });

        Assert.Equal(TimeSpan.FromMinutes(15), options.IdleTimeout);
    }

    // Subtle trap: Cookie.MaxAge is a browser-side Set-Cookie hint. A replayed cookie past
    // its MaxAge still hits a live server-side session that the sliding idle timeout would
    // keep refreshing. Server-side enforcement of the absolute lifetime lives in
    // SessionAbsoluteLifetimeMiddleware — this test guards against a well-meaning refactor
    // that pushes MaxAge back into the AddSession options block.
    [Fact]
    public void CookieMaxAge_IsNotSetByTheAddCpdSessionExtension()
    {
        var options = BuildSessionOptions(new Dictionary<string, string?>
        {
            ["SearchAnalytics:SessionAbsoluteHours"] = "24"
        });

        Assert.Null(options.Cookie.MaxAge);
    }

    // The other cookie hygiene bits Program.cs relied on before this refactor must stay
    // in place in every environment — the extension is a superset, not a replacement.
    [Fact]
    public void CookieHardening_HttpOnlyAndEssential_ArePreservedInEveryEnvironment()
    {
        foreach (var env in new[] { Environments.Development, Environments.Production, "Staging" })
        {
            var options = BuildSessionOptions(new Dictionary<string, string?>(), env);

            Assert.True(options.Cookie.HttpOnly);
            Assert.True(options.Cookie.IsEssential);
        }
    }

    // Production locks the cookie to secure-only — a replayed http request against a real
    // staging/production host cannot leak the cookie. Development downgrades to
    // SameAsRequest so a plain-http local dev host (docker exposing :8080) can round-trip
    // the cookie for browsers hitting the container over http (host.docker.internal in
    // particular does NOT qualify for Chromium's localhost secure-context exemption).
    [Fact]
    public void CookieSecurePolicy_IsAlwaysOutsideDevelopment_AndSameAsRequestInDevelopment()
    {
        var devOptions = BuildSessionOptions(new Dictionary<string, string?>(), Environments.Development);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, devOptions.Cookie.SecurePolicy);

        foreach (var env in new[] { Environments.Production, Environments.Staging })
        {
            var options = BuildSessionOptions(new Dictionary<string, string?>(), env);
            Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        }
    }

    private static SessionOptions BuildSessionOptions(
        IDictionary<string, string?> settings,
        string environmentName = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var environment = new StubWebHostEnvironment(environmentName);

        var services = new ServiceCollection();
        services.AddCpdSession(configuration, environment);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SessionOptions>>().Value;
    }

    // Minimal IWebHostEnvironment stub — only the EnvironmentName is read by the
    // extension. Everything else is inert but non-null to satisfy the interface.
    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "cpd-tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
