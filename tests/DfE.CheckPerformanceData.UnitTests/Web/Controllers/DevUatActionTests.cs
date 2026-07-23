using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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

// The debug drive/failure actions add no new machinery: each one reuses an existing dev path (the
// pipeline runner for drive, the seed/dead-letter path for failure). The standalone /dev/uat GET
// console was retired and its controls folded into the dashboard's Demo panel, so the no-JS form
// post now redirects to the dashboard; the AJAX path (the panel's normal mode) returns JSON. These
// tests pin that reuse-and-redirect contract and the batch behaviour of "drive xN".
public sealed class DevUatActionTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly IPupilDataBlobClient _pupilBlob = Substitute.For<IPupilDataBlobClient>();

    private DevUatController CreateSut(bool ajax = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = "true" })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = "Development";
        var runner = new DevPipelineRunner(_dbContext, _queueService, _pupilBlob);
        var httpContext = new DefaultHttpContext();
        if (ajax)
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var sut = new DevUatController(config, _queueService, runner, env)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>()),
        };
        return sut;
    }

    [Fact]
    public async Task Drive_Enabled_EnqueuesOnceAndRedirectsToConsole()
    {
        var sut = CreateSut();

        var result = await sut.Drive("approved", count: 1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/observability", redirect.Url);
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
        Assert.Equal("/admin/observability", redirect.Url);
        await _queueService.Received(1).DeadLetterAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedDlq_Enabled_DeadLettersAndRedirectsToConsole()
    {
        var sut = CreateSut();

        var result = await sut.SeedDlq(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/observability", redirect.Url);
        await _queueService.Received(1).DeadLetterAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // The AJAX contract: an XMLHttpRequest drive returns JSON { ok, reference } so the page can
    // update the board in place without a full-page reload, while the plain form post still
    // redirects (the no-JS fallback covered above). The reference is the last driven reference.
    [Fact]
    public async Task Drive_AjaxRequest_ReturnsOkJsonWithReference()
    {
        var sut = CreateSut(ajax: true);

        var result = await sut.Drive("approved", count: 1, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var ok = json.Value!.GetType().GetProperty("ok")!.GetValue(json.Value);
        var reference = json.Value!.GetType().GetProperty("reference")!.GetValue(json.Value);
        Assert.Equal(true, ok);
        Assert.False(string.IsNullOrEmpty(reference as string));
        await _queueService.Received(1).EnqueueAsync(
            QueueOptions.RulesEngineQueue, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectFailure_AjaxRequest_ReturnsOkJson()
    {
        var sut = CreateSut(ajax: true);

        var result = await sut.InjectFailure(CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var ok = json.Value!.GetType().GetProperty("ok")!.GetValue(json.Value);
        Assert.Equal(true, ok);
        await _queueService.Received(1).DeadLetterAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedDlq_AjaxRequest_ReturnsOkJson()
    {
        var sut = CreateSut(ajax: true);

        var result = await sut.SeedDlq(CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var ok = json.Value!.GetType().GetProperty("ok")!.GetValue(json.Value);
        Assert.Equal(true, ok);
        await _queueService.Received(1).DeadLetterAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
