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

// The web host never registered a Zendesk client before this feature. The safe default is the
// real Zendesk client (matching the worker's configured default), never the dev outbox fake —
// the fake is opt-in only via Egress:UseDevOutbox, and never available in Production
// regardless of config. It is deliberately decoupled from Zendesk:UseFake (the worker's
// ticket-write choice), so a review app can write real tickets while still reading egress
// decisions from the outbox.
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

    // Decoupling: with no Egress:UseDevOutbox configured at all, the code takes the real-client
    // branch — proven here because the real branch's own settings validation is what throws.
    // Before the decoupling this scenario silently resolved when Zendesk:UseFake was absent.
    [Fact]
    public void Default_is_the_real_zendesk_source_so_missing_settings_fail_fast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Build(new Dictionary<string, string?>()));
        Assert.Contains("section is missing", ex.Message);
    }

    [Fact]
    public void UseDevOutbox_true_in_a_non_production_environment_selects_the_dev_outbox_source()
    {
        using var sp = Build(
            new Dictionary<string, string?> { ["Egress:UseDevOutbox"] = "true" },
            environmentName: "Development");
        using var scope = sp.CreateScope();
        Assert.IsType<DevOutboxEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEgressBlobClient>());
    }

    [Fact]
    public void UseDevOutbox_true_in_production_refuses_at_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(
            new Dictionary<string, string?> { ["Egress:UseDevOutbox"] = "true" },
            environmentName: "Production"));
        Assert.Contains("Production", ex.Message);
    }

    [Fact]
    public void Real_zendesk_is_selected_when_the_dev_outbox_is_switched_off()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Egress:UseDevOutbox"] = "false",
            ["ZendeskSettings:Subdomain"] = "esfa-preprod",
            ["ZendeskSettings:Domain"] = "zendesk",
            ["ZendeskSettings:ClientId"] = "id",
            ["ZendeskSettings:ClientSecret"] = "secret",
            ["SchoolCheckingExercise:TargetViewTitle"] = "View"
        });
        using var scope = sp.CreateScope();
        Assert.IsType<ZendeskEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
    }

    // Decoupling: Zendesk:UseFake alone no longer selects the outbox for egress reads. With the
    // worker's write flag set to "true" but Egress:UseDevOutbox unset, the real read source
    // (full settings supplied so the real branch resolves) is still what DI returns.
    [Fact]
    public void Zendesk_UseFake_true_alone_does_not_select_the_dev_outbox_for_egress_reads()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Zendesk:UseFake"] = "true",
            ["ZendeskSettings:Subdomain"] = "esfa-preprod",
            ["ZendeskSettings:Domain"] = "zendesk",
            ["ZendeskSettings:ClientId"] = "id",
            ["ZendeskSettings:ClientSecret"] = "secret",
            ["SchoolCheckingExercise:TargetViewTitle"] = "View"
        });
        using var scope = sp.CreateScope();
        Assert.IsType<ZendeskEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
    }

    // Default_is_the_real_zendesk_source_so_missing_settings_fail_fast above only proves the
    // default indirectly (missing settings on the real branch throw). This proves it directly:
    // with no Egress:UseDevOutbox key present at all — not "false", simply absent — and full,
    // complete ZendeskSettings configured, DI resolves the real client type outright.
    [Fact]
    public void Default_resolves_the_real_zendesk_client_when_UseDevOutbox_is_not_configured_at_all()
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
