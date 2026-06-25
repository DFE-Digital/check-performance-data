using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

public sealed class RequestNotificationServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime EndDate = new(2026, 6, 26, 17, 0, 0);
    private const string ReferenceNumber = "REF001";
    private const string CurrentUserEmail = "current.user@education.gov.uk";

    private readonly INotifyService _notifyService = Substitute.For<INotifyService>();
    private readonly IDfESignInApiClient _dfESignInApiClient = Substitute.For<IDfESignInApiClient>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly IEmailLinkGenerator _emailLinkGenerator = Substitute.For<IEmailLinkGenerator>();
    private readonly RequestNotificationService _sut;

    private static bool MatchWindowId(object o, Guid windowId)
    {
        var prop = o.GetType().GetProperty("windowId");
        return prop != null && prop.GetValue(o)?.ToString() == windowId.ToString();
    }

    public RequestNotificationServiceTests()
    {
        _currentUserService.Email.Returns(CurrentUserEmail);
        _currentUserService.Ukprn.Returns("10000000");
        _sut = new RequestNotificationService(
            _notifyService, _dfESignInApiClient, _currentUserService, _emailLinkGenerator);
    }

    // ── NotifySubmissionConfirmedAsync ───────────────────────────────────────

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_ResolvesRecipientsFromCurrentUserAndOrgUsers()
    {
        var orgUserEmail = "org.user@school.gov.uk";
        _dfESignInApiClient.GetOrganisationUsersAsync("10000000")
            .Returns(new OrganisationUsersResponseDto
            {
                Users = [new OrganisationUserDto { FirstName = "Org", LastName = "User", Email = orgUserEmail }]
            });

        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            ReferenceNumber,
            Arg.Any<string>(),
            Arg.Is<IReadOnlyCollection<string>>(r =>
                r.Contains(CurrentUserEmail) && r.Contains(orgUserEmail) && r.Count == 2),
            Arg.Any<NotificationType>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_FormatsDeadlineCorrectly()
    {
        _dfESignInApiClient.GetOrganisationUsersAsync(Arg.Any<string>(), Arg.Any<string[]>())
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            "5pm on Friday 26 June 2026",
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<NotificationType>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_GeneratesLinkAndPassesToNotifyService()
    {
        var linkUrl = "https://example.gov.uk/WhatToChange/Index?windowId=aaa";
        _emailLinkGenerator.GenerateLink("WhatToChange", "Index", Arg.Is<object>(o => MatchWindowId(o, WindowId)), "SubmissionNotification")
            .Returns(linkUrl);
        _dfESignInApiClient.GetOrganisationUsersAsync(Arg.Any<string>(), Arg.Any<string[]>())
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<string>>(),
            NotificationType.SubmissionConfirmed,
            linkUrl);
    }

    // ── NotifyDataCheckConfirmedAsync ────────────────────────────────────────

    [Fact]
    public async Task NotifyDataCheckConfirmedAsync_SendsWithDataCheckTypeAndNoUrl()
    {
        _dfESignInApiClient.GetOrganisationUsersAsync(Arg.Any<string>())
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.NotifyDataCheckConfirmedAsync(EndDate, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            ReferenceNumber,
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<string>>(),
            NotificationType.DataCheckConfirmed);
    }

    // ── Recipient deduplication ──────────────────────────────────────────────

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_DeduplicatesRecipientsCaseInsensitively()
    {
        var duplicateEmail = "Current.User@education.gov.uk";
        _dfESignInApiClient.GetOrganisationUsersAsync("10000000")
            .Returns(new OrganisationUsersResponseDto
            {
                Users = [new OrganisationUserDto { FirstName = "Current", LastName = "User", Email = duplicateEmail }]
            });

        await _sut.NotifySubmissionConfirmedAsync(WindowId, EndDate, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyCollection<string>>(r => r.Count == 1 && r.Contains(CurrentUserEmail)),
            Arg.Any<NotificationType>(),
            Arg.Any<string?>());
    }

    // ── Deadline edge cases ──────────────────────────────────────────────────

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_HandlesMidnightDeadline()
    {
        var midnight = new DateTime(2026, 6, 26, 0, 0, 0);
        _dfESignInApiClient.GetOrganisationUsersAsync(Arg.Any<string>())
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.NotifySubmissionConfirmedAsync(WindowId, midnight, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            "12am on Friday 26 June 2026",
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<NotificationType>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task NotifySubmissionConfirmedAsync_HandlesNoonDeadline()
    {
        var noon = new DateTime(2026, 6, 26, 12, 0, 0);
        _dfESignInApiClient.GetOrganisationUsersAsync(Arg.Any<string>())
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.NotifySubmissionConfirmedAsync(WindowId, noon, ReferenceNumber);

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            "12pm on Friday 26 June 2026",
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<NotificationType>(),
            Arg.Any<string?>());
    }
}
