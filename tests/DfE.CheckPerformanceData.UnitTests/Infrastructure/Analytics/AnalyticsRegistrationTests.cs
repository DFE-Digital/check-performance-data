using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.Analytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure.Analytics;

public sealed class AnalyticsRegistrationTests
{
    private static IAnalyticsService Resolve(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddInfrastructureDependencies(config);
        return services.BuildServiceProvider().GetRequiredService<IAnalyticsService>();
    }

    [Fact]
    public void Registers_NoOp_When_DfeAnalytics_Not_Configured()
    {
        Assert.IsType<NullAnalyticsService>(Resolve([]));
    }

    [Fact]
    public void Registers_RealAdapter_When_DfeAnalytics_DatasetConfigured()
    {
        var sut = Resolve(new Dictionary<string, string?>
        {
            ["DfeAnalytics:DatasetId"] = "events_dataset",
            ["DfeAnalytics:ProjectId"] = "check-performance-data",
        });

        Assert.IsType<DfeAnalyticsService>(sut);
    }
}
