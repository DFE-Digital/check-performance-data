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
        query.GetStageAveragesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new StageAverages(null, null, null, null));
        query.GetDeployMarkersAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DeployMarker>());
        query.GetDecisionMixOverTimeAsync(Arg.Any<ThroughputGranularity>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DecisionMixBucket>());
        query.GetGroupedTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new GroupedTransactionsPage(Array.Empty<GroupedTransactionRow>(), 0, 1, 25));
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

    // --- With no selection the dashboard renders Today (since midnight) in hourly buckets ---

    [Fact]
    public async Task Index_Defaults_ToTodayInHourlyBuckets()
    {
        var query = BuildQuery();
        var controller = BuildController(query);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Equal("today", model.SelectedRange);
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
        Assert.Equal("today", model.SelectedRange);
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

    // --- Duplicate per-queue health lights collapse to just the overall ---

    [Fact]
    public async Task Index_AllQueuesShareTheOverallReasons_SuppressesPerQueueLights()
    {
        // A dead-letter backlog trips every queue identically (depth/age fine, DLQ over its limit),
        // so the per-queue lights merely repeat the overall — only the overall should render.
        var query = BuildQuery();
        var queueAdmin = Substitute.For<IQueueAdminService>();
        queueAdmin.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new QueueDepth("rules-engine", 0, null),
            new QueueDepth("zendesk", 0, null),
        });
        queueAdmin.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(99); // over the red limit

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.HealthDepthAmber).Returns(25);
        settings.GetIntAsync(SettingKeys.HealthDepthRed).Returns(100);
        settings.GetIntAsync(SettingKeys.HealthOldestAgeAmberSeconds).Returns(120);
        settings.GetIntAsync(SettingKeys.HealthOldestAgeRedSeconds).Returns(600);
        settings.GetIntAsync(SettingKeys.HealthDlqRateRed).Returns(5);

        var controller = BuildController(query, queueAdmin, settings);

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.False(model.ShowPerQueueHealth);
    }

    [Fact]
    public async Task Index_QueuesDifferFromOverall_KeepsPerQueueLights()
    {
        // One queue backing up on depth while the other is healthy: the lights differ, so the
        // per-queue breakdown is kept.
        var query = BuildQuery();
        var queueAdmin = Substitute.For<IQueueAdminService>();
        queueAdmin.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new QueueDepth("rules-engine", 60, TimeSpan.FromSeconds(30)),
            new QueueDepth("zendesk", 0, null),
        });
        queueAdmin.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.HealthDepthAmber).Returns(25);
        settings.GetIntAsync(SettingKeys.HealthDepthRed).Returns(100);
        settings.GetIntAsync(SettingKeys.HealthOldestAgeAmberSeconds).Returns(120);
        settings.GetIntAsync(SettingKeys.HealthOldestAgeRedSeconds).Returns(600);
        settings.GetIntAsync(SettingKeys.HealthDlqRateRed).Returns(5);

        var controller = BuildController(query, queueAdmin, settings);

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.True(model.ShowPerQueueHealth);
    }

    // --- The headline decision counters describe the last 24 hours ---
    // Alongside "processed today", the dashboard shows ongoing 24-hour counts per decision —
    // auto-approved / auto-rejected / scrutiny — plus the current dead-letter count.

    [Fact]
    public async Task Index_PopulatesDecisionCounters_FromTheDecisionMixAndDlq()
    {
        var query = BuildQuery();
        query.GetDecisionMixAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new DecisionMixEntry("AutoApproved", 62),
                new DecisionMixEntry("AutoRejected", 3),
                new DecisionMixEntry("Scrutiny", 14),
            });

        var queueAdmin = Substitute.For<IQueueAdminService>();
        queueAdmin.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<QueueDepth>());
        queueAdmin.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(1);

        var controller = BuildController(query, queueAdmin);

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.Equal(62, model.AutoApprovedToday);
        Assert.Equal(3, model.AutoRejectedToday);
        Assert.Equal(14, model.ScrutinyToday);
        Assert.Equal(1, model.DeadLetterCount);
    }

    [Fact]
    public async Task Index_NonDefaultWindow_KeepsDecisionCountersOnTheLast24Hours()
    {
        var query = BuildQuery();
        var controller = BuildController(query);

        await controller.Index(range: "1h");

        // The chart decision-mix uses the selected (1h) window; the counters always describe the
        // last 24 hours, so a second 24-hour decision-mix read happens alongside.
        await query.Received(2).GetDecisionMixAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // --- The recent-submissions matrix is server-rendered from grouped transactions ---
    // So the table is populated on load (not empty/null until live traffic arrives); the board
    // engine seeds its live grid from these rows and folds in updates.

    [Fact]
    public async Task Index_PopulatesRecentSubmissions_FromGroupedTransactionsOverTheLast24Hours()
    {
        var query = BuildQuery();
        var now = DateTime.UtcNow;
        query.GetGroupedTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new GroupedTransactionsPage(
                new[]
                {
                    new GroupedTransactionRow("REF-1", now.AddSeconds(-30), now.AddSeconds(-20),
                        now.AddSeconds(-10), "AutoApproved", 120, false, now.AddSeconds(-10)),
                }, 1, 1, 25));

        var controller = BuildController(query);

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        var row = Assert.Single(model.RecentSubmissions);
        Assert.Equal("REF-1", row.ReferenceNumber);
        Assert.Equal("AutoApproved", row.Decision);
    }

    // --- The orphaned throughput JSON endpoint is gone: the dashboard form replaced it ---

    [Fact]
    public void ThroughputJsonEndpoint_IsRemoved()
    {
        Assert.Null(typeof(ObservabilityController).GetMethod("Throughput"));
    }

    // --- The dashboard's dev-only Demo panel is gated Dev:ToolsEnabled AND not production ---
    // The dashboard itself is always-on admin; only the demo controls (drive / inject / seed /
    // replay / demo trickle) are dev/test-gated, so the flag rides on the view model.

    private static ObservabilityController BuildGatedController(bool toolsEnabled, string environmentName)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dev:ToolsEnabled"] = toolsEnabled ? "true" : "false",
            })
            .Build();
        var env = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        env.EnvironmentName = environmentName;

        var queueAdmin = Substitute.For<IQueueAdminService>();
        queueAdmin.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<QueueDepth>());
        queueAdmin.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(Arg.Any<string>()).Returns(10);

        return new ObservabilityController(
            BuildQuery(), queueAdmin, new HealthEvaluator(), new StatusSentenceBuilder(), settings, config, env);
    }

    [Fact]
    public async Task Index_DevToolsEnabledNonProduction_SetsDemoToolsEnabled()
    {
        var controller = BuildGatedController(toolsEnabled: true, environmentName: "Development");

        var result = await controller.Index();

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.DemoToolsEnabled);
    }

    [Fact]
    public async Task Index_DevToolsEnabledInProduction_DoesNotSetDemoToolsEnabled()
    {
        var controller = BuildGatedController(toolsEnabled: true, environmentName: "Production");

        var result = await controller.Index();

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.DemoToolsEnabled);
    }

    [Fact]
    public async Task Index_DevToolsDisabled_DoesNotSetDemoToolsEnabled()
    {
        var controller = BuildGatedController(toolsEnabled: false, environmentName: "Development");

        var result = await controller.Index();

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.DemoToolsEnabled);
    }

    [Fact]
    public async Task Index_NoConfigOrEnvironment_DefaultsDemoToolsToOff()
    {
        // The bare unit construction (no config / env) must default the demo gate to off so the
        // panel never renders unless explicitly enabled outside production.
        var controller = BuildController(BuildQuery());

        var result = await controller.Index();

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.DemoToolsEnabled);
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

    // --- Export this view as an Excel workbook (four chart tabs) ---

    [Fact]
    public void ExportExcel_HasAuthorizeAttribute_WithAdminRole()
    {
        var method = typeof(ObservabilityController).GetMethod(nameof(ObservabilityController.ExportExcel));
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public async Task ExportExcel_ReturnsAnXlsxWorkbookFile()
    {
        var query = BuildQuery();
        query.GetThroughputAsync(Arg.Any<string>(), Arg.Any<ThroughputGranularity>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new ThroughputBucket(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), 7) });
        query.GetDecisionMixAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new DecisionMixEntry("AutoApproved", 7) });

        var controller = BuildController(query);

        var result = await controller.ExportExcel();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        Assert.NotEmpty(file.FileContents);
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
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionsPage(Array.Empty<TransactionRow>(), 0, 1, 15));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(15);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions(page: 2);

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<TransactionsViewModel>(view.Model);

        // The page size comes from Wiki:PageLength, not a hard-coded constant.
        await query.Received(1).GetTransactionsAsync(
            2, 15, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transactions_ComputesTotalPagesFromTheCount()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
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

    [Fact]
    public async Task Transactions_PassesTheReferenceFilterThrough_AndExposesItOnTheModel()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionsPage(Array.Empty<TransactionRow>(), 0, 1, 20));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions(page: 1, reference: "REF-123");

        var model = Assert.IsType<TransactionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("REF-123", model.Reference);

        await query.Received(1).GetTransactionsAsync(
            1, 20, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Is<string?>(r => r == "REF-123"),
            Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // --- Stage filters and sort flow through to the query and onto the model ---

    [Fact]
    public async Task Transactions_PassesStageFiltersAndSort_AndExposesThemOnTheModel()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionsPage(Array.Empty<TransactionRow>(), 0, 1, 20));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions(
            page: 1, stage: new[] { "Submitted", "bogus" }, sort: "reference", dir: "asc");

        var model = Assert.IsType<TransactionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        // The bogus stage is dropped (only known stages survive); the sort is exposed for the headers.
        Assert.Equal(new[] { "Submitted" }, model.Stages);
        Assert.Equal("reference", model.SortKey);
        Assert.False(model.SortDescending);

        await query.Received(1).GetTransactionsAsync(
            1, 20, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
            Arg.Is<IReadOnlyList<string>?>(s => s != null && s.Count == 1 && s[0] == "Submitted"),
            "reference", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transactions_UnknownSort_FallsBackToTheDefaultDescending()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetTransactionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionsPage(Array.Empty<TransactionRow>(), 0, 1, 20));

        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions(page: 1, sort: "injection; drop table");

        var model = Assert.IsType<TransactionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(TransactionSort.DefaultKey, model.SortKey);
        Assert.True(model.SortDescending);
    }

    // --- Grouped view: the grouped sort key flows through to the grouped query and onto the model ---

    [Fact]
    public async Task Transactions_Grouped_PassesGroupedSortThrough_AndExposesItOnTheModel()
    {
        var query = BuildQuery();
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        var result = await controller.Transactions(page: 1, group: true, sort: "submit", dir: "asc");

        var model = Assert.IsType<TransactionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Grouped);
        Assert.Equal("submit", model.SortKey);
        Assert.False(model.SortDescending);

        await query.Received(1).GetGroupedTransactionsAsync(
            1, 20, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<string?>(),
            "submit", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transactions_Grouped_UnknownSort_FallsBackToTheGroupedDefault()
    {
        var query = BuildQuery();
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.WikiPageLength).Returns(20);

        var controller = BuildController(query, settings: settings);

        // A flat-list key (or anything not in the grouped allow-list) snaps to the grouped default.
        var result = await controller.Transactions(page: 1, group: true, sort: "latency");

        var model = Assert.IsType<TransactionsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(GroupedTransactionSort.DefaultKey, model.SortKey);
        Assert.True(model.SortDescending);
    }

    // --- Range/granularity: custom from/to drives the chart window; named ranges resolve ---

    [Fact]
    public async Task Index_CustomRange_QueriesTheSuppliedWindow_AndExposesItOnTheModel()
    {
        var query = BuildQuery();
        var controller = BuildController(query, settings: Substitute.For<ISettingService>());

        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);

        // The from/to arrive as datetime-local strings (DateTime does not bind from these query values).
        var result = await controller.Index(
            range: "custom", from: "2026-06-01T00:00", to: "2026-06-08T00:00");
        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.True(model.IsCustomRange);
        Assert.Equal("custom", model.SelectedRange);
        Assert.Equal(from, model.SelectedFromUtc);
        Assert.Equal(to, model.SelectedToUtc);

        // The chart series are queried for the supplied window, not a fixed rolling one.
        await query.Received().GetThroughputAsync(
            Arg.Any<string>(), Arg.Any<ThroughputGranularity>(), from, to, Arg.Any<CancellationToken>());
        await query.Received().GetDecisionMixAsync(from, to, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_NamedCalendarRange_IsResolvedAndExposed_NotCustom()
    {
        var query = BuildQuery();
        var controller = BuildController(query, settings: Substitute.For<ISettingService>());

        var result = await controller.Index(range: "lastmonth");
        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.Equal("lastmonth", model.SelectedRange);
        Assert.False(model.IsCustomRange);
        Assert.Contains(model.SelectedGranularity, model.GranularityOptions);
    }

    [Fact]
    public async Task Index_UnknownRange_FallsBackToTodayDefault()
    {
        var query = BuildQuery();
        var controller = BuildController(query, settings: Substitute.For<ISettingService>());

        var result = await controller.Index(range: "fortnight");
        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.Equal(DashboardRanges.DefaultValue, model.SelectedRange);
    }

    [Fact]
    public async Task Replay_HonoursTheRequestedFromAndToWindow()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetReplayWindowAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<JourneyEvent>());

        var controller = BuildController(query);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);

        await controller.Replay(from: from, to: to);

        await query.Received(1).GetReplayWindowAsync(
            Arg.Is<DateTime>(d => d == from), Arg.Is<DateTime>(d => d == to), Arg.Any<CancellationToken>());
    }

    // --- The in-dashboard replay endpoints (picker list + selected events) are admin-gated ---

    [Theory]
    [InlineData(nameof(ObservabilityController.SubmissionsJson))]
    [InlineData(nameof(ObservabilityController.ReplaySelected))]
    public void ReplaySurfaces_HaveAuthorizeAttribute_WithAdminRole(string actionName)
    {
        var method = typeof(ObservabilityController).GetMethod(actionName);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public async Task SubmissionsJson_ReturnsRecentSubmissionsForThePicker()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetSubmissionsAsync(Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SubmissionsPage(
                new[] { new SubmissionRow("REF-1", DateTime.UtcNow, "RulesEvaluated", null, "AutoApproved") }, 1, 1, 100));

        var controller = BuildController(query);

        var result = await controller.SubmissionsJson();

        // A JSON list over a recent window (a non-null from) — the picker filters client-side.
        Assert.IsType<JsonResult>(result);
        await query.Received(1).GetSubmissionsAsync(
            1, 100, Arg.Is<DateTime?>(d => d != null), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaySelected_CombinesTheJourneysOfTheChosenReferences_TimeOrdered()
    {
        var query = Substitute.For<IMetricsQueryService>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        query.GetJourneyAsync("REF-1", Arg.Any<CancellationToken>())
            .Returns(new[] { new JourneyEvent("Submitted", "REF-1", "rules-engine", null, 0, t0.AddSeconds(2)) });
        query.GetJourneyAsync("REF-2", Arg.Any<CancellationToken>())
            .Returns(new[] { new JourneyEvent("Submitted", "REF-2", "rules-engine", null, 0, t0.AddSeconds(1)) });

        var controller = BuildController(query);

        var result = await controller.ReplaySelected(new[] { "REF-1", "REF-2" });

        var json = Assert.IsType<JsonResult>(result);
        var events = Assert.IsAssignableFrom<IEnumerable<JourneyEvent>>(json.Value).ToList();
        Assert.Equal(2, events.Count);

        // Combined and ordered by recorded time: REF-2 (1s) before REF-1 (2s).
        Assert.Equal("REF-2", events[0].ReferenceNumber);
        Assert.Equal("REF-1", events[1].ReferenceNumber);
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
