using System.Reflection;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The observability dashboard is always-on but role-gated cypmd_admin on every action,
// including the SSE stream (no unauthenticated firehose). The throughput action validates
// its granularity against the server-side allow-list before any aggregation runs.
public sealed class ObservabilityControllerTests
{
    private static ObservabilityController BuildController(
        IMetricsQueryService? query = null,
        IQueueAdminService? queueAdmin = null,
        ISettingService? settings = null)
    {
        query ??= Substitute.For<IMetricsQueryService>();
        queueAdmin ??= Substitute.For<IQueueAdminService>();
        settings ??= Substitute.For<ISettingService>();
        return new ObservabilityController(query, queueAdmin, new HealthEvaluator(), new StatusSentenceBuilder(), settings);
    }

    // --- Every action carries [Authorize(Roles = cypmd_admin)] ---

    [Theory]
    [InlineData(nameof(ObservabilityController.Index))]
    [InlineData(nameof(ObservabilityController.Throughput))]
    [InlineData(nameof(ObservabilityController.Journey))]
    [InlineData(nameof(ObservabilityController.Stream))]
    public void Action_HasAuthorizeAttribute_WithAdminRole(string actionName)
    {
        var method = typeof(ObservabilityController).GetMethod(actionName);
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    // --- Index returns the dashboard view model ---

    [Fact]
    public async Task Index_ReturnsDashboardViewModel()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetCurrentSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new ObservabilitySnapshot(
                Array.Empty<QueueDepthSnapshot>(),
                Array.Empty<JourneyEvent>(),
                Array.Empty<DecisionMixEntry>(),
                DateTime.UtcNow));
        query.GetThroughputAsync(Arg.Any<string>(), Arg.Any<ThroughputGranularity>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ThroughputBucket>());
        query.GetDecisionMixAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DecisionMixEntry>());
        query.GetDwellByStageAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<StageDwell>());
        query.GetDeployMarkersAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DeployMarker>());

        var queueAdmin = Substitute.For<IQueueAdminService>();
        queueAdmin.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<QueueDepth>());
        queueAdmin.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(Arg.Any<string>()).Returns(10);

        var controller = BuildController(query, queueAdmin, settings);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.NotNull(view.Model);
    }

    // --- The throughput action rejects a granularity outside the allow-list ---

    [Fact]
    public async Task Throughput_RejectsUnknownGranularity()
    {
        var controller = BuildController();

        var result = await controller.Throughput("not-a-granularity", null, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- A valid granularity reaches the query service ---

    [Fact]
    public async Task Throughput_AcceptsKnownGranularity()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetThroughputAsync(Arg.Any<string>(), Arg.Any<ThroughputGranularity>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ThroughputBucket>());

        var controller = BuildController(query);

        var result = await controller.Throughput("Minute", null, null);

        Assert.IsNotType<BadRequestObjectResult>(result);
    }
}
