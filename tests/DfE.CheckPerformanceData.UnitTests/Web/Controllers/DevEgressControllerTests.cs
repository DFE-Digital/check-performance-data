using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The seeder writes rows that look like real requests, so it must be unreachable wherever the
// dev tools are off and in Production regardless of the flag — the DevPipelineController rule.
public sealed class DevEgressControllerTests
{
    private static DevEgressController Build(bool toolsEnabled, string environment)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = toolsEnabled ? "true" : "false" }).Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        return new DevEgressController(config, Substitute.For<IPortalDbContext>(), Substitute.For<IRequestStateBlobClient>(), Substitute.For<IEgressBlobClient>(), env);
    }

    [Theory]
    [InlineData(false, "Development")]
    [InlineData(true, "Production")]
    public async Task Seed_and_cleanup_are_404_when_not_allowed(bool toolsEnabled, string environment)
    {
        var sut = Build(toolsEnabled, environment);
        Assert.IsType<NotFoundResult>(await sut.Seed(Guid.NewGuid(), "RemoveLearners", "approved", 1, "860/4070", 142313, "pupil-died", CancellationToken.None));
        Assert.IsType<NotFoundResult>(await sut.Cleanup(Guid.NewGuid(), CancellationToken.None));
    }
}
