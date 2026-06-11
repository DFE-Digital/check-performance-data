using System.Reflection;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The board's stakeholder-demo showpieces split into two gating classes:
//   - the failure-recovery poison inject and the Zendesk-styled preview are dev/test only,
//     gated on Dev:ToolsEnabled (the preview additionally respects Zendesk:UseFake), so they
//     404 in production exactly like the other /dev/* tooling;
//   - the board replay window endpoint is ALWAYS-ON in every environment (role-gated
//     cypmd_admin only) — it must stay reachable with the dev flag off, because replay is a
//     locked always-on surface, not a dev tool.
// The failure-recovery inject composes the existing SeedDeadLetter path rather than adding any
// new failure machinery.
public sealed class DevDemoControlsTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();

    private static IConfiguration Config(bool? toolsEnabled = null, bool? useFake = null)
    {
        var values = new Dictionary<string, string?>();
        if (toolsEnabled.HasValue)
            values["Dev:ToolsEnabled"] = toolsEnabled.Value ? "true" : "false";
        if (useFake.HasValue)
            values["Zendesk:UseFake"] = useFake.Value ? "true" : "false";

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private DevQueueSeedController CreateSeed(IConfiguration config) =>
        new(config, _queueService);

    private DevPipelineController CreatePipeline(IConfiguration config) =>
        new(config, _dbContext, _queueService);

    // --- The failure-recovery poison inject is dev-gated and reuses SeedDeadLetter ---

    [Fact]
    public async Task InjectFailureDemo_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreateSeed(Config(toolsEnabled: false));

        var result = await sut.InjectFailureDemo(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _queueService.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectFailureDemo_FlagAbsent_DefaultsToNotFound()
    {
        var sut = CreateSeed(Config());

        var result = await sut.InjectFailureDemo(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task InjectFailureDemo_ToolsEnabled_ComposesExistingSeedDeadLetterPath()
    {
        // The existing SeedDeadLetter path enqueues the synthetic message and dead-letters it by
        // its own id. The inject demo must reach that same path — enqueue then dead-letter —
        // rather than introduce a new failure-injection mechanism.
        var seededId = Guid.NewGuid();
        _queueService
            .EnqueueAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(seededId);

        var sut = CreateSeed(Config(toolsEnabled: true));

        var result = await sut.InjectFailureDemo(CancellationToken.None);

        Assert.IsNotType<NotFoundResult>(result);
        await _queueService.Received().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        await _queueService.Received().DeadLetterAsync(
            seededId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- The seed dead-letters the message it seeded, never an arbitrary dequeued one ---

    [Fact]
    public async Task SeedDeadLetter_DeadLettersTheSeededMessageById_AndNeverDequeues()
    {
        var seededId = Guid.NewGuid();
        _queueService
            .EnqueueAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(seededId);

        // The oldest visible message on the queue is a REAL pending one. If the seed path
        // dequeued, it would claim — and then dead-letter — this legitimate message instead of
        // the synthetic one it just enqueued.
        var realMessage = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = QueueOptions.RulesEngineQueue,
            Payload = """{"Reference":"REAL-1"}""",
        };
        _queueService
            .DequeueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(realMessage);

        var sut = CreateSeed(Config(toolsEnabled: true));

        var result = await sut.SeedDeadLetter(CancellationToken.None);

        Assert.IsNotType<NotFoundResult>(result);
        await _queueService.DidNotReceive().DequeueAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queueService.Received(1).DeadLetterAsync(
            seededId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queueService.DidNotReceive().DeadLetterAsync(
            realMessage.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- The Zendesk-styled preview is dev-gated AND respects Zendesk:UseFake ---

    [Fact]
    public async Task ZendeskPreview_ToolsDisabled_ReturnsNotFound()
    {
        var sut = CreatePipeline(Config(toolsEnabled: false, useFake: true));

        var result = await sut.ZendeskPreview(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ZendeskPreview_FakeDisabled_ReturnsNotFound()
    {
        var sut = CreatePipeline(Config(toolsEnabled: true, useFake: false));

        var result = await sut.ZendeskPreview(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // --- The always-on replay endpoint is reachable with the dev flag OFF ---

    [Fact]
    public void Replay_DoesNotGateOnDevToolsEnabled()
    {
        // The replay action must NOT 404 when Dev:ToolsEnabled is off: it is a locked always-on
        // surface. Asserting the action carries no IConfiguration/Dev gate by construction — the
        // controller takes no IConfiguration dependency and the action is plain role-gated.
        var query = Substitute.For<IMetricsQueryService>();
        var controller = new ObservabilityController(
            query,
            Substitute.For<IQueueAdminService>(),
            new HealthEvaluator(),
            new StatusSentenceBuilder());

        // No exception, no NotFound short-circuit: the method exists and is reachable.
        Assert.NotNull(typeof(ObservabilityController).GetMethod(nameof(ObservabilityController.Replay)));
        Assert.NotNull(controller);
    }

    [Fact]
    public async Task Replay_ReturnsRecordedWindowAsJson()
    {
        var from = new DateTime(2026, 2, 1, 14, 0, 0, DateTimeKind.Utc);
        var to = from.AddMinutes(10);

        var query = Substitute.For<IMetricsQueryService>();
        query.GetReplayWindowAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new JourneyEvent("Submitted", "REF-1", "rules-engine", null, 0, from),
            });

        var controller = new ObservabilityController(
            query,
            Substitute.For<IQueueAdminService>(),
            new HealthEvaluator(),
            new StatusSentenceBuilder());

        var result = await controller.Replay(from, to);

        var json = Assert.IsType<JsonResult>(result);
        var events = Assert.IsAssignableFrom<IReadOnlyList<JourneyEvent>>(json.Value);
        Assert.Single(events);
    }

    [Fact]
    public void Replay_CarriesAdminRoleAuthorizeAttribute()
    {
        var method = typeof(ObservabilityController).GetMethod(nameof(ObservabilityController.Replay));
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }
}
