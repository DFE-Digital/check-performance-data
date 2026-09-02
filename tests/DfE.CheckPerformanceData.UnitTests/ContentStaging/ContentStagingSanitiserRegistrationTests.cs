using DfE.CheckPerformanceData.Application.ContentStaging;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// ContentStagingService takes its sanitiser as an optional constructor parameter, so an
// unregistered sanitiser is not a startup failure — it is a silently disabled one. Import
// would keep working, keep reporting success, and stop stripping script tags, event handlers
// and javascript: URLs from everything it wrote into the page tree.
//
// Every other test in this area constructs the sanitiser explicitly, so all of them would
// still pass with the registration deleted. These pin the wiring itself.
public sealed class ContentStagingSanitiserRegistrationTests
{
    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();
        services.AddApplicationDependencies();
        return services;
    }

    [Fact]
    public void ApplicationDependencies_RegisterTheSanitiser()
    {
        Assert.Contains(Registered(), d => d.ServiceType == typeof(ContentBundleSanitiser));
    }

    [Fact]
    public void ApplicationDependencies_RegisterEverySanitiserDependency()
    {
        // Registering the sanitiser but not what it needs fails at resolve time rather than
        // at startup, which lands the same way: the import path loses its sanitiser.
        var services = Registered();
        var required = typeof(ContentBundleSanitiser)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        foreach (var dependency in required)
        {
            Assert.Contains(services, d => d.ServiceType == dependency);
        }
    }

    [Fact]
    public void ContentStagingService_TakesTheSanitiserFromTheContainer()
    {
        // The parameter is optional by design, for the unit tests that build the service by
        // hand. That is exactly why the registration needs its own guard: nothing else fails
        // when it disappears.
        var parameter = typeof(ContentStagingService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .SingleOrDefault(p => p.ParameterType == typeof(ContentBundleSanitiser));

        Assert.NotNull(parameter);
        Assert.Contains(Registered(), d => d.ServiceType == typeof(ContentBundleSanitiser));
    }
}
