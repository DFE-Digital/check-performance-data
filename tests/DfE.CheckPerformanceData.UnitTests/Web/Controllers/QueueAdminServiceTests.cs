using DfE.CheckPerformanceData.Application.Queue;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class QueueAdminServiceTests
{
    // The DLQ badge count must come from a COUNT(*) on the queue service, not by loading every
    // dead-letter row (and its unredacted payload) just to take .Count.
    [Fact]
    public async Task GetDlqCountAsync_UsesCountQuery_DoesNotMaterialiseMessages()
    {
        var queueService = Substitute.For<IQueueService>();
        queueService.GetDlqCountAsync(Arg.Any<CancellationToken>()).Returns(7);

        var sut = new QueueAdminService(queueService);

        var count = await sut.GetDlqCountAsync();

        Assert.Equal(7, count);
        await queueService.Received(1).GetDlqCountAsync(Arg.Any<CancellationToken>());
        await queueService.DidNotReceive().GetDlqMessagesAsync(Arg.Any<CancellationToken>());
    }
}
