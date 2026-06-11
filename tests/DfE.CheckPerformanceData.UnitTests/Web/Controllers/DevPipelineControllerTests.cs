using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The dev pipeline trigger and outbox viewer are gated on the Dev:ToolsEnabled config flag:
// when it is off (the default) they must 404 and never touch the database or the queue.
public sealed class DevPipelineControllerTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();

    private DevPipelineController CreateSut(bool toolsEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dev:ToolsEnabled"] = toolsEnabled ? "true" : "false",
            })
            .Build();

        return new DevPipelineController(configuration, _dbContext, _queueService);
    }

    [Fact]
    public async Task SubmitRequest_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreateSut(toolsEnabled: false);

        var result = await sut.SubmitRequest(outcome: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _queueService.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outbox_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreateSut(toolsEnabled: false);

        var result = await sut.Outbox(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
