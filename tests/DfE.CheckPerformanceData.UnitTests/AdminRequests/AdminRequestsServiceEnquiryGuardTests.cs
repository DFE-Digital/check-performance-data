using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;
using IWindowService = DfE.CheckPerformanceData.Application.WindowManagement.IWindowService;

namespace DfE.CheckPerformanceData.Application.UnitTests.AdminRequests;

// AB#296648/AB#297848/AB#298704: the window-close Zendesk replay must skip results enquiries —
// every kind.
//
// This path rebuilds a PUPIL AMENDMENT ticket from a journey blob. An enquiry's QAN, session, current
// and revised grade have no place in that shape, so replaying one would create a malformed ticket —
// and worse, would flip the row from SubmittedUnCommitted to SubmittedCommitted, so the real
// enquiry-to-Zendesk dispatch (a separate story) could never find it again.
//
// The enquiry cases are Theories over every enquiry kind rather than one hardcoded member: a new
// results-enquiry journey that forgets this guard is exactly the regression AB#297848 shipped with.
// EnquiryKinds below derives from WhatToChangeCheckingExerciseMap, so a new kind is covered here
// the moment it maps to the ResultsEnquiry exercise — nothing to add by hand (AB#298704 arrived
// this way).
public sealed class AdminRequestsServiceEnquiryGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private static readonly Guid AmendmentRowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EnquiryRowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IAdminRequestsRepository _repository = Substitute.For<IAdminRequestsRepository>();
    private readonly IRequestStateBlobClient _stateBlob = Substitute.For<IRequestStateBlobClient>();
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly AdminRequestsService _sut;

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "page-1",
        Pages = [new JourneyPage { Id = "page-1" }]
    };

    public AdminRequestsServiceEnquiryGuardTests()
    {
        _flowService.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Flow);
        _flowService.ResolveRequestType(Arg.Any<QuestionFlowConfig>(), Arg.Any<RequestState>()).Returns("Remove");

        _sut = new AdminRequestsService(
            _repository, _stateBlob, _flowService, _queueService, Substitute.For<IWindowService>(),
            new FakeTimeProvider(Now));
    }

    private static ReplayRequestRow Row(Guid id, string reference) => new()
    {
        ChangeRequestId = id,
        WindowId = WindowId,
        ReferenceNumber = reference,
        OrganisationUrn = 142313,
        SubmittedById = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        SubmittedByName = "Ada Editor"
    };

    private static PupilDto Pupil() => new()
    {
        Id = Guid.NewGuid(), Firstname = "Billy", Surname = "B", Sex = "M",
        DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = "500001", Identifier = "9900000001"
    };

    private static CheckingWindowDto Window => new()
    {
        Title = "16 to 19 2026", KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
    };

    /// <summary>
    /// Every WhatToChange that maps to the ResultsEnquiry checking exercise. Sourced from the map
    /// rather than listed by hand, so a new enquiry journey is covered by these tests the moment it
    /// is added to WhatToChangeCheckingExerciseMap — it cannot be forgotten here the way the guard
    /// itself was. Fully qualified: the test assembly has its own WindowManagement namespace.
    /// </summary>
    public static TheoryData<WhatToChange> EnquiryKinds =>
        new(Enum.GetValues<WhatToChange>()
            .Where(c => global::DfE.CheckPerformanceData.Application.WindowManagement
                .WhatToChangeCheckingExerciseMap.CheckingExerciseFor(c)
                == CheckingExerciseType.ResultsEnquiry));

    private static RequestState Journey(WhatToChange change) => new()
    {
        SelectedWhatToChange = change,
        CheckingWindow = Window,
        SelectedPupil = Pupil(),
        SelectedResult = change == WhatToChange.IncorrectGrade
            ? new StudentResultRecord { Qan = "60180882", Grade = "9", Session = "S2024" }
            : null,
        // A missing-qualification enquiry's subject is a QualList entry, not a held result.
        SelectedQualification = change == WhatToChange.MissingQualification
            ? new QualificationReference
            {
                Qan = "60146084",
                QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
                AwardingOrganisation = "AQA"
            }
            : null,
        QuestionAnswers = [],
        QuestionHistory = ["page-1"]
    };

    private void Seed(params (ReplayRequestRow Row, WhatToChange Change)[] rows)
    {
        _repository.GetRequestsForOpenWindowsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(rows.Select(r => r.Row).ToList());
        foreach (var (row, change) in rows)
            _stateBlob.GetAsync(WindowId, row.ReferenceNumber).Returns(Journey(change));
    }

    [Fact]
    public async Task An_amendment_is_replayed()
    {
        Seed((Row(AmendmentRowId, "CYPMD_Post16_AAAAAA1"), WhatToChange.Remove));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(1, count);
        await _queueService.Received(1).EnqueueAsync(
            QueueOptions.ZendeskQueue, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(EnquiryKinds))]
    public async Task A_results_enquiry_is_not_replayed(WhatToChange enquiry)
    {
        Seed((Row(EnquiryRowId, "CYPMD_16to19_RE_BBBBBB2"), enquiry));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(0, count);
        await _queueService.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default(object)!, default);
    }

    [Theory]
    [MemberData(nameof(EnquiryKinds))]
    public async Task A_results_enquiry_row_keeps_its_uncommitted_status(WhatToChange enquiry)
    {
        // The important half: committing it would hide the enquiry from the dispatch that is
        // supposed to send it.
        Seed((Row(EnquiryRowId, "CYPMD_16to19_RE_BBBBBB2"), enquiry));

        await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        await _repository.DidNotReceive().SetStatusAsync(
            EnquiryRowId, Arg.Any<RequestStatus>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(EnquiryKinds))]
    public async Task A_mixed_batch_replays_only_the_amendment(WhatToChange enquiry)
    {
        Seed(
            (Row(AmendmentRowId, "CYPMD_Post16_AAAAAA1"), WhatToChange.Remove),
            (Row(EnquiryRowId, "CYPMD_16to19_RE_BBBBBB2"), enquiry));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(1, count);
        await _repository.Received(1).SetStatusAsync(
            AmendmentRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SetStatusAsync(
            EnquiryRowId, Arg.Any<RequestStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_enquiry_does_not_stop_a_later_amendment_being_replayed()
    {
        // Ordering matters: a `continue` skips one row, but a `return`/`break` would silently drop
        // every amendment queued behind an enquiry.
        Seed(
            (Row(EnquiryRowId, "CYPMD_16to19_RE_BBBBBB2"), WhatToChange.IncorrectGrade),
            (Row(AmendmentRowId, "CYPMD_Post16_AAAAAA1"), WhatToChange.Remove));

        var count = await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(1, count);
        await _repository.Received(1).SetStatusAsync(
            AmendmentRowId, RequestStatus.SubmittedCommitted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drafts_are_still_marked_not_submitted()
    {
        // The guard must not short-circuit the rest of the close-window work.
        Seed((Row(EnquiryRowId, "CYPMD_16to19_RE_BBBBBB2"), WhatToChange.IncorrectGrade));

        await _sut.ProcessCloseWindowEvent(CancellationToken.None);

        await _repository.Received(1).MarkDraftsNotSubmittedForOpenWindowsAsync(
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
