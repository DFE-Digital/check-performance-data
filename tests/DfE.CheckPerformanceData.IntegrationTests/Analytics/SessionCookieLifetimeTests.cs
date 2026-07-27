using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    // Landmine #2: Cookie.MaxAge is a browser-side Set-Cookie hint. A replayed cookie past
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
    // in place — the extension is a superset, not a replacement.
    [Fact]
    public void CookieHardening_HttpOnlyEssentialAndSecureAlways_ArePreserved()
    {
        var options = BuildSessionOptions(new Dictionary<string, string?>());

        Assert.True(options.Cookie.HttpOnly);
        Assert.True(options.Cookie.IsEssential);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    private static SessionOptions BuildSessionOptions(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddCpdSession(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SessionOptions>>().Value;
    }
}
