using System.Reflection;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Observability;
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

    private static IMetricsQueryService BuildQuery()
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
        query.GetDecisionMixOverTimeAsync(Arg.Any<ThroughputGranularity>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DecisionMixBucket>());
        return query;
    }

    // --- Every action carries [Authorize(Roles = cypmd_admin)] ---

    [Theory]
    [InlineData(nameof(ObservabilityController.Index))]
    [InlineData(nameof(ObservabilityController.Journey))]
    [InlineData(nameof(ObservabilityController.Stream))]
    [InlineData(nameof(ObservabilityController.Inspect))]
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
        var query = BuildQuery();

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

    // --- With no selection the dashboard renders the 24-hour window in hourly buckets ---

    [Fact]
    public async Task Index_Defaults_ToHourlyBucketsOverTheLast24Hours()
    {
        var query = BuildQuery();
        var controller = BuildController(query);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal("24h", model.SelectedRange);
        Assert.Equal(ThroughputGranularity.Hour, model.SelectedGranularity);
        await query.Received(1).GetThroughputAsync(
            QueueOptions.ZendeskQueue,
            ThroughputGranularity.Hour,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    // --- A recognised range with a paired granularity drives the chart queries ---

    [Fact]
    public async Task Index_HonoursARecognisedRangeAndPairedGranularity()
    {
        var fromTimes = new List<DateTime>();
        var toTimes = new List<DateTime>();
        var query = BuildQuery();
        query.GetThroughputAsync(
                Arg.Any<string>(),
                ThroughputGranularity.FiveMinute,
                Arg.Do<DateTime>(fromTimes.Add),
                Arg.Do<DateTime>(toTimes.Add),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ThroughputBucket>());

        var controller = BuildController(query);

        var result = await controller.Index(range: "1h", granularity: "FiveMinute");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal("1h", model.SelectedRange);
        Assert.Equal(ThroughputGranularity.FiveMinute, model.SelectedGranularity);
        var window = Assert.Single(toTimes) - Assert.Single(fromTimes);
        Assert.Equal(TimeSpan.FromHours(1), window);
    }

    // --- An unknown range falls back to the default window rather than erroring ---

    [Fact]
    public async Task Index_UnknownRange_FallsBackToTheDefault()
    {
        var controller = BuildController(BuildQuery());

        var result = await controller.Index(range: "fortnight");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal("24h", model.SelectedRange);
        Assert.Equal(ThroughputGranularity.Hour, model.SelectedGranularity);
    }

    // --- A granularity that makes no sense for the range snaps to the range's default ---

    [Fact]
    public async Task Index_UnpairedGranularity_SnapsToTheRangeDefault()
    {
        var query = BuildQuery();
        var controller = BuildController(query);

        var result = await controller.Index(range: "1h", granularity: "Day");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal("1h", model.SelectedRange);
        Assert.Equal(ThroughputGranularity.Minute, model.SelectedGranularity);
        await query.DidNotReceive().GetThroughputAsync(
            Arg.Any<string>(),
            ThroughputGranularity.Day,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    // --- The headline tiles and sentence stay on the last 24 hours whatever the charts show ---

    [Fact]
    public async Task Index_NonDefaultWindow_KeepsHeadlineFiguresOnTheLast24Hours()
    {
        var query = BuildQuery();
        var controller = BuildController(query);

        await controller.Index(range: "1h");

        // The charts use the selected window; the "processed today" headline still needs the
        // 24-hour figures, so a second hourly 24-hour read happens alongside.
        await query.Received(1).GetThroughputAsync(
            QueueOptions.ZendeskQueue,
            ThroughputGranularity.Minute,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await query.Received(1).GetThroughputAsync(
            QueueOptions.ZendeskQueue,
            ThroughputGranularity.Hour,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    // --- The decision-mix-over-time series is queried with the selected bucket size ---

    [Fact]
    public async Task Index_PopulatesDecisionMixOverTime_WithTheSelectedGranularity()
    {
        var series = new[]
        {
            new DecisionMixBucket(DateTime.UtcNow, "AutoApproved", 3),
            new DecisionMixBucket(DateTime.UtcNow, "AutoRejected", 1),
        };
        var query = BuildQuery();
        query.GetDecisionMixOverTimeAsync(ThroughputGranularity.FiveMinute,
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(series);

        var controller = BuildController(query);

        var result = await controller.Index(range: "6h", granularity: "FiveMinute");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal(series, model.DecisionMixOverTime);
    }

    // --- The orphaned throughput JSON endpoint is gone: the dashboard form replaced it ---

    [Fact]
    public void ThroughputJsonEndpoint_IsRemoved()
    {
        Assert.Null(typeof(ObservabilityController).GetMethod("Throughput"));
    }

    // --- Export returns the underlying data as CSV, not a chart image ---

    [Fact]
    public void Export_HasAuthorizeAttribute_WithAdminRole()
    {
        var method = typeof(ObservabilityController).GetMethod(nameof(ObservabilityController.Export));
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public async Task Export_ReturnsCsvFileWithAFilename()
    {
        var query = BuildQuery();
        query.GetThroughputAsync(Arg.Any<string>(), Arg.Any<ThroughputGranularity>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new ThroughputBucket(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), 7) });
        query.GetDecisionMixAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new DecisionMixEntry("AutoApproved", 7) });

        var controller = BuildController(query);

        var result = await controller.Export();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.EndsWith(".csv", file.FileDownloadName);

        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Throughput", csv);
        Assert.Contains("AutoApproved", csv);
    }

    [Fact]
    public async Task Export_HonoursTheSelectedRangeAndGranularity()
    {
        var query = BuildQuery();
        var controller = BuildController(query);

        await controller.Export(range: "1h", granularity: "FiveMinute");

        // The export reads the SAME series the charts use, at the selected granularity, so the
        // numbers in the file match the numbers on screen.
        await query.Received(1).GetThroughputAsync(
            QueueOptions.ZendeskQueue,
            ThroughputGranularity.FiveMinute,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    // --- The full transactions page is paged by the Wiki:PageLength setting ---

    [Fact]
    public void Transactions_HasAuthorizeAttribute_WithAdminRole()
    {
        var method = typeof(ObservabilityController).GetMethod(nameof(ObservabilityController.Transactions));
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public async Task Transactions_PagesByTheConfiguredPageLength()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionsPage(Array.Empty<TransactionRow>(), 0, 1, 15));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(15);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions(page: 2);

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<TransactionsViewModel>(view.Model);

        // The page size comes from Wiki:PageLength, not a hard-coded constant.
        await query.Received(1).GetTransactionsAsync(
            2, 15, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transactions_ComputesTotalPagesFromTheCount()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionsPage(Array.Empty<TransactionRow>(), 41, 1, 20));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions();

        var model = Assert.IsType<TransactionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(41, model.TotalCount);
        Assert.Equal(20, model.PageSize);
        Assert.Equal(3, model.TotalPages); // ceil(41 / 20)
    }

    // --- The submissions picker is admin-gated and paged by Wiki:PageLength ---

    [Theory]
    [InlineData(nameof(ObservabilityController.Submissions))]
    [InlineData(nameof(ObservabilityController.Walkthrough))]
    public void ReplaySurfaces_HaveAuthorizeAttribute_WithAdminRole(string actionName)
    {
        var method = typeof(ObservabilityController).GetMethod(actionName);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public async Task Submissions_PagesByTheConfiguredPageLength_AndDefaultsToARecentWindow()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetSubmissionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SubmissionsPage(Array.Empty<SubmissionRow>(), 0, 1, 20));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Submissions();

        var model = Assert.IsType<SubmissionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(20, model.PageSize);

        // Default window: a recent window is applied (a non-null from), so the picker opens on the
        // last 20 by recency rather than every reference ever seen.
        await query.Received(1).GetSubmissionsAsync(
            1, 20, Arg.Is<DateTime?>(d => d != null), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submissions_DateFilterNarrowsTheWindow()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetSubmissionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SubmissionsPage(Array.Empty<SubmissionRow>(), 0, 1, 20));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await controller.Submissions(from: from, to: to);

        await query.Received(1).GetSubmissionsAsync(
            1, 20,
            Arg.Is<DateTime?>(d => d == from),
            Arg.Is<DateTime?>(d => d == to),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Walkthrough_BuildsAStageProgressionForEachSelectedReference()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetJourneyAsync("REF-1", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new JourneyEvent("Submitted", "REF-1", "rules-engine", null, 0, DateTime.UtcNow),
                new JourneyEvent("RulesEvaluated", "REF-1", "rules-engine", "AutoApproved", 10, DateTime.UtcNow.AddSeconds(1)),
                new JourneyEvent("TicketCreated", "REF-1", "zendesk", "AutoApproved", 20, DateTime.UtcNow.AddSeconds(2)),
            });

        var controller = BuildController(query);

        var result = await controller.Walkthrough(new[] { "REF-1" });

        var model = Assert.IsType<WalkthroughViewModel>(Assert.IsType<ViewResult>(result).Model);
        var item = Assert.Single(model.Items);
        Assert.Equal("REF-1", item.ReferenceNumber);

        // The stage progression is the ordered stage keys the reference actually visited, mapped to
        // the five board stages, so the cohort can be single-stepped across them.
        Assert.Equal(new[] { "submit", "rules-engine", "ticket" }, item.StageKeys);
    }

    // --- Inspect is a journey-only panel: decision + stages, never a queue-row payload ---

    [Fact]
    public async Task Inspect_ReturnsJourneyOnlyPanel_NeverReadsAQueueRowPayload()
    {
        // Even a Guid-shaped reference must not trigger a queue-row payload lookup: the board
        // keys tokens by reference number, and metrics are only recorded after ack/dead-letter,
        // when the row is already gone. The panel is the journey only.
        var reference = Guid.NewGuid().ToString();

        var query = Substitute.For<IMetricsQueryService>();
        query.GetJourneyAsync(reference, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new JourneyEvent("RulesEvaluated", reference, "rules-engine", "AutoApproved", 1200, DateTime.UtcNow),
            });

        var queueAdmin = Substitute.For<IQueueAdminService>();

        var controller = BuildController(query, queueAdmin, settings: null);

        var result = await controller.Inspect(reference);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<InspectViewModel>(view.Model);
        Assert.Equal("AutoApproved", model.Decision);
        Assert.NotEmpty(model.Stages);
        await queueAdmin.DidNotReceive().GetMessageDetailAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // --- Inspect for an unknown reference returns the panel with no decision and no payload ---

    [Fact]
    public async Task Inspect_UnknownReference_ReturnsEmptyPanel()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetJourneyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<JourneyEvent>());

        var controller = BuildController(query);

        var result = await controller.Inspect("REF-does-not-exist");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<InspectViewModel>(view.Model);
        Assert.Empty(model.Stages);
    }
}
