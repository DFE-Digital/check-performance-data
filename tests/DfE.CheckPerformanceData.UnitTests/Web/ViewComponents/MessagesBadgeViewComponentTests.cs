using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.ViewComponents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.ViewComponents;

// The badge sits in the admin chrome on every page, so it has two jobs that pull against
// each other: surface the backlog, and never be able to break the layout. These pin both —
// the counts reach the view model separately (so the view can colour off the dead-letter
// count alone), and either backing service throwing degrades that side to zero instead of
// bubbling out of a view component render.
public sealed class MessagesBadgeViewComponentTests
{
    private readonly ISearchMessageService _messages = Substitute.For<ISearchMessageService>();
    private readonly IQueueAdminService _queue = Substitute.For<IQueueAdminService>();

    private MessagesBadgeViewComponent Sut()
    {
        var component = new MessagesBadgeViewComponent(_messages, _queue);
        // HttpContext is reached via ViewComponentContext for the RequestAborted token.
        component.ViewComponentContext = new ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        return component;
    }

    private static MessagesBadgeViewModel ModelOf(IViewComponentResult result)
    {
        var view = Assert.IsType<ViewViewComponentResult>(result);
        return Assert.IsType<MessagesBadgeViewModel>(view.ViewData!.Model);
    }

    [Fact]
    public async Task InvokeAsync_CarriesBothCountsSeparately_NotAPreSummedTotal()
    {
        _messages.GetUnreadCountAsync(Arg.Any<CancellationToken>()).Returns(4);
        _queue.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(2);

        var model = ModelOf(await Sut().InvokeAsync());

        Assert.Equal(4, model.UnreadMessages);
        Assert.Equal(2, model.DeadLetterCount);
        Assert.Equal(6, model.Total);
        // Colour keys off the dead-letter count, which is only possible because the two
        // counts arrive separately.
        Assert.Equal("govuk-tag--red", model.TagClass);
    }

    [Fact]
    public async Task InvokeAsync_UnreadFeedbackOnly_IsNotAnAlarm()
    {
        _messages.GetUnreadCountAsync(Arg.Any<CancellationToken>()).Returns(3);
        _queue.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        var model = ModelOf(await Sut().InvokeAsync());

        Assert.Equal("govuk-tag--blue", model.TagClass);
        Assert.Equal(3, model.Total);
    }

    [Fact]
    public async Task InvokeAsync_MessagesLookupThrows_DegradesThatSideToZero()
    {
        _messages.GetUnreadCountAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("inbox unavailable"));
        _queue.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(7);

        var model = ModelOf(await Sut().InvokeAsync());

        Assert.Equal(0, model.UnreadMessages);
        // The dead-letter side still reports, and still raises the alarm.
        Assert.Equal(7, model.DeadLetterCount);
        Assert.Equal("govuk-tag--red", model.TagClass);
    }

    [Fact]
    public async Task InvokeAsync_QueueLookupThrows_DegradesThatSideToZero()
    {
        _messages.GetUnreadCountAsync(Arg.Any<CancellationToken>()).Returns(5);
        _queue.GetDlqCountAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("queue unavailable"));

        var model = ModelOf(await Sut().InvokeAsync());

        Assert.Equal(5, model.UnreadMessages);
        Assert.Equal(0, model.DeadLetterCount);
        Assert.Equal("govuk-tag--blue", model.TagClass);
    }

    [Fact]
    public async Task InvokeAsync_BothLookupsThrow_StillRendersAQuietBadge()
    {
        // The badge is a passive indicator, never a hard dependency of the layout.
        _messages.GetUnreadCountAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("down"));
        _queue.GetDlqCountAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("down"));

        var model = ModelOf(await Sut().InvokeAsync());

        Assert.Equal(0, model.Total);
        Assert.Equal("govuk-tag--grey", model.TagClass);
    }
}
