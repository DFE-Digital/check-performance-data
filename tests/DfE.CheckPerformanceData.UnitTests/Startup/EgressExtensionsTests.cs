using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Infrastructure.Egress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// The web host never registered a Zendesk client before this feature. With the fake selected (the
// default) the egress must resolve without any Zendesk configuration at all; with it disabled the
// real Refit client is registered and the real ticket source is used.
public sealed class EgressExtensionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton(Substitute.For<IPortalDbContext>());
        services.AddSingleton<IReadOnlyDictionary<string, BlobServiceClient>>(new Dictionary<string, BlobServiceClient>());
        services.AddSingleton(TimeProvider.System);
        services.AddCpdEgress(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_is_the_dev_outbox_source_with_no_zendesk_settings_needed()
    {
        using var sp = Build(new Dictionary<string, string?>());
        using var scope = sp.CreateScope();
        Assert.IsType<DevOutboxEgressTicketSource>(scope.ServiceProvider.GetRequiredService<IEgressTicketSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEgressBlobClient>());
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
}
