using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using InfrastructureDependencyManager = DfE.CheckPerformanceData.Infrastructure.DependencyManager;

namespace DfE.CheckPerformanceData.Application.UnitTests.DependencyManager;

// The worker builds its Zendesk client's base address by interpolating Subdomain and Domain, so
// blank values produce "https://..com" — not a parseable hostname. That surfaced as a
// UriFormatException thrown while the host resolved its hosted services, which killed the
// process on startup and said nothing about which setting was missing. On a fresh clone with an
// unpopulated .env that is the default state, so the worker could not start at all.
//
// Three behaviours have to hold together. An environment using the fake Zendesk service must
// start regardless — that is the whole point of the fake defaulting on. An environment that
// really does want the client must be told precisely which of the two hostname settings is
// missing, because without them there is no client to build. And a missing credential must not
// take the host down: the rules consumer, the DLQ, metrics, search-analytics and content-staging
// retention jobs all share this process, and none of them talks to Zendesk.
public class ZendeskApiClientConfigurationTests
{
    // What an unfilled user-secrets file carries. .env.example uses the angle-bracket form.
    private const string Placeholder = "[PLACE THESE IN YOUR USER SECRETS]";
    private const string EnvExamplePlaceholder = "<zendesk-api-token>";

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

    private static ZendeskSettings SettingsWith(
        string subdomain = "dfe", string domain = "zendesk",
        string email = "cypmd@education.gov.uk", string apiToken = "token")
        => new() { Subdomain = subdomain, Domain = domain, Email = email, ApiToken = apiToken };

    private static IServiceProvider Build(IConfiguration config, bool requireRealClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZendeskApiClient(config, requireRealClient);
        return services.BuildServiceProvider();
    }

