using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The UAT console drive/failure actions add no new machinery: each one reuses an existing dev
// path (the pipeline runner for drive, the seed/dead-letter path for failure) and redirects back
// to /dev/uat so the watcher stays on the console. These tests pin that reuse-and-redirect
// contract and the batch behaviour of "drive xN".
public sealed class DevUatActionTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();

    private DevUatController CreateSut()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = "true" })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = "Development";
        var runner = new DevPipelineRunner(_dbContext, _queueService);
        var sut = new DevUatController(config, _queueService, runner, env)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>()),
        };
        return sut;
    }

    [Fact]
    public async Task Drive_Enabled_EnqueuesOnceAndRedirectsToConsole()
    {
        var sut = CreateSut();

        var result = await sut.Drive("approved", count: 1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/dev/uat", redirect.Url);
        await _queueService.Received(1).EnqueueAsync(
            QueueOptions.RulesEngineQueue, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drive_BatchOfThree_EnqueuesThreeTimes()
    {
        var sut = CreateSut();

        await sut.Drive("scrutiny", count: 3, CancellationToken.None);

        await _queueService.Received(3).EnqueueAsync(
            QueueOptions.RulesEngineQueue, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drive_CountBelowOne_DefaultsToASingleMessage()
    {
        var sut = CreateSut();

        await sut.Drive("approved", count: 0, CancellationToken.None);

        await _queueService.Received(1).EnqueueAsync(
            QueueOptions.RulesEngineQueue, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drive_RemembersLastReference_ForTheJourneyShortcut()
    {
        var sut = CreateSut();

        await sut.Drive("approved", count: 1, CancellationToken.None);

        var journey = sut.LastJourney();
        var redirect = Assert.IsType<RedirectResult>(journey);
        Assert.Contains("/admin/observability/journey/", redirect.Url);
    }

    [Fact]
    public async Task InjectFailure_Enabled_DeadLettersAndRedirectsToConsole()
    {
        var sut = CreateSut();

        var result = await sut.InjectFailure(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/dev/uat", redirect.Url);
        await _queueService.Received(1).DeadLetterAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedDlq_Enabled_DeadLettersAndRedirectsToConsole()
    {
        var sut = CreateSut();

        var result = await sut.SeedDlq(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/dev/uat", redirect.Url);
        await _queueService.Received(1).DeadLetterAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
