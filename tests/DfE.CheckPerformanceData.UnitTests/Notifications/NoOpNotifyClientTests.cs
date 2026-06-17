using DfE.CheckPerformanceData.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Notifications;

public sealed class NoOpNotifyClientTests
{
    [Fact]
    public async Task SendEmailAsync_LogsWarning_AndDoesNotThrow()
    {
        var logger = Substitute.For<ILogger<NoOpNotifyClient>>();
        var sut = new NoOpNotifyClient(logger);

        await sut.SendEmailAsync(
            "ops@example.com",
            new Dictionary<string, object> { ["dlq_depth"] = 11 });

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SendEmailAsync_WhenCancelled_Throws()
    {
        var sut = new NoOpNotifyClient(Substitute.For<ILogger<NoOpNotifyClient>>());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await sut.SendEmailAsync(
                "ops@example.com",
                new Dictionary<string, object> { ["dlq_depth"] = 11 },
                cts.Token));
    }
}
