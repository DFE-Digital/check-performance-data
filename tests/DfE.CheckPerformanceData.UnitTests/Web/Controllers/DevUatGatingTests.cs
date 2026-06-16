using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The debug drive/inject/seed endpoints are /dev/* surfaces: gated exactly like
// DevPipelineController and DevQueueSeedController on Dev:ToolsEnabled AND a hard production guard.
// With the flag off, or in production with the flag mistakenly on, every action 404s and never
// touches the queue. With the flag on outside production they reach their reused dev paths. (The
// standalone /dev/uat GET console was retired; only these POST endpoints remain, called by the
// dashboard's Demo panel.)
public sealed class DevUatGatingTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();

    private static IConfiguration Config(bool? toolsEnabled)
    {
        var values = new Dictionary<string, string?>();
        if (toolsEnabled.HasValue)
            values["Dev:ToolsEnabled"] = toolsEnabled.Value ? "true" : "false";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHostEnvironment Env(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return env;
    }

    private DevUatController CreateSut(bool? toolsEnabled, string environmentName = "Development")
    {
        var runner = new DevPipelineRunner(_dbContext, _queueService);
        var sut = new DevUatController(Config(toolsEnabled), _queueService, runner, Env(environmentName));
        // The Index view-render path is not exercised here; gating tests assert the result type
        // before any view executes, so a minimal TempData is enough for the redirect actions.
        sut.TempData = new TempDataDictionary(new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            Substitute.For<ITempDataProvider>());
        return sut;
    }

    [Fact]
    public async Task Drive_ToolsDisabled_ReturnsNotFoundAndDoesNotEnqueue()
    {
        var sut = CreateSut(toolsEnabled: false);

        var result = await sut.Drive("approved", count: 1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _queueService.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectFailure_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreateSut(toolsEnabled: false);
        Assert.IsType<NotFoundResult>(await sut.InjectFailure(CancellationToken.None));
    }

    [Fact]
    public async Task SeedDlq_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreateSut(toolsEnabled: false);
        Assert.IsType<NotFoundResult>(await sut.SeedDlq(CancellationToken.None));
    }
}
