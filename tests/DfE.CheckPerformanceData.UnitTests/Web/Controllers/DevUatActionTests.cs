using DfE.CheckPerformanceData.Application.Observability;
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
    private readonly IMetricsSink _metricsSink = Substitute.For<IMetricsSink>();
    private readonly IDemoTrafficPurger _demoPurger = Substitute.For<IDemoTrafficPurger>();

    private DevUatController CreateSut(bool ajax = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = "true" })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = "Development";
        var runner = new DevPipelineRunner(_dbContext, _queueService);
        var httpContext = new DefaultHttpContext();
        if (ajax)
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var sut = new DevUatController(config, _queueService, runner, env, _metricsSink, _demoPurger)
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

    [Fact]
    public async Task SeedMessages_Enabled_BulkWritesSyntheticHistoryAndRedirects()
    {
        var sut = CreateSut();

        // Default invocation (no range/from/to/perDay) seeds the default "last couple months" window.
        var result = await sut.SeedMessages(cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/observability", redirect.Url);
        // A single bulk write of a non-empty batch of synthetic events.
        await _metricsSink.Received(1).RecordManyAsync(
            Arg.Is<IEnumerable<QueueMetricEvent>>(e => e.Any()), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedMessages_CustomWindowAndPerDay_SeedsThatDensityAcrossTheWindow()
    {
        var sut = CreateSut();

        IEnumerable<QueueMetricEvent>? captured = null;
        await _metricsSink.RecordManyAsync(
            Arg.Do<IEnumerable<QueueMetricEvent>>(e => captured = e.ToList()), Arg.Any<CancellationToken>());

        // A 5-day custom window at 4/day → 20 distinct submissions, all inside the window.
        var from = "2026-04-01T00:00";
        var to = "2026-04-06T00:00";
        await sut.SeedMessages(range: "custom", from: from, to: to, perDay: 4,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        var references = captured!.Select(e => e.ReferenceNumber).Distinct().Count();
        Assert.Equal(20, references);
        Assert.All(captured!, e => Assert.InRange(
            e.RecordedAtUtc,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 6, 0, 1, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task SeedMessages_AjaxRequest_ReturnsOkJsonWithACount()
    {
        var sut = CreateSut(ajax: true);

        var result = await sut.SeedMessages(cancellationToken: CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var ok = json.Value!.GetType().GetProperty("ok")!.GetValue(json.Value);
        var count = json.Value!.GetType().GetProperty("count")!.GetValue(json.Value);
        Assert.Equal(true, ok);
        Assert.True((int)count! > 0);
        await _metricsSink.Received(1).RecordManyAsync(
            Arg.Any<IEnumerable<QueueMetricEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PurgeDemo_Enabled_PurgesAndRedirects()
    {
        _demoPurger.PurgeAsync(Arg.Any<CancellationToken>())
            .Returns(new DemoPurgeResult(10, 2, 1));

        var sut = CreateSut();

        var result = await sut.PurgeDemo(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/observability", redirect.Url);
        await _demoPurger.Received(1).PurgeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PurgeDemo_AjaxRequest_ReturnsOkJsonWithRemovedCount()
    {
        _demoPurger.PurgeAsync(Arg.Any<CancellationToken>())
            .Returns(new DemoPurgeResult(10, 2, 1));

        var sut = CreateSut(ajax: true);

        var result = await sut.PurgeDemo(CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var ok = json.Value!.GetType().GetProperty("ok")!.GetValue(json.Value);
        var removed = json.Value!.GetType().GetProperty("removed")!.GetValue(json.Value);
        Assert.Equal(true, ok);
        Assert.Equal(13, removed); // 10 + 2 + 1
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
