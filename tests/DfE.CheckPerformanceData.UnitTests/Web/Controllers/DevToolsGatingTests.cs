using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The /dev/* tooling endpoints are gated on the Dev:ToolsEnabled config flag, not on the
// environment name. The DfE test site runs ASPNETCORE_ENVIRONMENT=Development, so an
// environment-name gate cannot distinguish localhost and test from production-with-the-same-name
// and conflates "show dev tools" with other concerns. The flag defaults to off so the endpoints
// 404 unless an appsettings layer turns them on (dev + test only).
public sealed class DevToolsGatingTests
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

    private DevPipelineController CreatePipeline(IConfiguration config) =>
        new(config, _dbContext, _queueService);

    private DevQueueSeedController CreateSeed(IConfiguration config) =>
        new(config, _queueService);

    [Fact]
    public async Task DevPipeline_SubmitRequest_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreatePipeline(Config(toolsEnabled: false));

        var result = await sut.SubmitRequest(outcome: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _queueService.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevPipeline_SubmitRequest_FlagAbsent_DefaultsToNotFound()
    {
        var sut = CreatePipeline(Config(toolsEnabled: null));

        var result = await sut.SubmitRequest(outcome: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DevPipeline_Outbox_ToolsEnabled_DoesNotReturnNotFoundOnGate()
    {
        var sut = CreatePipeline(Config(toolsEnabled: true));

        var result = await sut.Outbox(CancellationToken.None);

        // With the flag on, the gate is passed: the action does not 404 on the gate.
        // (It returns the Outbox view rather than NotFound.)
        Assert.IsNotType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DevQueueSeed_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreateSeed(Config(toolsEnabled: false));

        var result = await sut.SeedDeadLetter(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _queueService.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevQueueSeed_FlagAbsent_DefaultsToNotFound()
    {
        var sut = CreateSeed(Config(toolsEnabled: null));

        var result = await sut.SeedDeadLetter(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
