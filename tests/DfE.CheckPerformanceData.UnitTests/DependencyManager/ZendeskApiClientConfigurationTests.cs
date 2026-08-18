using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace DfE.CheckPerformanceData.Application.UnitTests.DependencyManager;

// The worker builds its Zendesk client's base address by interpolating Subdomain and Domain, so
// blank values produce "https://..com" — not a parseable hostname. That surfaced as a
// UriFormatException thrown while the host resolved its hosted services, which killed the
// process on startup and said nothing about which setting was missing. On a fresh clone with an
// unpopulated .env that is the default state, so the worker could not start at all.
//
// Two behaviours have to hold together: an environment using the fake Zendesk service must start
// regardless (that is the whole point of the fake defaulting on), and an environment that really
// does want the client must be told precisely what is missing.
public class ZendeskApiClientConfigurationTests
{
    private static IConfiguration ConfigWith(
        string? subdomain = "dfe", string? domain = "zendesk",
        string? email = "cypmd@education.gov.uk", string? apiToken = "token")
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ZendeskSettings:Subdomain"] = subdomain,
            ["ZendeskSettings:Domain"] = domain,
            ["ZendeskSettings:Email"] = email,
            ["ZendeskSettings:ApiToken"] = apiToken,
        }).Build();

    private static IServiceProvider Build(IConfiguration config, bool requireRealClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZendeskApiClient(config, requireRealClient);
        return services.BuildServiceProvider();
    }

    // The case that crash-looped: blank settings, fake in use. Resolving the client is what the
    // host does at startup, so this reproduces the original failure exactly.
    [Fact]
    public void BlankSettings_WithTheFakeInUse_StillBuildsAResolvableClient()
    {
        var provider = Build(ConfigWith(subdomain: "", domain: "", email: "", apiToken: ""),
            requireRealClient: false);

        var client = provider.GetRequiredService<IZendeskApi>();

        Assert.NotNull(client);
    }

    [Fact]
    public void BlankSettings_WhenTheRealClientIsWanted_FailsNamingEverySetting()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(
                ConfigWith(subdomain: "", domain: "", email: "", apiToken: ""),
                requireRealClient: true));

        Assert.Contains("Subdomain", ex.Message);
        Assert.Contains("Domain", ex.Message);
        Assert.Contains("Email", ex.Message);
        Assert.Contains("ApiToken", ex.Message);
        // The message has to say what to do about it, not merely what is wrong.
        Assert.Contains("Zendesk:UseFake", ex.Message);
    }

    // A partially-configured environment is the likelier real-world case, and the message is
    // only useful if it names the one that is actually missing.
    [Fact]
    public void OneBlankSetting_IsReportedOnItsOwn()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(ConfigWith(domain: "   "), requireRealClient: true));

        Assert.Contains("Domain", ex.Message);
        Assert.DoesNotContain("Subdomain", ex.Message);
        Assert.DoesNotContain("ApiToken", ex.Message);
    }

    // Resolving is where the base address gets built, so a client that resolves is a client
    // whose address parsed — which is the whole of what broke.
    [Fact]
    public void CompleteSettings_ResolveWithoutThrowing()
    {
        var provider = Build(ConfigWith(), requireRealClient: true);

        Assert.NotNull(provider.GetRequiredService<IZendeskApi>());
    }

    // Whitespace is not configuration. An unset compose variable substitutes to empty, and a
    // half-filled settings file often leaves a stray space.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhitespaceCountsAsMissing(string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(ConfigWith(subdomain: value), requireRealClient: true));
    }

    // A missing section is a different failure from a blank one and keeps its own message.
    [Fact]
    public void MissingSection_StillReportsTheSectionRatherThanTheFields()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var empty = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(empty, requireRealClient: true));

        Assert.Contains("section is missing", ex.Message);
    }
}
