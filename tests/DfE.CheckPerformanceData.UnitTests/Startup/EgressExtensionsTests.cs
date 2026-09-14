using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// The web host never registered a Zendesk client before this feature. Review finding B1: the safe
// default is the real Zendesk client (matching the worker's configured default), never the dev
// outbox fake — the fake is opt-in only, and never available in Production regardless of config.
public sealed class EgressExtensionsTests
{
    private static IHostEnvironment Env(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return env;
    }

    private static ServiceProvider Build(Dictionary<string, string?> values, string environmentName = "Development")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton(Substitute.For<IPortalDbContext>());
        services.AddSingleton<IReadOnlyDictionary<string, BlobServiceClient>>(new Dictionary<string, BlobServiceClient>());
        services.AddSingleton(TimeProvider.System);
        services.AddCpdEgress(config, Env(environmentName));
        return services.BuildServiceProvider();
    }

    // B1(a): with no Zendesk:UseFake configured at all, the code takes the real-client branch —
    // proven here because the real branch's own settings validation is what throws. Before the
    // fix this scenario silently resolved the dev outbox fake instead.
    [Fact]
    public void Default_is_the_real_zendesk_source_so_missing_settings_fail_fast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Build(new Dictionary<string, string?>()));
        Assert.Contains("section is missing", ex.Message);
    }

    [Fact]
    public void UseFake_true_in_a_non_production_environment_selects_the_dev_outbox_source()
    {
        using var sp = Build(
            new Dictionary<string, string?> { ["Zendesk:UseFake"] = "true" },
            environmentName: "Development");
        using var scope = sp.CreateScope();
        Assert.IsType<DevOutboxEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEgressBlobClient>());
    }

    [Fact]
    public void UseFake_true_in_production_refuses_at_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(
            new Dictionary<string, string?> { ["Zendesk:UseFake"] = "true" },
            environmentName: "Production"));
        Assert.Contains("Production", ex.Message);
    }

    [Fact]
    public void Real_zendesk_is_selected_when_the_fake_is_switched_off()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Zendesk:UseFake"] = "false",
            ["ZendeskSettings:Subdomain"] = "esfa-preprod",
            ["ZendeskSettings:Domain"] = "zendesk",
            ["ZendeskSettings:ClientId"] = "id",
            ["ZendeskSettings:ClientSecret"] = "secret",
            ["SchoolCheckingExercise:TargetViewTitle"] = "View"
        });
        using var scope = sp.CreateScope();
        Assert.IsType<ZendeskEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
    }

    // B1(b): Default_is_the_real_zendesk_source_so_missing_settings_fail_fast above only proves
    // the default indirectly (missing settings on the real branch throw). This proves it directly:
    // with no Zendesk:UseFake key present at all — not "false", simply absent — and full,
    // complete ZendeskSettings configured, DI resolves the real client type outright.
    [Fact]
    public void Default_resolves_the_real_zendesk_client_when_UseFake_is_not_configured_at_all()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["ZendeskSettings:Subdomain"] = "esfa-preprod",
            ["ZendeskSettings:Domain"] = "zendesk",
            ["ZendeskSettings:ClientId"] = "id",
            ["ZendeskSettings:ClientSecret"] = "secret",
            ["SchoolCheckingExercise:TargetViewTitle"] = "View"
        });
        using var scope = sp.CreateScope();
        Assert.IsType<ZendeskEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
    }
}
