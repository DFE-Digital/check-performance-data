using DfE.CheckPerformanceData.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// The auth cookie leaves the pod, so outside Development it must carry Secure; Development keeps
// SameAsRequest so the local http stack still works. The OIDC correlation and nonce cookies are
// different: they are SameSite=None, which browsers reject unless Secure is also set, so they stay
// Always Secure in every environment (localhost's secure-context exception lets a Secure cookie
// through over http). The environment is supplied by the caller (Program passes
// builder.Environment) rather than read from a static, so a test can pin both branches without
// touching process state.
public sealed class DfeSignInCookiePolicyTests
{
    [Theory]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    public void AuthCookie_SecurePolicyFollowsEnvironment(string environmentName, CookieSecurePolicy expected)
    {
        var options = AuthOptions(environmentName);

        Assert.Equal(expected, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void OidcCorrelationAndNonceCookies_AreAlwaysSecure(string environmentName)
    {
        var options = OidcOptions(environmentName);

        // SameSite=None cookies are rejected by browsers unless Secure is set. Downgrading them in
        // Development (as the auth cookie is) drops them on the OIDC callback, so correlation
        // validation fails and the handshake ends on the NoAccess page.
        Assert.Equal(CookieSecurePolicy.Always, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, options.NonceCookie.SecurePolicy);
        Assert.True(options.CorrelationCookie.HttpOnly);
        Assert.True(options.NonceCookie.HttpOnly);
        Assert.Equal(SameSiteMode.None, options.CorrelationCookie.SameSite);
        Assert.Equal(SameSiteMode.None, options.NonceCookie.SameSite);
    }

    private static CookieAuthenticationOptions AuthOptions(string environmentName) =>
        BuildProvider(environmentName)
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

    private static OpenIdConnectOptions OidcOptions(string environmentName) =>
        BuildProvider(environmentName)
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

    private static ServiceProvider BuildProvider(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDistributedMemoryCache();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        services.AddDfeSignInAuthentication(MinimalDfeSignInConfig(), environment);
        return services.BuildServiceProvider();
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
