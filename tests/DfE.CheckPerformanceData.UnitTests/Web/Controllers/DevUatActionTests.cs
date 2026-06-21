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
    private readonly IMetricsQueryService _metricsQuery = Substitute.For<IMetricsQueryService>();

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
        var sut = new DevUatController(config, _queueService, runner, env, _metricsSink, _demoPurger, _metricsQuery)
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

    // --- The Random drive resolves to one of the four outcomes; a literal passes through ---

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("scrutiny")]
    [InlineData("failed")]
    [InlineData(null)]
    public void ResolveDriveOutcome_NonRandom_PassesThroughOrDefaults(string? outcome)
    {
        var resolved = DevUatController.ResolveDriveOutcome(outcome);
        Assert.Equal(outcome ?? "approved", resolved);
    }

    [Fact]
    public void ResolveDriveOutcome_Random_OnlyEverProducesTheFourOutcomes_AndAllAppear()
    {
        var rng = new Random(20260621);
        var seen = new HashSet<string>();
        for (var i = 0; i < 500; i++)
        {
            var o = DevUatController.ResolveDriveOutcome("random", rng);
            Assert.Contains(o, DevUatController.RandomOutcomes);
            seen.Add(o);
        }

        // Over 500 rolls every one of the four outcomes is produced.
        Assert.Equal(new[] { "approved", "failed", "rejected", "scrutiny" }, seen.OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task Drive_Random_ReturnsOkWithAResolvedOutcome()
    {
        var sut = CreateSut(ajax: true);

        var result = await sut.Drive("random", count: 1, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var outcome = json.Value!.GetType().GetProperty("outcome")!.GetValue(json.Value) as string;
        Assert.Contains(outcome, DevUatController.RandomOutcomes);
    }

    // --- The load-test level drives the batch and reports the measured throughput ---

    [Fact]
    public async Task LoadTest_DrivesTheBatch_AndReturnsThroughputWhenComplete()
    {
        var sut = CreateSut(ajax: true);

        // The metrics query reports the whole batch already terminal, so the poll returns at once.
        _metricsQuery.GetLoadSampleAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new LoadSample(4, new StageAverages(200, 400, 600, 150)));

        var result = await sut.LoadTest(rate: 4, timeoutMs: 5000, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        int Prop(string n) => Convert.ToInt32(json.Value!.GetType().GetProperty(n)!.GetValue(json.Value));
        Assert.Equal(4, Prop("rate"));
        Assert.Equal(4, Prop("completed"));
        var timedOut = (bool)json.Value!.GetType().GetProperty("timedOut")!.GetValue(json.Value)!;
        Assert.False(timedOut);

        // The batch was measured against the metrics query (the random outcome split decides which
        // queue each message lands on, so the exact rules-engine enqueue count is not asserted here).
        await _metricsQuery.Received().GetLoadSampleAsync(
            Arg.Is<IReadOnlyList<string>>(r => r.Count == 4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadTest_DrivesReferencesPrefixedWithLoad()
    {
        // Load-test traffic is tagged with the load- prefix (load-… drives, load-fail-… failures) so
        // it is distinct from the drive buttons' DEV- traffic and purgeable by its well-known prefix.
        var sut = CreateSut(ajax: true);
        IReadOnlyList<string>? captured = null;
        _metricsQuery.GetLoadSampleAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.ArgAt<IReadOnlyList<string>>(0);
                return new LoadSample(captured.Count, new StageAverages(1, 1, 1, 1));
            });

        await sut.LoadTest(rate: 3, timeoutMs: 2000, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Count);
        Assert.All(captured, r => Assert.StartsWith("load-", r));
    }

    [Fact]
    public async Task LoadTest_ToolsDisabled_ReturnsNotFound()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = "false" })
            .Build();
        var runner = new DevPipelineRunner(_dbContext, _queueService);
        var sut = new DevUatController(config, _queueService, runner, metricsQuery: _metricsQuery)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<NotFoundResult>(await sut.LoadTest(rate: 4, timeoutMs: 1000, CancellationToken.None));
    }

    [Fact]
    public void LoadTestExport_BuildsAnXlsxFromPostedRows()
    {
        var sut = CreateSut();

        var request = new DevUatController.LoadTestExportRequest(new[]
        {
            new DevUatController.LoadTestExportRow(1, 1, 1.0, 1000, 200, 400, 600, 150),
            new DevUatController.LoadTestExportRow(10, 9, 8.0, 1200, 300, 420, 800, 160),
        });

        var result = sut.LoadTestExport(request);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        Assert.NotEmpty(file.FileContents);
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
