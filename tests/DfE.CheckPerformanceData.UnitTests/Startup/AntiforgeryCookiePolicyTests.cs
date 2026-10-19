using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// DefaultAntiforgery.CheckSSLConfig throws (a 500) when the cookie is Always and the request is
// not HTTPS, so this policy can only be set once the ingress's X-Forwarded-Proto is trusted
// (see ForwardedHeadersTrustTests). These pin the policy and, just as importantly, that nothing
// else about the cookie moved.
public sealed class AntiforgeryCookiePolicyTests
{
    [Fact]
    public void NonDevelopment_MarksTheCookieAlwaysSecure()
    {
        var options = OptionsFor(Environments.Production);

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void Development_LetsTheCookieFollowTheRequest()
    {
        var options = OptionsFor(Environments.Development);

        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void TheOtherCookieAttributesAreUnchanged()
    {
        var options = OptionsFor(Environments.Production);

        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
    }

    private static AntiforgeryOptions OptionsFor(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.AddCpdCoreWeb();

        return builder.Services.BuildServiceProvider()
            .GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
    }
}
