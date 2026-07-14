using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

public sealed class RequestNotificationServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime EndDate = new(2026, 6, 26, 17, 0, 0);
    private const string ReferenceNumber = "REF001";
    private const string CurrentUserEmail = "current.user@education.gov.uk";
    private const string Ukprn = "10000000";

    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly IEmailLinkGenerator _emailLinkGenerator = Substitute.For<IEmailLinkGenerator>();
    private readonly NotifySettings _settings = new()
    {
        ApiKey = "x",
        BulkConsolidationThreshold = 5
    };
    private readonly RequestNotificationService _sut;

    private static bool MatchWindowId(object o, Guid windowId)
    {
        var prop = o.GetType().GetProperty("windowId");
        return prop != null && prop.GetValue(o)?.ToString() == windowId.ToString();
    }

    private EmailNotification? Captured() =>
        (EmailNotification?)_dispatcher.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(INotificationDispatcher.EnqueueAsync))
            ?.GetArguments()[0];

    private IReadOnlyList<EmailNotification> AllCaptured() =>
        _dispatcher.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(INotificationDispatcher.EnqueueAsync))
            .Select(c => (EmailNotification)c.GetArguments()[0]!)
            .ToList();

    public RequestNotificationServiceTests()
    {
        _currentUserService.Email.Returns(CurrentUserEmail);
        _currentUserService.Ukprn.Returns(Ukprn);
        _sut = new RequestNotificationService(
            _currentUserService, _emailLinkGenerator, _dispatcher,
            Options.Create(_settings));
    }

    // ── NotifySubmissionConfirmedAsync ───────────────────────────────────────

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_EnqueuesSubmissionMessageWithOrgUsersAndOriginator()
    {
        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        var msg = Captured();
        Assert.NotNull(msg);
        Assert.Equal(NotificationType.SubmissionConfirmed, msg!.Type);
        Assert.Equal(ReferenceNumber, msg.ReferenceNumber);
        Assert.Equal(CurrentUserEmail, msg.OriginatorEmail);
        Assert.Equal(Ukprn, msg.Ukprn);
        Assert.True(msg.IncludeOrganisationUsers);
    }

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_FormatsDeadlineCorrectly()
    {
        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        Assert.Equal("5pm on Friday 26 June 2026", Captured()!.Deadline);
    }

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_GeneratesLinkOnRequestThreadAndCarriesItInMessage()
    {
        var linkUrl = "https://example.gov.uk/WhatToChange/Index?windowId=aaa";
        _emailLinkGenerator.GenerateLink(
            "WhatToChange", "Index", Arg.Is<object>(o => MatchWindowId(o, WindowId)), "SubmissionNotification")
            .Returns(linkUrl);

        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        Assert.Equal(linkUrl, Captured()!.LinkUrl);
    }

    [Theory]
    [InlineData(0, 0, "12am on Friday 26 June 2026")]
    [InlineData(12, 0, "12pm on Friday 26 June 2026")]
    public async Task NotifySubmissionConfirmedAsync_HandlesDeadlineEdgeCases(int hour, int minute, string expected)
    {
        await _sut.NotifySubmissionConfirmedAsync(
            WindowId, new DateTime(2026, 6, 26, hour, minute, 0), ReferenceNumber);

        Assert.Equal(expected, Captured()!.Deadline);
    }

    // ── NotifyDataCheckConfirmedAsync ────────────────────────────────────────

    [Fact]
    public async Task NotifyDataCheckConfirmedAsync_EnqueuesDataCheckMessageWithOrgUsersAndNoLink()
    {
        await _sut.NotifyDataCheckConfirmedAsync(EndDate, ReferenceNumber);

        var msg = Captured();
        Assert.NotNull(msg);
        Assert.Equal(NotificationType.DataCheckConfirmed, msg!.Type);
        Assert.Equal(ReferenceNumber, msg.ReferenceNumber);
        Assert.Equal("5pm on Friday 26 June 2026", msg.Deadline);
        Assert.Null(msg.LinkUrl);
        Assert.True(msg.IncludeOrganisationUsers);
    }

    // ── NotifyAmendmentWithdrawnAsync ─────────────────────────────────────────

    [Fact]
    public async Task NotifyAmendmentWithdrawnAsync_EnqueuesOriginatorOnlyMessage()
    {
        await _sut.NotifyAmendmentWithdrawnAsync(ReferenceNumber, EndDate);

        var msg = Captured();
        Assert.NotNull(msg);
        Assert.Equal(NotificationType.AmendmentWithdrawn, msg!.Type);
        Assert.Equal(ReferenceNumber, msg.ReferenceNumber);
        Assert.Equal("5pm on Friday 26 June 2026", msg.Deadline);
        Assert.Equal(CurrentUserEmail, msg.OriginatorEmail);
        Assert.False(msg.IncludeOrganisationUsers);
    }

    // ── NotifyDataCheckWithdrawnAsync ─────────────────────────────────────────

    [Fact]
    public async Task NotifyDataCheckWithdrawnAsync_EnqueuesMessageWithOrgUsers()
    {
        await _sut.NotifyDataCheckWithdrawnAsync(ReferenceNumber, EndDate);

        var msg = Captured();
        Assert.NotNull(msg);
        Assert.Equal(NotificationType.DataCheckWithdrawn, msg!.Type);
        Assert.Equal(ReferenceNumber, msg.ReferenceNumber);
        Assert.Equal("5pm on Friday 26 June 2026", msg.Deadline);
        Assert.True(msg.IncludeOrganisationUsers);
    }

    // ── No external calls on the request thread ──────────────────────────────

    [Fact]
    public async Task Notify_DoesNotResolveLinkForNonSubmissionNotifications()
    {
        await _sut.NotifyDataCheckConfirmedAsync(EndDate, ReferenceNumber);
        await _sut.NotifyAmendmentWithdrawnAsync(ReferenceNumber, EndDate);
        await _sut.NotifyDataCheckWithdrawnAsync(ReferenceNumber, EndDate);

        _emailLinkGenerator.DidNotReceive().GenerateLink(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
    }

    // ── NotifyBulkSubmissionConfirmedAsync ───────────────────────────────────

    [Fact]
    public async Task NotifyBulk_BelowThreshold_EnqueuesOneIndividualPerReference()
    {
        var refs = new[] { "R1", "R2", "R3", "R4" };

        await _sut.NotifyBulkSubmissionConfirmedAsync(WindowId, EndDate, refs);

        var all = AllCaptured();
        Assert.Equal(4, all.Count);
        Assert.All(all, m => Assert.Equal(NotificationType.SubmissionConfirmed, m.Type));
        Assert.Equal(new[] { "R1", "R2", "R3", "R4" }, all.Select(m => m.ReferenceNumber));
    }

    [Fact]
    public async Task NotifyBulk_AtThreshold_EnqueuesSingleConsolidated()
    {
        var refs = new[] { "R1", "R2", "R3", "R4", "R5" };

        await _sut.NotifyBulkSubmissionConfirmedAsync(WindowId, EndDate, refs);

        var all = AllCaptured();
        Assert.Single(all);
        Assert.Equal(NotificationType.BulkSubmissionConfirmed, all[0].Type);
        Assert.Equal(refs, all[0].ReferenceNumbers);
        Assert.True(all[0].IncludeOrganisationUsers);
    }
}
