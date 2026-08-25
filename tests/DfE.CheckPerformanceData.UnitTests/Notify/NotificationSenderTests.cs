using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Notify;

public sealed class NotificationSenderTests
{
    private const string ReferenceNumber = "REF001";
    private const string OriginatorEmail = "current.user@education.gov.uk";
    private const string Ukprn = "10000000";

    private readonly INotifyService _notifyService = Substitute.For<INotifyService>();
    private readonly IDfESignInApiClient _dfESignInApiClient = Substitute.For<IDfESignInApiClient>();
    private readonly NotificationSender _sut;

    public NotificationSenderTests()
    {
        _sut = new NotificationSender(_notifyService, _dfESignInApiClient);
    }

    private static EmailNotification Message(
        NotificationType type = NotificationType.SubmissionConfirmed,
        bool includeOrganisationUsers = true,
        string deadline = "5pm on Friday 26 June 2026",
        string? linkUrl = null,
        string ceName = "KS4 June",
        string learnerNoun = "Pupil",
        string turnaroundCommitment = "updated in the Autumn") =>
        new()
        {
            Type = type,
            ReferenceNumber = ReferenceNumber,
            Deadline = deadline,
            LinkUrl = linkUrl,
            Ukprn = Ukprn,
            OriginatorEmail = OriginatorEmail,
            IncludeOrganisationUsers = includeOrganisationUsers,
            CeName = ceName,
            LearnerNoun = learnerNoun,
            TurnaroundCommitment = turnaroundCommitment
        };

    [Fact]
    public async Task SendAsync_WhenIncludingOrgUsers_ResolvesRecipientsFromOriginatorAndOrgUsers()
    {
        var orgUserEmail = "org.user@school.gov.uk";
        _dfESignInApiClient.GetOrganisationUsersAsync(Ukprn)
            .Returns(new OrganisationUsersResponseDto
            {
                Users = [new OrganisationUserDto { FirstName = "Org", LastName = "User", Email = orgUserEmail }]
            });

        await _sut.SendAsync(Message());

        await _notifyService.Received(1).SendNotificationsAsync(
            ReferenceNumber,
            Arg.Any<string>(),
            Arg.Is<IReadOnlyCollection<string>>(r =>
                r.Contains(OriginatorEmail) && r.Contains(orgUserEmail) && r.Count == 2),
            Arg.Any<NotificationType>(),
            Arg.Any<EmailSubstitutions>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task SendAsync_WhenNotIncludingOrgUsers_SendsToOriginatorOnlyAndSkipsLookup()
    {
        await _sut.SendAsync(Message(
            type: NotificationType.AmendmentWithdrawn, includeOrganisationUsers: false, deadline: string.Empty));

        await _dfESignInApiClient.DidNotReceive().GetOrganisationUsersAsync(Arg.Any<string>());
        await _notifyService.Received(1).SendNotificationsAsync(
            ReferenceNumber,
            string.Empty,
            Arg.Is<IReadOnlyCollection<string>>(r => r.Count == 1 && r.Contains(OriginatorEmail)),
            NotificationType.AmendmentWithdrawn,
            Arg.Any<EmailSubstitutions>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task SendAsync_DeduplicatesRecipientsCaseInsensitively()
    {
        var duplicateEmail = "Current.User@education.gov.uk";
        _dfESignInApiClient.GetOrganisationUsersAsync(Ukprn)
            .Returns(new OrganisationUsersResponseDto
            {
                Users = [new OrganisationUserDto { FirstName = "Current", LastName = "User", Email = duplicateEmail }]
            });

        await _sut.SendAsync(Message());

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyCollection<string>>(r => r.Count == 1 && r.Contains(OriginatorEmail)),
            Arg.Any<NotificationType>(),
            Arg.Any<EmailSubstitutions>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task SendAsync_PassesTypeDeadlineAndLinkThroughToNotifyService()
    {
        var linkUrl = "https://example.gov.uk/WhatToChange/Index?windowId=aaa";
        _dfESignInApiClient.GetOrganisationUsersAsync(Ukprn)
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.SendAsync(Message(deadline: "5pm on Friday 26 June 2026", linkUrl: linkUrl));

        await _notifyService.Received(1).SendNotificationsAsync(
            ReferenceNumber,
            "5pm on Friday 26 June 2026",
            Arg.Any<IReadOnlyCollection<string>>(),
            NotificationType.SubmissionConfirmed,
            Arg.Is<EmailSubstitutions>(s =>
                s.CeName == "KS4 June" && s.LearnerNoun == "Pupil" &&
                s.TurnaroundCommitment == "updated in the Autumn"),
            linkUrl);
    }

    [Fact]
    public async Task SendAsync_PassesSubstitutionFieldsThroughToNotifyService()
    {
        _dfESignInApiClient.GetOrganisationUsersAsync(Ukprn)
            .Returns(new OrganisationUsersResponseDto { Users = [] });

        await _sut.SendAsync(Message(
            ceName: "16 to 19", learnerNoun: "Student", turnaroundCommitment: "updated in the Spring"));

        await _notifyService.Received(1).SendNotificationsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<NotificationType>(),
            Arg.Is<EmailSubstitutions>(s =>
                s.CeName == "16 to 19" && s.LearnerNoun == "Student" &&
                s.TurnaroundCommitment == "updated in the Spring"),
            Arg.Any<string?>(),
            Arg.Any<IReadOnlyCollection<string>?>());
    }
}
