using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The dev pipeline trigger and outbox viewer are development-only: in Production they must
// 404 and never touch the database or the queue.
public sealed class DevPipelineControllerTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();

    private DevPipelineController CreateSut(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return new DevPipelineController(env, _dbContext, _queueService);
    }

    [Fact]
    public async Task SubmitRequest_InProduction_ReturnsNotFound()
    {
        var sut = CreateSut("Production");

        var result = await sut.SubmitRequest(outcome: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _queueService.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outbox_InProduction_ReturnsNotFound()
    {
        var sut = CreateSut("Production");

        var result = await sut.Outbox(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
