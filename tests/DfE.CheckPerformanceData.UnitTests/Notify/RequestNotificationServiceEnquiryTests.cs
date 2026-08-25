using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

// AB#296648: what the producer side puts on the notification queue for a submitted results enquiry.
//
// Two choices are deliberate and worth pinning. There is no deadline — an enquiry is not something the
// school must come back and finish before the window shuts — and it goes to the submitter only, since
// nothing is being asked of the rest of the school.
public sealed class RequestNotificationServiceEnquiryTests
{
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IEmailLinkGenerator _linkGenerator = Substitute.For<IEmailLinkGenerator>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();
    private readonly RequestNotificationService _sut;

    public RequestNotificationServiceEnquiryTests()
    {
        _currentUser.Ukprn.Returns("10001234");
        _currentUser.Email.Returns("ada@school.test");

        _sut = new RequestNotificationService(
            _currentUser, _linkGenerator, _dispatcher,
            Options.Create(new NotifySettings { ApiKey = "test-key" }));
    }

    [Fact]
    public async Task One_notification_is_queued_for_the_enquiry()
    {
        await _sut.NotifyResultsEnquirySubmittedAsync("CYPMD_16to19_RE_4F9C2A1");

        await _dispatcher.Received(1).EnqueueAsync(Arg.Is<EmailNotification>(n =>
            n.Type == NotificationType.ResultsEnquirySubmitted &&
            n.ReferenceNumber == "CYPMD_16to19_RE_4F9C2A1"));
    }

    [Fact]
    public async Task It_carries_the_submitter_and_their_organisation()
    {
        await _sut.NotifyResultsEnquirySubmittedAsync("CYPMD_16to19_RE_4F9C2A1");

        await _dispatcher.Received(1).EnqueueAsync(Arg.Is<EmailNotification>(n =>
            n.OriginatorEmail == "ada@school.test" && n.Ukprn == "10001234"));
    }

    [Fact]
    public async Task It_goes_to_the_submitter_only()
    {
        // An enquiry asks nothing of the rest of the school, so copying every user would be noise.
        await _sut.NotifyResultsEnquirySubmittedAsync("CYPMD_16to19_RE_4F9C2A1");

        await _dispatcher.Received(1).EnqueueAsync(
            Arg.Is<EmailNotification>(n => !n.IncludeOrganisationUsers));
    }

    [Fact]
    public async Task It_carries_no_deadline()
    {
        // Unlike an amendment, there is nothing for the school to finish before the window closes —
        // so a deadline in this email would be misleading.
        await _sut.NotifyResultsEnquirySubmittedAsync("CYPMD_16to19_RE_4F9C2A1");

        await _dispatcher.Received(1).EnqueueAsync(
            Arg.Is<EmailNotification>(n => n.Deadline == string.Empty));
    }

    [Fact]
    public async Task It_needs_no_link_generation()
    {
        // The submission email links back into the journey; this one does not, so it must not depend
        // on HttpContext-backed link generation.
        await _sut.NotifyResultsEnquirySubmittedAsync("CYPMD_16to19_RE_4F9C2A1");

        _linkGenerator.DidNotReceiveWithAnyArgs().GenerateLink(default!, default!, default!, default!);
    }
}
