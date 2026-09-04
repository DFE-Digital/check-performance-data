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

    // An enquiry carries no checking-exercise substitutions: ce name, learner noun and turnaround
    // commitment are never sent for this notification type.
    private static readonly EmailSubstitutions NoSubstitutions = new(string.Empty, string.Empty, string.Empty);

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
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions);

        await _client.Received(1).SendEmailAsync(
            "ada@school.test", EnquiryTemplateId, Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task The_reference_number_reaches_the_template()
    {
        // The whole point of the email: the school needs the reference to quote back.
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                (string)p["ref number"] == "CYPMD_16to19_RE_4F9C2A1" &&
                (string)p["email address"] == "ada@school.test"));
    }

    [Fact]
    public async Task The_personalisation_is_exactly_what_the_template_declares()
    {
        // GOV.UK Notify ignores extra personalisation keys but fails the whole send with
        // "Missing personalisation" when the template declares a placeholder we don't supply.
        // The exact two-key contract is what lets the ops runbook (docs/results-enquiry.md,
        // "Confirmation email") be the whole truth: a template built from it can never hit a
        // missing key, and no template can come to depend on a key the code might stop sending.
        // The url and reference list are supplied here precisely so the optional-key branches
        // are exercised: exclusion for this type must be structural, not "the caller happens
        // to pass null" (the enquiry producer sets neither — RequestNotificationService).
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions,
            url: "https://service.test/submit-others", referenceNumbers: ["REF-A", "REF-B"]);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                p.Count == 2 && p.ContainsKey("email address") && p.ContainsKey("ref number")));
    }

    [Fact]
    public async Task A_turnaround_commitment_never_reaches_the_enquiry_template()
    {
        // Unreachable via RequestNotificationService today (enquiry notifications enqueue empty
        // substitutions), but the key gate must not depend on that: a future refactor passing real
        // substitutions through would otherwise bounce every enquiry email against the template.
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted,
            new EmailSubstitutions(string.Empty, string.Empty, "2 working days"));

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p => !p.ContainsKey("turnaround commitment")));
    }

    [Fact]
    public async Task Amendment_notifications_still_carry_their_deadline()
    {
        // The deadline key moves behind a type gate in this change; the five amendment-side
        // templates all declare ((deadline)) and must keep receiving it.
        await Build().SendNotificationsAsync(
            "REF-1", "4pm on Friday 3 October 2026", ["ada@school.test"],
            NotificationType.SubmissionConfirmed, NoSubstitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                (string)p["deadline"] == "4pm on Friday 3 October 2026"));
    }

    [Fact]
    public async Task Every_recipient_is_emailed()
    {
        await Build().SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["one@school.test", "two@school.test"],
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions);

        await _client.Received(2).SendEmailAsync(
            Arg.Any<string>(), EnquiryTemplateId, Arg.Any<Dictionary<string, object>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_unconfigured_template_logs_a_warning_and_sends_nothing(string? templateId)
    {
        // An environment may have no template id configured. When that happens, the send must be a
        // no-op with a warning — not an exception, and not a call to Notify with a blank template that
        // would fail once per recipient.
        await Build(templateId).SendNotificationsAsync(
            "CYPMD_16to19_RE_4F9C2A1", string.Empty, ["ada@school.test"],
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions);

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
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions);
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
            NotificationType.ResultsEnquirySubmitted, NoSubstitutions);

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

            await service.SendNotificationsAsync("REF-1", "a deadline", ["ada@school.test"], type, NoSubstitutions);

            await _client.Received(1).SendEmailAsync(
                "ada@school.test", expected, Arg.Any<Dictionary<string, object>>());
        }
    }
}
