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
    private const string DeadlineText = "28 February 2025";
    private const string TestRefNumber = "INT-TEST-REF-001";
    private const string TestEmail = "test@example.com";
    private const string FallbackTemplateId = "test-template-id";

    [Fact]
    public async Task SendPupilDataCheckConfirmAsync_SendsEmailWithCorrectTemplate()
    {
        var apiKey = Environment.GetEnvironmentVariable(NotifyApiKeyEnvVar);
        if (string.IsNullOrEmpty(apiKey))
            return;

        var settings = CreateSettings(apiKey);
        settings.PupilDataCheckConfirmTemplateId = Environment.GetEnvironmentVariable(ConfirmTemplateIdEnvVar) ?? FallbackTemplateId;

        var service = CreateService(settings);

        var exception = await Record.ExceptionAsync(() =>
            service.SendPupilDataCheckConfirmAsync(TestEmail, TestRefNumber, DeadlineText));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendSubmissionNotificationAsync_SendsEmailWithCorrectTemplate()
    {
        var apiKey = Environment.GetEnvironmentVariable(NotifyApiKeyEnvVar);
        if (string.IsNullOrEmpty(apiKey))
            return;

        var settings = CreateSettings(apiKey);
        settings.SubmissionNotificationTemplateId = Environment.GetEnvironmentVariable(SubmissionTemplateIdEnvVar) ?? FallbackTemplateId;

        var service = CreateService(settings);

        var exception = await Record.ExceptionAsync(() =>
            service.SendSubmissionNotificationAsync(TestEmail, TestRefNumber, DeadlineText, "https://example.com/submit-others"));

        Assert.Null(exception);
    }

    private static NotifySettings CreateSettings(string apiKey) => new()
    {
        ApiKey = apiKey,
        PupilDataCheckConfirmTemplateId = FallbackTemplateId,
        PupilDataCheckWithdrawTemplateId = FallbackTemplateId,
        SubmissionNotificationTemplateId = FallbackTemplateId,
        WithdrawNotificationTemplateId = FallbackTemplateId,
        DeadlineText = DeadlineText,
    };

    private static NotifyService CreateService(NotifySettings settings)
    {
        var client = new NotificationClient(settings.ApiKey);
        var options = Options.Create(settings);
        var logger = Substitute.For<ILogger<NotifyService>>();
        return new NotifyService(client, options, logger);
    }
}
