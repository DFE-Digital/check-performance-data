using System.Reflection;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The queues index is redesigned to render all three queues (rules-engine, zendesk,
// dead-letter) through one reused panel: count + oldest-age + top-5 + redaction-gated
// detail + a per-queue view-all action.
public sealed class QueueAdminTopNTests
{
    // --- The index model carries all three queues, each with count, oldest age and a top-5 ---

    [Fact]
    public async Task Index_ReturnsModelForAllThreeQueues_WithCountOldestAgeAndTopMessages()
    {
        var adminService = Substitute.For<IQueueAdminService>();

        adminService.GetQueueDepthsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new QueueDepth(QueueOptions.RulesEngineQueue, 7, TimeSpan.FromMinutes(3)),
            new QueueDepth(QueueOptions.ZendeskQueue, 2, TimeSpan.FromMinutes(1)),
        });
        adminService.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(4);

        adminService.GetTopMessagesAsync(QueueOptions.RulesEngineQueue, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new QueueMessageSummary(Guid.NewGuid(), QueueOptions.RulesEngineQueue, DateTime.UtcNow, 1),
            });
        adminService.GetTopMessagesAsync(QueueOptions.ZendeskQueue, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<QueueMessageSummary>());

        var controller = new QueueAdminController(adminService);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<QueueIndexViewModel>(view.Model);

        // All three queues are present as panels.
        Assert.Equal(3, model.Panels.Count);
        Assert.Contains(model.Panels, p => p.QueueName == QueueOptions.RulesEngineQueue);
        Assert.Contains(model.Panels, p => p.QueueName == QueueOptions.ZendeskQueue);
        Assert.Contains(model.Panels, p => p.IsDeadLetter);

        var rules = model.Panels.Single(p => p.QueueName == QueueOptions.RulesEngineQueue);
        Assert.Equal(7, rules.Count);
        Assert.Equal(TimeSpan.FromMinutes(3), rules.OldestMessageAge);
        Assert.Single(rules.TopMessages);

        var dlq = model.Panels.Single(p => p.IsDeadLetter);
        Assert.Equal(4, dlq.Count);
    }

    // --- The top-N read is capped at five messages per queue ---

    [Fact]
    public async Task Index_RequestsAtMostFiveMessagesPerQueue()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        adminService.GetQueueDepthsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new QueueDepth(QueueOptions.RulesEngineQueue, 99, TimeSpan.FromMinutes(1)) });
        adminService.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(0);
        adminService.GetTopMessagesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<QueueMessageSummary>());

        var controller = new QueueAdminController(adminService);

        await controller.Index();

        await adminService.Received().GetTopMessagesAsync(
            QueueOptions.RulesEngineQueue, 5, Arg.Any<CancellationToken>());
    }

    // --- The per-queue view-all action returns a page of messages for the named queue ---

    [Fact]
    public async Task QueueList_ReturnsPageForNamedQueue()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var messages = new[]
        {
            new QueueMessageSummary(Guid.NewGuid(), QueueOptions.ZendeskQueue, DateTime.UtcNow, 1),
            new QueueMessageSummary(Guid.NewGuid(), QueueOptions.ZendeskQueue, DateTime.UtcNow, 2),
        };
        adminService.GetQueueMessagesPageAsync(QueueOptions.ZendeskQueue, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new QueueMessagesPage(messages, 2, 1, 20));

        var controller = new QueueAdminController(adminService);

        var result = await controller.QueueList(QueueOptions.ZendeskQueue);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<QueueListViewModel>(view.Model);
        Assert.Equal(QueueOptions.ZendeskQueue, model.QueueName);
        Assert.Equal(2, model.Messages.Count);
    }

    // --- The per-queue view-all pages by the CMS:PageLength setting, not an unbounded list ---

    [Fact]
    public async Task QueueList_PagesByTheConfiguredPageLength()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        adminService.GetQueueMessagesPageAsync(QueueOptions.ZendeskQueue, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new QueueMessagesPage(Array.Empty<QueueMessageSummary>(), 55, 2, 15));

        var settings = Substitute.For<DfE.CheckPerformanceData.Application.Settings.ISettingService>();
        settings.GetIntAsync(DfE.CheckPerformanceData.Application.Settings.SettingKeys.CmsPageLength).Returns(15);

        var controller = new QueueAdminController(adminService, settingService: settings);

        var result = await controller.QueueList(QueueOptions.ZendeskQueue, page: 2);

        var model = Assert.IsType<QueueListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(15, model.PageSize);
        Assert.Equal(55, model.TotalCount);
        Assert.Equal(4, model.TotalPages); // ceil(55 / 15)

        // The page size came from CMS:PageLength and the page index was honoured — never an
        // unbounded read.
        await adminService.Received(1).GetQueueMessagesPageAsync(
            QueueOptions.ZendeskQueue, 2, 15, Arg.Any<CancellationToken>());
        await adminService.DidNotReceive().GetQueueMessagesAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- An unknown queue name is rejected (server-side allow-list) ---

    [Fact]
    public async Task QueueList_RejectsUnknownQueueName()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var controller = new QueueAdminController(adminService);

        var result = await controller.QueueList("not-a-real-queue");

        Assert.IsType<NotFoundResult>(result);
        await adminService.DidNotReceive().GetQueueMessagesPageAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // --- The view-all action is role-gated to cypmd_admin ---

    // The class-level RequireAdminSection gate covers Index, QueueList and every other action.
    // Verified in QueueAdminControllerTests.Controller_Gated_By_RulesEngineQueue_Section — no
    // need to duplicate the assertion here per-action.
}
