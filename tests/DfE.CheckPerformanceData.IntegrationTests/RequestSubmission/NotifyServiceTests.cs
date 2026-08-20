using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using Microsoft.Extensions.Logging;
using Notify.Client;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

public sealed class NotifyServiceTests
{
    private const string NotifyApiKeyEnvVar = "NOTIFY_API_KEY";
    private const string ConfirmTemplateIdEnvVar = "NOTIFY_CONFIRM_TEMPLATE_ID";
    private const string SubmissionTemplateIdEnvVar = "NOTIFY_SUBMISSION_TEMPLATE_ID";
    private const string WithdrawTemplateIdEnvVar = "NOTIFY_WITHDRAW_TEMPLATE_ID";
    private const string TestRefNumber = "INT-TEST-REF-001";
    private const string TestEmail = "test@example.com";
    private const string FallbackTemplateId = "test-template-id";

    [Fact]
    public async Task SendNotificationsAsync_WithSubmissionConfirmed_SendsEmail()
    {
        var apiKey = Environment.GetEnvironmentVariable(NotifyApiKeyEnvVar);
        if (string.IsNullOrEmpty(apiKey))
            return;

        var settings = CreateSettings(apiKey);
        settings.SubmissionNotificationTemplateId = Environment.GetEnvironmentVariable(SubmissionTemplateIdEnvVar) ?? FallbackTemplateId;

        var service = CreateService(settings);

        var recipients = new[] { TestEmail };

        var exception = await Record.ExceptionAsync(() =>
            service.SendNotificationsAsync(
                TestRefNumber, "5pm on Friday 26 June 2026", recipients, NotificationType.SubmissionConfirmed,
                new EmailSubstitutions("KS4 June", "Pupil", ""), "https://example.com/submit-others"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendNotificationsAsync_WithDataCheckConfirmed_SendsEmail()
    {
        var apiKey = Environment.GetEnvironmentVariable(NotifyApiKeyEnvVar);
        if (string.IsNullOrEmpty(apiKey))
            return;

        var settings = CreateSettings(apiKey);
        settings.PupilDataCheckConfirmTemplateId = Environment.GetEnvironmentVariable(ConfirmTemplateIdEnvVar) ?? FallbackTemplateId;

        var service = CreateService(settings);

        var recipients = new[] { TestEmail };

        var exception = await Record.ExceptionAsync(() =>
            service.SendNotificationsAsync(
                TestRefNumber, "5pm on Friday 26 June 2026", recipients, NotificationType.DataCheckConfirmed,
                new EmailSubstitutions("KS4 June", "Pupil", "")));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendNotificationsAsync_WithAmendmentWithdrawn_SendsEmail()
    {
        var apiKey = Environment.GetEnvironmentVariable(NotifyApiKeyEnvVar);
        if (string.IsNullOrEmpty(apiKey))
            return;

        var settings = CreateSettings(apiKey);
        settings.WithdrawNotificationTemplateId = Environment.GetEnvironmentVariable(WithdrawTemplateIdEnvVar) ?? FallbackTemplateId;

        var service = CreateService(settings);

        var recipients = new[] { TestEmail };

        var exception = await Record.ExceptionAsync(() =>
            service.SendNotificationsAsync(
                TestRefNumber, "5pm on Friday 26 June 2026", recipients, NotificationType.AmendmentWithdrawn,
                new EmailSubstitutions("KS4 June", "Pupil", "")));

        Assert.Null(exception);
    }

    private static NotifySettings CreateSettings(string apiKey) => new()
    {
        ApiKey = apiKey,
        PupilDataCheckConfirmTemplateId = FallbackTemplateId,
        PupilDataCheckWithdrawTemplateId = FallbackTemplateId,
        SubmissionNotificationTemplateId = FallbackTemplateId,
        WithdrawNotificationTemplateId = FallbackTemplateId,
        DlqThresholdTemplateId = FallbackTemplateId,
    };

    private static NotifyService CreateService(NotifySettings settings)
    {
        var client = new NotificationClient(settings.ApiKey);
        var emailClient = new NotifyEmailClient(client);
        var options = Options.Create(settings);
        var logger = Substitute.For<ILogger<NotifyService>>();
        return new NotifyService(emailClient, options, logger);
    }
}
