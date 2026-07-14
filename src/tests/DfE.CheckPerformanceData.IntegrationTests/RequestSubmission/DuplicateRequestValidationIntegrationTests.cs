using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class DuplicateRequestValidationIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public DuplicateRequestValidationIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // ── T008: CheckForConflictAsync against PostgreSQL ──────────────────────

    [Fact]
    public async Task CheckForConflictAsync_WhenNoConflictExists_ReturnsNoConflict()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 100000, "REF-NO-CONFLICT", Guid.NewGuid());

        Assert.IsType<DuplicateCheckResult.NoConflict>(result);
    }

    [Fact]
    public async Task CheckForConflictAsync_WhenSelfSubmitted_ReturnsSelfSubmittedWithReference()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        await SeedRequestAsync(windowId, "REF-SELF-1", pupilId, currentUserId);

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 100000, "REF-SELF-OTHER", currentUserId);

        var self = Assert.IsType<DuplicateCheckResult.SelfSubmitted>(result);
        Assert.Equal("REF-SELF-1", self.ReferenceNumber);
    }

    [Fact]
    public async Task CheckForConflictAsync_WhenOtherSubmitted_ReturnsOtherSubmittedWithReference()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var submitterUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        await SeedRequestAsync(windowId, "REF-OTHER-1", pupilId, submitterUserId);

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 100000, "REF-OTHER-CURRENT", currentUserId);

        var other = Assert.IsType<DuplicateCheckResult.OtherSubmitted>(result);
        Assert.Equal("REF-OTHER-1", other.ReferenceNumber);
    }

    [Fact]
    public async Task CheckForConflictAsync_ReturnsNoConflict_WhenCurrentReferenceMatches()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        await SeedRequestAsync(windowId, "REF-SELF-EXCLUDE", pupilId, currentUserId);

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 100000, "REF-SELF-EXCLUDE", currentUserId);

        Assert.IsType<DuplicateCheckResult.NoConflict>(result);
    }

    [Fact]
    public async Task CheckForConflictAsync_ReturnsNoConflict_WhenOnlyInProgressRequestExists()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        await SeedRequestAsync(windowId, "REF-INPROG", pupilId, Guid.NewGuid(), RequestStatus.InProgress);

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 100000, "REF-OTHER", Guid.NewGuid());

        Assert.IsType<DuplicateCheckResult.NoConflict>(result);
    }

    [Fact]
    public async Task CheckForConflictAsync_ReturnsNoConflict_WhenOnlyWithdrawnRequestExists()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        await SeedRequestAsync(windowId, "REF-WD", pupilId, Guid.NewGuid(), RequestStatus.Withdrawn);

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 100000, "REF-OTHER", Guid.NewGuid());

        Assert.IsType<DuplicateCheckResult.NoConflict>(result);
    }

    [Fact]
    public async Task CheckForConflictAsync_ScopesByOrganisation()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        // Request for org 100000
        await SeedRequestAsync(windowId, "REF-ORG-1", pupilId, currentUserId, organisationUrn: 100000);
        // Request for org 999999 with same pupil
        await SeedRequestAsync(windowId, "REF-ORG-2", pupilId, currentUserId, organisationUrn: 999999);

        var result = await new RequestRepository(_fixture.CreateContext())
            .CheckForConflictAsync(windowId, pupilId, 999999, "REF-ORG-CURRENT", currentUserId);

        var self = Assert.IsType<DuplicateCheckResult.SelfSubmitted>(result);
        Assert.Equal("REF-ORG-2", self.ReferenceNumber);
    }

    // ── T010: Full pupil-selection duplicate check flow ─────────────────────

    [Fact]
    public async Task HasSubmittedRequestAsync_WhenSelfSubmittedViaFullFlow_ReturnsSelfSubmitted()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();

        var service = CreateService(currentUserId);
        await SeedRequestAsync(windowId, "REF-FLOW-1", pupilId, currentUserId);

        var result = await service.HasSubmittedRequestAsync(windowId, pupilId, 100000);

        Assert.IsType<DuplicateCheckResult.SelfSubmitted>(result);
    }

    [Fact]
    public async Task HasSubmittedRequestAsync_WhenOtherSubmittedViaFullFlow_ReturnsOtherSubmitted()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var pupilId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();

        var service = CreateService(currentUserId);
        await SeedRequestAsync(windowId, "REF-FLOW-2", pupilId, otherUserId);

        var result = await service.HasSubmittedRequestAsync(windowId, pupilId, 100000);

        Assert.IsType<DuplicateCheckResult.OtherSubmitted>(result);
    }

    // ── T011: Final-submission duplicate check via ConfirmRequestAsync ──────

    [Fact]
    public async Task ConfirmRequestAsync_WhenSelfSubmittedConflict_ThrowsDuplicateRequestExceptionWithSelfSubmitted()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var currentUserId = Guid.NewGuid();
        var journey = ValidJourney(windowId);
        await SeedRequestAsync(windowId, "REF-CONFIRM-SELF", journey.SelectedPupil!.Id, currentUserId);

        var service = CreateService(currentUserId);

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            service.ConfirmRequestAsync(windowId, journey));

        Assert.Equal(ConflictType.SelfSubmitted, ex.ConflictType);
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenOtherSubmittedConflict_ThrowsDuplicateRequestExceptionWithOtherSubmitted()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var otherUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var journey = ValidJourney(windowId);
        await SeedRequestAsync(windowId, "REF-CONFIRM-OTHER", journey.SelectedPupil!.Id, otherUserId);

        var service = CreateService(currentUserId);

        var ex = await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            service.ConfirmRequestAsync(windowId, journey));

        Assert.Equal(ConflictType.OtherSubmitted, ex.ConflictType);
    }

    [Fact]
    public async Task ConfirmRequestAsync_WhenNoConflict_DoesNotThrow()
    {
        await TruncateAsync();
        var windowId = await SeedWindowAsync();
        var currentUserId = Guid.NewGuid();
        var journey = ValidJourney(windowId);

        var flowService = Substitute.For<IQuestionFlowService>();
        flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns((QuestionFlowConfig?)null);

        var service = CreateService(currentUserId, flowService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmRequestAsync(windowId, journey));
    }

    // ── T018: Guidance text ─────────────────────────────────────────────────

    [Fact]
    public void SelfSubmittedMessages_ContainGuidanceText()
    {
        var pupilSelection = $"{DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.SelfSubmittedPupilSelection} {DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.SelfSubmittedGuidance}";
        var summary = $"{DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.SelfSubmittedSummary} {DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.SelfSubmittedGuidance}";

        Assert.Contains("view your existing request", pupilSelection);
        Assert.Contains("view your existing request", summary);
    }

    [Fact]
    public void OtherSubmittedMessages_ContainGuidanceText()
    {
        var pupilSelection = $"{DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.OtherSubmittedPupilSelection} {DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.OtherSubmittedGuidance}";
        var summary = $"{DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.OtherSubmittedSummary} {DfE.CheckPerformanceData.Web.Common.DuplicateRequestMessages.OtherSubmittedGuidance}";

        Assert.Contains("coordinate with colleagues", pupilSelection);
        Assert.Contains("coordinate with colleagues", summary);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private RequestService CreateService(Guid userId)
    {
        return CreateService(userId, Substitute.For<IQuestionFlowService>());
    }

    private RequestService CreateService(Guid userId, IQuestionFlowService flowService)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId.ToString());
        currentUser.DisplayName.Returns("Test User");
        currentUser.Email.Returns("test@education.gov.uk");
        currentUser.OrganisationUrn.Returns("100000");
        currentUser.OrganisationName.Returns("Test School");

        var repository = new RequestRepository(_fixture.CreateContext());
        var requestStateBlobClient = Substitute.For<IRequestStateBlobClient>();
        var logger = Substitute.For<ILogger<RequestService>>();
        var queueService = Substitute.For<IQueueService>();
        var requestNotificationService = Substitute.For<IRequestNotificationService>();
        var checkYourPupilDataService = Substitute.For<ICheckYourPupilDataService>();

        return new RequestService(flowService, requestStateBlobClient, repository, currentUser,
            logger, queueService, requestNotificationService, checkYourPupilDataService);
    }

    private async Task SeedRequestAsync(
        Guid windowId, string referenceNumber, Guid pupilId, Guid submittedById,
        RequestStatus status = RequestStatus.SubmittedUnCommitted, long organisationUrn = 100000)
    {
        await new RequestRepository(_fixture.CreateContext())
            .UpsertAsync(new ChangeRequestData
            {
                WindowId = windowId,
                ReferenceNumber = referenceNumber,
                OrganisationUrn = organisationUrn,
                PupilId = pupilId,
                PupilUpn = "UPN1",
                PupilFirstname = "Jane",
                PupilSurname = "Smith",
                Timestamp = DateTime.UtcNow,
                SubmittedById = submittedById,
                SubmittedByName = "Test User",
                Status = status,
                RequestType = RequestType.Amendment,
                RequestTypeDescription = "Remove"
            });
    }

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedWindowAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }

    private static RequestState ValidJourney(Guid windowId)
    {
        var state = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Remove,
            CheckingWindow = new CheckingWindowDto
            {
                Id = windowId,
                Title = "KS4 June",
                KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = DateTime.UtcNow.AddDays(-10),
                EndDate = DateTime.UtcNow.AddDays(20)
            },
            ReferenceNumber = "CYPMD_KS4June_INTEGRATION_TEST",
            QuestionHistory = [],
            QuestionAnswers = new()
        };
        state.SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
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
