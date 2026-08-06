using DfE.CheckPerformanceData.Web.Controllers.ViewModels;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.ViewComponents;

// The admin chrome's Messages badge replaced two standalone badges, one of which — the
// dead-letter queue — rendered RED on any non-zero count. The consolidated badge summed
// both counts into one blue tag, so messages dead-lettering in production looked exactly
// like a calm inbox and operators lost the at-a-glance alarm.
//
// The tag colour therefore has to key off the dead-letter count specifically, not the
// combined total. Red outranks blue: a queue that is dead-lettering is the thing an
// operator needs to see first, whatever the feedback inbox is doing.
public sealed class MessagesBadgeViewModelTests
{
    [Fact]
    public void DeadLetteredMessages_RenderRed_EvenWhenTheInboxIsAlsoUnread()
    {
        var model = new MessagesBadgeViewModel(UnreadMessages: 4, DeadLetterCount: 2);

        Assert.Equal(6, model.Total);
        Assert.Equal("govuk-tag--red", model.TagClass);
    }

    [Fact]
    public void ASingleDeadLetteredMessage_IsEnoughToRaiseTheAlarm()
    {
        var model = new MessagesBadgeViewModel(UnreadMessages: 0, DeadLetterCount: 1);

        Assert.Equal(1, model.Total);
        Assert.Equal("govuk-tag--red", model.TagClass);
    }

    [Fact]
    public void UnreadFeedbackAlone_RendersBlue()
    {
        var model = new MessagesBadgeViewModel(UnreadMessages: 3, DeadLetterCount: 0);

        Assert.Equal(3, model.Total);
        Assert.Equal("govuk-tag--blue", model.TagClass);
    }

    [Fact]
    public void NothingWaiting_RendersGrey()
    {
        var model = new MessagesBadgeViewModel(UnreadMessages: 0, DeadLetterCount: 0);

        Assert.Equal(0, model.Total);
        Assert.Equal("govuk-tag--grey", model.TagClass);
    }
}
