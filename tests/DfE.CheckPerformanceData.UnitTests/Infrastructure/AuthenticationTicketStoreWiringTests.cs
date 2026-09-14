using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// A ticket store that is implemented but never attached to the cookie scheme changes nothing.
// These pin the wiring itself: the real registration the application calls must come out with
// SessionStore set, or the ticket goes back into the cookie and the ingress budget goes with it.
public sealed class AuthenticationTicketStoreWiringTests
{
    [Fact]
    public void AddDfeSignInAuthentication_HoldsTheTicketServerSide()
    {
        var options = CookieOptionsFrom(services =>
            services.AddDfeSignInAuthentication(MinimalDfeSignInConfig()));

        Assert.NotNull(options.SessionStore);
        Assert.IsType<DistributedCacheTicketStore>(options.SessionStore);
    }

    [Fact]
    public void AddCpdAuthenticationTicketStore_AttachesTheStoreToTheCookieScheme()
    {
        var options = CookieOptionsFrom(services =>
        {
            services.AddCpdAuthenticationTicketStore();
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        });

        Assert.IsType<DistributedCacheTicketStore>(options.SessionStore);
    }

    private static CookieAuthenticationOptions CookieOptionsFrom(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDistributedMemoryCache();
        register(services);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // Only the values AddDfeSignInAuthentication insists on; the handshake is never performed here.
    private static IConfiguration MinimalDfeSignInConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DfeSignIn:ApiClientSecret"] = "not-a-real-secret",
                ["DfeSignIn:ClientId"] = "CheckPerformanceData",
                ["DfeSignIn:Audience"] = "signin.education.gov.uk",
                ["DfeSignIn:MetadataAddress"] = "https://example.invalid/.well-known/openid-configuration",
                ["DfeSignIn:ClientSecret"] = "not-a-real-secret",
            })
            .Build();
}