    // Starts every hosted service the registration added and returns the warnings they logged.
    // The credential warning is delivered that way because registration runs before the host
    // exists, so there is no logger to write to at the point the settings are read.
    private static async Task<IReadOnlyList<string>> WarningsAtStartup(
        IConfiguration config, bool requireRealClient)
    {
        var captured = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(captured));
        services.AddZendeskApiClient(config, requireRealClient);

        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        return captured.Warnings;
    }

    // The case that crash-looped: blank settings, fake in use. Resolving the client is what the
    // host does at startup, so this reproduces the original failure exactly.
    [Fact]
    public void BlankSettings_WithTheFakeInUse_StillBuildAResolvableClient()
    {
        var provider = Build(ConfigWith(subdomain: "", domain: "", email: "", apiToken: ""),
            requireRealClient: false);

        var client = provider.GetRequiredService<IZendeskApi>();

        Assert.NotNull(client);
    }

    // Subdomain and Domain are the two that get interpolated into the base address, so they are
    // the two the error is about. Naming the credentials here would be wrong twice over: they
    // have nothing to do with the URI, and reporting them implies the host stopped for them.
    [Fact]
    public void BlankHostSettings_WhenTheRealClientIsWanted_FailNamingOnlyTheHostSettings()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(
                ConfigWith(subdomain: "", domain: "", email: "", apiToken: ""),
                requireRealClient: true));

        Assert.Contains("Subdomain", ex.Message);
        Assert.Contains("Domain", ex.Message);
        Assert.DoesNotContain("Email", ex.Message);
        Assert.DoesNotContain("ApiToken", ex.Message);
    }

    // A partially-configured environment is the likelier real-world case, and the message is
    // only useful if it names the one that is actually missing.
    [Fact]
    public void OneBlankHostSetting_IsReportedOnItsOwn()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(ConfigWith(domain: "   "), requireRealClient: true));

        Assert.Contains("Domain", ex.Message);
        Assert.DoesNotContain("Subdomain", ex.Message);
    }

    // The failure happens in a container or an App Service slot, where configuration arrives as
    // environment variables and the colon form of the key does nothing. An instruction the
    // operator cannot follow where they are reading it is not an instruction.
    [Fact]
    public void TheFailure_NamesBothFormsOfTheUseFakeKey()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(ConfigWith(subdomain: ""), requireRealClient: true));

        Assert.Contains("Zendesk:UseFake", ex.Message);
        Assert.Contains("Zendesk__UseFake", ex.Message);
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

    // The placeholders are not blank, so a blank check waves them through and the host starts
    // pointing at whatever they happen to name. They mean "unset" and have to be read that way,
    // or the fail-fast is defeated by the values a half-filled .env or user-secrets file carries.
    [Theory]
    [InlineData(Placeholder)]
    [InlineData(EnvExamplePlaceholder)]
    [InlineData("<zendesk-subdomain>")]
    [InlineData("the subdomain")]
    [InlineData("THE SUBDOMAIN")]
    [InlineData("  the subdomain  ")]
    public void PlaceholderSubdomain_IsTreatedAsMissing(string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(ConfigWith(subdomain: value), requireRealClient: true));

        Assert.Contains("Subdomain", ex.Message);
    }

    [Theory]
    [InlineData(Placeholder)]
    [InlineData(EnvExamplePlaceholder)]
    [InlineData("the domain")]
    public void PlaceholderDomain_IsTreatedAsMissing(string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddZendeskApiClient(ConfigWith(domain: value), requireRealClient: true));

        Assert.Contains("Domain", ex.Message);
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

    // Resolving is where the base address gets built, so a client that resolves is a client
    // whose address parsed — which is the whole of what broke.
    [Fact]
    public void CompleteSettings_ResolveWithoutThrowing()
    {
        var provider = Build(ConfigWith(), requireRealClient: true);

        Assert.NotNull(provider.GetRequiredService<IZendeskApi>());
    }

    // The point of the split. A rotated-out or not-yet-populated ZendeskSettings__ApiToken is a
    // credential problem: the Zendesk call fails, and nothing else in the process should care.
    // Before the split it took the whole worker down, consumers and retention jobs included.
    [Theory]
    [InlineData("", "")]
    [InlineData(Placeholder, Placeholder)]
    [InlineData("cypmd@education.gov.uk", EnvExamplePlaceholder)]
    [InlineData("cypmd@education.gov.uk", "")]
    public async Task MissingCredentials_DoNotStopTheHostStarting(string email, string apiToken)
    {
        var provider = Build(ConfigWith(email: email, apiToken: apiToken), requireRealClient: true);

        // Resolving the client and starting the hosted services is what the host does at start,
        // and is where the original crash-loop happened. Both have to survive.
        Assert.NotNull(provider.GetRequiredService<IZendeskApi>());
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    // Not stopping the host is only half of it — silence would leave an operator to discover the
    // missing secret from a Zendesk 401 hours later.
    [Fact]
    public async Task MissingCredentials_AreWarnedAboutAtStartup()
    {
        var warnings = await WarningsAtStartup(
            ConfigWith(email: "", apiToken: Placeholder), requireRealClient: true);

        var warning = Assert.Single(warnings);
        Assert.Contains("Email", warning);
        Assert.Contains("ApiToken", warning);
    }

    [Fact]
    public async Task OneMissingCredential_IsWarnedAboutOnItsOwn()
    {
        var warnings = await WarningsAtStartup(ConfigWith(apiToken: ""), requireRealClient: true);

        var warning = Assert.Single(warnings);
        Assert.Contains("ApiToken", warning);
        Assert.DoesNotContain("Email", warning);
    }

    // The warning must carry the name of the setting and never what is in it. The placeholder is
    // the case that can actually leak: it is a reported setting whose value is non-empty, so a
    // warning that interpolated values would print it.
    [Fact]
    public async Task TheWarning_NamesTheSettingsWithoutQuotingTheirValues()
    {
        var warnings = await WarningsAtStartup(
            ConfigWith(email: "cypmd@education.gov.uk", apiToken: Placeholder),
            requireRealClient: true);

        var warning = Assert.Single(warnings);
        Assert.Contains("ApiToken", warning);
        Assert.DoesNotContain(Placeholder, warning);
        Assert.DoesNotContain("cypmd@education.gov.uk", warning);
    }

    [Fact]
    public async Task CompleteSettings_WarnAboutNothing()
    {
        var warnings = await WarningsAtStartup(ConfigWith(), requireRealClient: true);

        Assert.Empty(warnings);
    }

    // With the fake in use there is no credential to be missing, so the warning would be noise
    // in exactly the environment that never talks to Zendesk.
    [Fact]
    public async Task MissingCredentials_WithTheFakeInUse_WarnAboutNothing()
    {
        var warnings = await WarningsAtStartup(
            ConfigWith(subdomain: "", domain: "", email: "", apiToken: ""),
            requireRealClient: false);

        Assert.Empty(warnings);
    }

    [Fact]
    public void CompleteSettings_PointTheClientAtTheConfiguredHost()
    {
        var address = InfrastructureDependencyManager.ZendeskBaseAddress(SettingsWith());

        Assert.Equal(new Uri("https://dfe.zendesk.com"), address);
    }

    // Whitespace around a hostname setting is a paste artefact, not part of the host. Untrimmed
    // it would reach Uri and throw there — the opaque unnamed crash all of this exists to avoid.
    [Fact]
    public void SurroundingWhitespace_IsNotPartOfTheHost()
    {
        var address = InfrastructureDependencyManager.ZendeskBaseAddress(
            SettingsWith(subdomain: " dfe ", domain: " zendesk "));

        Assert.Equal(new Uri("https://dfe.zendesk.com"), address);
    }

    // .invalid is reserved by RFC 2606 and can never resolve, so a call that somehow reached it
    // fails immediately rather than arriving at a real host. Anything the client cannot be used
    // for goes there — including a missing credential, because selecting the fake replaces
    // IZendeskService and nothing else: IZendeskAttachmentService stays the real Refit client,
    // and this address is what stops a half-configured environment reaching Zendesk through it.
    [Theory]
    [InlineData("", "zendesk", "a@b.gov.uk", "token")]
    [InlineData("the subdomain", "zendesk", "a@b.gov.uk", "token")]
    [InlineData(Placeholder, "zendesk", "a@b.gov.uk", "token")]
    [InlineData("dfe", "", "a@b.gov.uk", "token")]
    [InlineData("dfe", "zendesk", "", "token")]
    [InlineData("dfe", "zendesk", "a@b.gov.uk", "")]
    [InlineData("dfe", "zendesk", "a@b.gov.uk", EnvExamplePlaceholder)]
    public void AnySettingMissing_PointsTheClientAtAnUnresolvableHost(
        string subdomain, string domain, string email, string apiToken)
    {
        var address = InfrastructureDependencyManager.ZendeskBaseAddress(
            SettingsWith(subdomain, domain, email, apiToken));

        Assert.Equal("invalid", address.Host.Split('.')[^1]);
    }

    // The fake replacing IZendeskService is not on its own enough to keep an environment away
    // from a real Zendesk, so a fake-mode environment that has a hostname but no credentials
    // must not end up addressing one.
    [Fact]
    public void WithTheFakeInUse_AConfiguredHostnameAndNoCredentials_StillGoNowhere()
    {
        var provider = Build(ConfigWith(email: "", apiToken: ""), requireRealClient: false);
        var address = InfrastructureDependencyManager.ZendeskBaseAddress(
            SettingsWith(email: string.Empty, apiToken: string.Empty));

        Assert.NotNull(provider.GetRequiredService<IZendeskApi>());
        Assert.Equal("invalid", address.Host.Split('.')[^1]);
    }

    // The registration used to assign its lambda parameter and discard it, so IOptions resolved
    // an unbound instance. Nothing injects it today; the first thing that does would have got
    // nulls and no clue why.
    [Fact]
    public void TheSettings_AreBoundForInjection()
    {
        var provider = Build(ConfigWith(), requireRealClient: true);

        var settings = provider.GetRequiredService<IOptions<ZendeskSettings>>().Value;

        Assert.Equal("dfe", settings.Subdomain);
        Assert.Equal("zendesk", settings.Domain);
        Assert.Equal("cypmd@education.gov.uk", settings.Email);
        Assert.Equal("token", settings.ApiToken);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get { lock (_warnings) { return _warnings.ToArray(); } }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel < LogLevel.Warning) return;
                lock (warnings) { warnings.Add(formatter(state, exception)); }
            }
        }
    }
}
