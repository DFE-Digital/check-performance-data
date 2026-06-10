using DfE.CheckPerformanceData.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notify.Interfaces;
using Notify.Models.Responses;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Notifications;

public sealed class GovukNotifyClientTests
{
    private const string TemplateId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task SendEmailAsync_WithConfiguredClient_SendsViaNotificationClient()
    {
        var notify = Substitute.For<INotificationClient>();
        notify.SendEmail(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, dynamic>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(new EmailNotificationResponse());

        var sut = new GovukNotifyClient(notify, TemplateId, NullLogger<GovukNotifyClient>.Instance);

        await sut.SendEmailAsync(
            "ops@example.com",
            new Dictionary<string, dynamic> { ["dlq_depth"] = 11 });

        notify.Received(1).SendEmail(
            "ops@example.com",
            TemplateId,
            Arg.Any<Dictionary<string, dynamic>>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task SendEmailAsync_WithoutApiKey_DegradesToWarning_AndDoesNotThrow()
    {
        var logger = Substitute.For<ILogger<GovukNotifyClient>>();

        // No underlying Notify client => API key was not configured.
        var sut = new GovukNotifyClient(notificationClient: null, templateId: TemplateId, logger);

        await sut.SendEmailAsync(
            "ops@example.com",
            new Dictionary<string, dynamic> { ["dlq_depth"] = 11 });

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
