using System.Web;
using DfE.CheckPerformanceData.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// PostChallengeReturnUrl only helps if the OIDC handler carries the changed RedirectUri into the
// state it sends to DfE Sign-in. These run the real handler, wired by the real registration, and
// read the return address back out of the protected state — the value the user is sent to after
// signing in.
public sealed class PostChallengeRedirectWiringTests
{
    private const string Host = "check-performance-data.education.gov.uk";
    private const string NextStep = "/CheckYourPupilData/3b40f427-4ef4-492b-b1b3-3acda14765e5/nextstep";
    private const string FormPage = "/CheckYourPupilData/3b40f427-4ef4-492b-b1b3-3acda14765e5";

    [Fact]
    public async Task ChallengedPost_ReturnsToThePageTheFormWasOn()
    {
        var returnUrl = await ReturnUrlAfterChallenge("POST", NextStep, referer: $"https://{Host}{FormPage}");

        Assert.Equal(FormPage, returnUrl);
    }

    [Fact]
    public async Task ChallengedPost_WithoutAReferer_ReturnsToTheLandingPage()
    {
        var returnUrl = await ReturnUrlAfterChallenge("POST", NextStep, referer: null);

        Assert.Equal("/LandingPage", returnUrl);
    }

    [Fact]
    public async Task ChallengedGet_StillReturnsToItself()
    {
        var returnUrl = await ReturnUrlAfterChallenge("GET", FormPage, referer: $"https://{Host}/LandingPage");

        Assert.Equal(FormPage, returnUrl);
    }

    [Fact]
    public async Task ChallengedPost_WithItsOwnRedirectUri_KeepsIt()
    {
        var returnUrl = await ReturnUrlAfterChallenge(
            "POST", NextStep, referer: $"https://{Host}{FormPage}",
            new AuthenticationProperties { RedirectUri = "/Account/Chosen" });

        Assert.Equal("/Account/Chosen", returnUrl);
    }

    private static async Task<string?> ReturnUrlAfterChallenge(
        string method, string path, string? referer, AuthenticationProperties? properties = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDistributedMemoryCache();
        services.AddDfeSignInAuthentication(DfeSignInConfig());
        // No metadata fetch: hand the handler its endpoints directly. The handler's own
        // post-configure has already built a fetching manager by now, so replace the manager.
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o =>
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { AuthorizationEndpoint = "https://oidc.signin.example/auth" }));
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(Host);
        context.Request.Path = path;
        if (referer is not null) context.Request.Headers.Referer = referer;

        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);

        var location = new Uri(context.Response.Headers.Location.ToString());
        Assert.Equal("oidc.signin.example", location.Host);
        var state = HttpUtility.ParseQueryString(location.Query)["state"];

        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        return options.StateDataFormat.Unprotect(state)?.RedirectUri;
    }

    private static IConfiguration DfeSignInConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DfeSignIn:ApiClientSecret"] = "not-a-real-secret",
                ["DfeSignIn:ClientId"] = "CheckPerformanceData",
                ["DfeSignIn:Audience"] = "signin.education.gov.uk",
                ["DfeSignIn:MetadataAddress"] = "https://oidc.signin.example/.well-known/openid-configuration",
                ["DfeSignIn:ClientSecret"] = "not-a-real-secret",
                ["DfeSignIn:RequireHttpsMetadata"] = "true",
            })
            .Build();
}
