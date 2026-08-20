using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

// AB#296648: the results-enquiry confirmation email.
//
// NotifyService resolves a template id from a switch whose default arm THROWS. Adding a
// NotificationType member without a case here means the email silently never sends in a deployed
// environment — the local DevConsoleNotifyService would still log it, so nothing would look wrong.
// That is the gap these tests close.
public sealed class NotifyServiceResultsEnquiryTests
{
    private const string EnquiryTemplateId = "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb";

    private readonly INotifyEmailClient _client = Substitute.For<INotifyEmailClient>();
    private readonly ILogger<NotifyService> _logger = Substitute.For<ILogger<NotifyService>>();

    private NotifyService Build(string? enquiryTemplateId = EnquiryTemplateId)
    {
        var settings = new NotifySettings
        {
            ApiKey = "test-key",
            SubmissionNotificationTemplateId = "sub-template",
            BulkSubmissionNotificationTemplateId = "bulk-template",
            PupilDataCheckConfirmTemplateId = "confirm-template",
            PupilDataCheckWithdrawTemplateId = "withdraw-check-template",
            WithdrawNotificationTemplateId = "withdraw-template",
            DlqThresholdTemplateId = "dlq-template",
            ResultsEnquirySubmittedTemplateId = enquiryTemplateId!
        };

        return new NotifyService(_client, Options.Create(settings), _logger);
    }

    [Fact]
    public async Task A_results_enquiry_notification_uses_its_configured_template()
    {
        // Without the switch case this throws ArgumentOutOfRangeException instead.
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted);

        await _client.Received(1).SendEmailAsync(
            "ada@school.test", EnquiryTemplateId, Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task The_reference_number_reaches_the_template()
    {
        // The whole point of the email: the school needs the reference to quote back.
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                (string)p["ref number"] == "CYPMD_16to19_RE_4F9C2A1" &&
                (string)p["email address"] == "ada@school.test"));
    }

    [Fact]
    public async Task Every_recipient_is_emailed()
    {
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["one@school.test", "two@school.test"],
            NotificationType.ResultsEnquirySubmitted);

        await _client.Received(2).SendEmailAsync(
            Arg.Any<string>(), EnquiryTemplateId, Arg.Any<Dictionary<string, object>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_unconfigured_template_logs_a_warning_and_sends_nothing(string? templateId)
    {
        // The template does not exist yet (an ops prerequisite). Until it does, the send must be a
        // no-op with a warning — not an exception, and not a call to Notify with a blank template that
        // would fail once per recipient.
        await Build(templateId).SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted);

        await _client.DidNotReceiveWithAnyArgs()
            .SendEmailAsync(default!, default!, default!);
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task An_unconfigured_template_does_not_throw()
    {
        // Submission has already succeeded by the time this runs; an exception here must not be able
        // to turn a successful enquiry into an error for the user.
        var service = Build(enquiryTemplateId: null);

        await service.SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted);
    }

    [Fact]
    public async Task A_send_failure_is_isolated_per_recipient()
    {
        // Existing behaviour that must survive: one bad address does not stop the others.
        var service = Build();
        _client.SendEmailAsync("bad@school.test", Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns<Task>(_ => throw new InvalidOperationException("Notify rejected it"));

        await service.SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["bad@school.test", "good@school.test"],
            NotificationType.ResultsEnquirySubmitted);

        await _client.Received(1).SendEmailAsync(
            "good@school.test", EnquiryTemplateId, Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task The_existing_notification_types_still_resolve_their_templates()
    {
        // Guards against the new case or the blank-template guard disturbing the live emails.
        var service = Build();

        foreach (var (type, expected) in new[]
                 {
                     (NotificationType.SubmissionConfirmed, "sub-template"),
                     (NotificationType.BulkSubmissionConfirmed, "bulk-template"),
                     (NotificationType.DataCheckConfirmed, "confirm-template"),
                     (NotificationType.AmendmentWithdrawn, "withdraw-template"),
                     (NotificationType.DataCheckWithdrawn, "withdraw-check-template"),
                 })
        {
            _client.ClearReceivedCalls();

            await service.SendNotificationsAsync("REF-1", "a deadline", ["ada@school.test"], type);

            await _client.Received(1).SendEmailAsync(
                "ada@school.test", expected, Arg.Any<Dictionary<string, object>>());
        }
    }
}
