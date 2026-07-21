using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.RequestSubmission;

public sealed class DuplicateRequestValidatorTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PupilId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const long OrganisationUrn = 100000;
    private static readonly Guid CurrentUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string ExistingRef = "REF-EXISTING";

    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly RequestService _sut;

    public DuplicateRequestValidatorTests()
    {
        _currentUser.UserId.Returns(CurrentUserId.ToString());
        _currentUser.DisplayName.Returns("Test User");
        _currentUser.Email.Returns("test.user@education.gov.uk");
        _currentUser.OrganisationUrn.Returns(OrganisationUrn.ToString());

        var requestStateBlobClient = Substitute.For<IRequestStateBlobClient>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<RequestService>>();
        var queueService = Substitute.For<IQueueService>();
        var requestNotificationService = Substitute.For<IRequestNotificationService>();
        var checkYourPupilDataService = Substitute.For<ICheckYourPupilDataService>();

        _sut = new RequestService(_flowService, requestStateBlobClient, _repository, _currentUser,
            logger, queueService, requestNotificationService, checkYourPupilDataService);
    }

    // ── CheckForConflictAsync (T007) ────────────────────────────────────────

    [Fact]
    public async Task CheckForConflictAsync_WhenNoConflictExists_ReturnsNoConflict()
    {
        _repository.CheckForConflictAsync(WindowId, PupilId, OrganisationUrn, string.Empty, CurrentUserId)
            .Returns(new DuplicateCheckResult.NoConflict());

        var result = await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        Assert.IsType<DuplicateCheckResult.NoConflict>(result);
    }

    [Fact]
    public async Task CheckForConflictAsync_WhenSelfSubmitted_ReturnsSelfSubmittedWithReference()
    {
        _repository.CheckForConflictAsync(WindowId, PupilId, OrganisationUrn, string.Empty, CurrentUserId)
            .Returns(new DuplicateCheckResult.SelfSubmitted(ExistingRef, "", "", ""));

        var result = await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        var self = Assert.IsType<DuplicateCheckResult.SelfSubmitted>(result);
        Assert.Equal(ExistingRef, self.ReferenceNumber);
    }

    [Fact]
    public async Task CheckForConflictAsync_WhenOtherSubmitted_ReturnsOtherSubmittedWithReference()
    {
        _repository.CheckForConflictAsync(WindowId, PupilId, OrganisationUrn, string.Empty, CurrentUserId)
            .Returns(new DuplicateCheckResult.OtherSubmitted(ExistingRef, "", "", ""));

        var result = await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        var other = Assert.IsType<DuplicateCheckResult.OtherSubmitted>(result);
        Assert.Equal(ExistingRef, other.ReferenceNumber);
    }

    // ── HasSubmittedRequestAsync discriminator (T009) ───────────────────────

    [Fact]
    public async Task HasSubmittedRequestAsync_PassesCurrentUserIdToRepository()
    {
        await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        await _repository.Received(1).CheckForConflictAsync(
            WindowId, PupilId, OrganisationUrn, string.Empty, CurrentUserId);
    }

    [Fact]
    public async Task HasSubmittedRequestAsync_WhenRepositoryReturnsNoConflict_ReturnsNoConflict()
    {
        _repository.CheckForConflictAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(new DuplicateCheckResult.NoConflict());

        var result = await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        Assert.IsType<DuplicateCheckResult.NoConflict>(result);
    }

    [Fact]
    public async Task HasSubmittedRequestAsync_WhenRepositoryReturnsSelfSubmitted_ReturnsSelfSubmitted()
    {
        _repository.CheckForConflictAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(new DuplicateCheckResult.SelfSubmitted("REF-001", "", "", ""));

        var result = await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        Assert.IsType<DuplicateCheckResult.SelfSubmitted>(result);
    }

    [Fact]
    public async Task HasSubmittedRequestAsync_WhenRepositoryReturnsOtherSubmitted_ReturnsOtherSubmitted()
    {
        _repository.CheckForConflictAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(new DuplicateCheckResult.OtherSubmitted("REF-002", "", "", ""));

        var result = await _sut.HasSubmittedRequestAsync(WindowId, PupilId, OrganisationUrn);

        Assert.IsType<DuplicateCheckResult.OtherSubmitted>(result);
    }

    // ── ConfirmRequestAsync discriminator ───────────────────────────────────

    [Fact]
    public async Task ConfirmRequestAsync_WhenSelfSubmittedConflict_ThrowsDuplicateRequestExceptionWithSelfSubmitted()
    {
        var journey = ValidJourney();
        _repository.CheckForConflictAsync(WindowId, journey.SelectedPupil!.Id, OrganisationUrn,
                journey.ReferenceNumber!, CurrentUserId)
            .Returns(new DuplicateCheckResult.SelfSubmitted(ExistingRef, "", "", ""));

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            _sut.ConfirmRequestAsync(WindowId, journey));

        Assert.Equal(ConflictType.SelfSubmitted, ex.ConflictType);
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenOtherSubmittedConflict_ThrowsDuplicateRequestExceptionWithOtherSubmitted()
    {
        var journey = ValidJourney();
        _repository.CheckForConflictAsync(WindowId, journey.SelectedPupil!.Id, OrganisationUrn,
                journey.ReferenceNumber!, CurrentUserId)
            .Returns(new DuplicateCheckResult.OtherSubmitted(ExistingRef, "", "", ""));

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            _sut.ConfirmRequestAsync(WindowId, journey));

        Assert.Equal(ConflictType.OtherSubmitted, ex.ConflictType);
    }

    // ── T003: Service-layer propagation of UpsertAsync-level DuplicateRequestException ──

    [Fact]
    public async Task SubmitRequestAsync_WhenUpsertThrowsDuplicateRequestException_PropagatesWithOtherSubmitted()
    {
        var journey = ValidJourney();
        var config = new QuestionFlowConfig { FirstPageId = "test", Pages = [] };

        _repository.CheckForConflictAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(new DuplicateCheckResult.NoConflict());
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns(config);
        _flowService.ResolveRequestType(config, journey).Returns("Remove");

        _repository.UpsertAsync(Arg.Any<ChangeRequestData>())
            .Returns(Task.FromException<Guid>(new DuplicateRequestException(
                ConflictType.OtherSubmitted, "Remove", "Remove", "Other User", false)));

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            _sut.SubmitRequestAsync(WindowId, journey));

        Assert.Equal(ConflictType.OtherSubmitted, ex.ConflictType);
        Assert.Equal("Remove", ex.ConflictingReasonType);
        Assert.Equal("Other User", ex.ConflictingUserName);
    }

    [Fact]
    public async Task SubmitRequestAsync_WhenUpsertThrowsDuplicateRequestExceptionWithSelfSubmitted_PropagatesSelfSubmitted()
    {
        var journey = ValidJourney();
        var config = new QuestionFlowConfig { FirstPageId = "test", Pages = [] };

        _repository.CheckForConflictAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(new DuplicateCheckResult.NoConflict());
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns(config);
        _flowService.ResolveRequestType(config, journey).Returns("Remove");

        _repository.UpsertAsync(Arg.Any<ChangeRequestData>())
            .Returns(Task.FromException<Guid>(new DuplicateRequestException(
                ConflictType.SelfSubmitted, "Remove", "Remove", CurrentUserId.ToString(), true)));

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            _sut.SubmitRequestAsync(WindowId, journey));

        Assert.Equal(ConflictType.SelfSubmitted, ex.ConflictType);
        Assert.True(ex.ReasonsMatch);
    }

    // ── DuplicateRequestMessages scenario messages ──────────────────────────

    [Fact]
    public void AttentionBannerHtml_WhenSelfAndReasonMatches_IncludesReferenceAndLink()
    {
        var html = DuplicateRequestMessages.AttentionBannerHtml(true, true, "Remove", "Jane Smith", "CYPMD_001", "/link", "");
        Assert.Contains("pupil removal request", html);
        Assert.Contains("Jane Smith", html);
        Assert.Contains("CYPMD_001", html);
        Assert.Contains("View submitted request", html);
        Assert.Contains("delete the previously submitted request", html);
    }

    [Fact]
    public void AttentionBannerHtml_WhenOtherAndReasonMatches_IncludesUserName()
    {
        var html = DuplicateRequestMessages.AttentionBannerHtml(false, true, "Remove", "Jane Smith", "CYPMD_001", "/link", "Sarah Jenkins");
        Assert.Contains("Sarah Jenkins", html);
        Assert.Contains("pupil removal request", html);
    }

    [Fact]
    public void AttentionBannerHtml_WhenSelfAndReasonDiffers_ShowsDifferentType()
    {
        var html = DuplicateRequestMessages.AttentionBannerHtml(true, false, "Remove", "Jane Smith", "CYPMD_001", "/link", "");
        Assert.Contains("request of a different type", html);
        Assert.Contains("pupil removal request", html);
    }

    [Fact]
    public void AttentionBannerHtml_WhenOtherAndReasonDiffers_CheckWithColleague()
    {
        var html = DuplicateRequestMessages.AttentionBannerHtml(false, false, "Include", "Jane Smith", "CYPMD_001", "/link", "Sarah Jenkins");
        Assert.Contains("Sarah Jenkins", html);
        Assert.Contains("request of a different type", html);
        Assert.Contains("pupil inclusion request", html);
        Assert.Contains("check with your colleague", html);
    }

    [Fact]
    public void SummaryMessage_WhenSelfSubmittedAndReasonMatches_IncludesRequestType()
    {
        var message = DuplicateRequestMessages.SummaryMessage(true, true, "Remove");
        Assert.Contains("pupil removal request", message);
    }

    [Fact]
    public void SummaryMessage_WhenOtherSubmittedAndReasonMatches_IncludesRequestType()
    {
        var message = DuplicateRequestMessages.SummaryMessage(false, true, "Remove");
        Assert.Contains("pupil removal request", message);
    }

    [Fact]
    public void ErrorSummaryMessage_IsCorrect()
    {
        Assert.Equal("A request has already been submitted for this pupil", DuplicateRequestMessages.ErrorSummaryMessage);
    }

    [Fact]
    public void FieldErrorMessage_IsCorrect()
    {
        Assert.Equal("Choose another pupil", DuplicateRequestMessages.FieldErrorMessage);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static RequestState ValidJourney()
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = new CheckingWindowDto
            {
                Id = WindowId,
                Title = "KS4 June",
                KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = DateTime.UtcNow.AddDays(-10),
                EndDate = DateTime.UtcNow.AddDays(20)
            },
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
            QuestionHistory = [],
            QuestionAnswers = new()
        };
        state.SelectedPupil = new PupilDto
        {
            Id = PupilId,
            Firstname = "Jane",
            Surname = "Smith",
            Sex = "F",
            DateOfBirth = "01/01/2010",
            Age = 16,
            Cypmd_Id = "CYPMD123",
            Upn = "123123"
        };
        return state;
    }
}
