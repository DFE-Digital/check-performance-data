using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Maintenance;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// AB#302158: the web host runs the automatic exercise hand-over, on the ticket's two hours unless
// an environment says otherwise. appsettings.json carries no entry on purpose: the defaults are
// the code's, and an environment overrides with ExerciseHandOver__* in its terraform config.
public sealed class ExerciseHandOverExtensionsTests
{
    private static ServiceCollection Services(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCpdExerciseHandOver(configuration);
        return services;
    }

    private static ExerciseHandOverSettings Settings(ServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IOptions<ExerciseHandOverSettings>>().Value;

    [Fact]
    public void The_job_is_a_hosted_service_and_runs_on_the_tickets_two_hours_by_default()
    {
        var services = Services([]);

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ExerciseHandOverJob));

        var settings = Settings(services);
        Assert.True(settings.Enabled);
        Assert.Equal(TimeSpan.FromHours(2), settings.DelayAfterEnd);
        Assert.Equal(TimeSpan.FromHours(24), settings.CatchUpWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.PollInterval);
    }

    [Fact]
    public void An_environment_can_switch_it_off_and_change_its_timings()
    {
        var settings = Settings(Services(new Dictionary<string, string?>
        {
            ["ExerciseHandOver:Enabled"] = "false",
            ["ExerciseHandOver:DelayAfterEnd"] = "00:01:00",
            ["ExerciseHandOver:CatchUpWindow"] = "00:30:00",
            ["ExerciseHandOver:PollInterval"] = "00:00:30"
        }));

        Assert.False(settings.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.DelayAfterEnd);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.CatchUpWindow);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.PollInterval);
    }
}
