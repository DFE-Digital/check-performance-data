using System.Text.Json;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
// Two unrelated classes are named CheckingWindowDto; a journey's RequestState holds the LandingPage one.
using JourneyWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// AB#302158: the automatic hand-over against a real Postgres — the real window read, the real
// sweep over real request rows, the real queue table and the real audit row. Only the journey
// blob and the flow file are stand-ins (the sweep's own tests cover those).
//
// The design keeps no record that a hand-over ran; it relies on a second run finding nothing.
// The first fact is that claim, proven.
[Collection(nameof(PostgresCollection))]
public sealed class AutomaticExerciseHandOverTests(PostgresFixture fixture) : IAsyncLifetime
{
    // 18:00 UTC is 19:00 on this test's local wall clock: exactly two hours after the 17:00 end.
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0);
    private static readonly DateTime PupilDataEnd = new(2026, 11, 2, 17, 0, 0);
    private static readonly DateTime EnquiryEnd = new(2027, 3, 31, 17, 0, 0);

    private static readonly QuestionFlowConfig Flow = new()
    {
        FirstPageId = "page-1",
        Pages = [new JourneyPage { Id = "page-1" }]
    };

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _pupilDataId = Guid.NewGuid();
    private readonly Guid _enquiryId = Guid.NewGuid();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    private string SubmittedRef => $"AUTO_S_{_suffix}";
    private string DraftRef => $"AUTO_D_{_suffix}";
    private string EnquiryDraftRef => $"AUTO_E_{_suffix}";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    }

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();

        var window = new CheckingWindow
        {
            Id = _windowId,
            Title = "Automatic hand-over",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = Start,
            EndDate = EnquiryEnd
        };
        window.CheckingExercises.Add(new CheckingExercise
        {
            Id = _pupilDataId, ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Start, EndDate = PupilDataEnd, SortOrder = 0
        });
        window.CheckingExercises.Add(new CheckingExercise
        {
            Id = _enquiryId, ExerciseType = CheckingExerciseType.ResultsEnquiry,
            StartDate = Start, EndDate = EnquiryEnd, SortOrder = 1
        });
        ctx.CheckingWindows.Add(window);

        ctx.ChangeRequests.AddRange(
            Request(_pupilDataId, SubmittedRef, RequestStatus.SubmittedUnCommitted),
            Request(_pupilDataId, DraftRef, RequestStatus.InProgress),
            // Belongs to the exercise that is still open: this run must leave it alone.
            Request(_enquiryId, EnquiryDraftRef, RequestStatus.InProgress));

        await ctx.SaveChangesAsync();
    }

    // The audit rows stay (the table refuses DELETE); every assertion is keyed on this test's own
    // window id and references.
    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.ChangeRequests.Where(r => r.WindowId == _windowId).ExecuteDeleteAsync();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    private ChangeRequest Request(Guid exerciseId, string reference, RequestStatus status) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = _windowId,
        CheckingExerciseId = exerciseId,
        OrganisationUrn = 142313,
        Submitted = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Unspecified),
        SubmittedById = Guid.Parse("99999999-9999-9999-9999-999999999999"),
        SubmittedByName = "Ada Editor",
        Status = status,
        ReferenceNumber = reference,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove",
        AmendmentType = WhatToChange.Remove
    };

    private RequestState Journey() => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
        CheckingWindow = new JourneyWindowDto
        {
            Id = _windowId,
            Title = "Automatic hand-over",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = Start,
            EndDate = EnquiryEnd
        },
        SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(), Firstname = "Alice", Surname = "Smith", Sex = "F",
            DateOfBirth = "01/09/2010", Age = 15, Cypmd_Id = "", Identifier = "A86040700001B"
        },
        QuestionAnswers = [],
        QuestionHistory = ["page-1"]
    };

    private AutomaticExerciseHandOver Sut(DateTimeOffset? nowUtc = null)
    {
        var ctx = fixture.CreateContext();

        var blob = Substitute.For<IRequestStateBlobClient>();
        blob.GetAsync(_windowId, SubmittedRef).Returns(Journey());
        var flows = Substitute.For<IQuestionFlowService>();
        flows.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Flow);

        var sweep = new CloseExerciseService(
            new AdminRequestsRepository(ctx), blob, flows, new PostgresQueueService(ctx));

        return new AutomaticExerciseHandOver(
            new WindowRepository(ctx),
            sweep,
            new WindowAdminAuditWriter(ctx),
            new FixedTimeProvider(nowUtc ?? NowUtc),
            Options.Create(new ExerciseHandOverSettings()),
            NullLogger<AutomaticExerciseHandOver>.Instance);
    }

    private async Task<RequestStatus> StatusOfAsync(string reference)
    {
        await using var ctx = fixture.CreateContext();
        return await ctx.ChangeRequests.AsNoTracking()
            .Where(r => r.ReferenceNumber == reference).Select(r => r.Status).SingleAsync();
    }

    [Fact]
    public async Task A_due_exercise_is_handed_over_and_audited_once_and_a_second_run_does_nothing()
    {
        // The shared database holds other tests' windows; only this one's matter here.
        var due = Assert.Single(
            await Sut().FindDueAsync(CancellationToken.None), d => d.WindowId == _windowId);
        Assert.Equal(CheckingExerciseType.PupilData, due.Exercise);
        Assert.Equal(PupilDataEnd, due.ExerciseEnd);

        var first = await Sut().HandOverAsync(due, CancellationToken.None);
        var second = await Sut().HandOverAsync(due, CancellationToken.None);

        Assert.Equal(new AutomaticHandOverOutcome(_windowId, CheckingExerciseType.PupilData, 1, 1, Failed: false), first);
        Assert.Equal(new AutomaticHandOverOutcome(_windowId, CheckingExerciseType.PupilData, 0, 0, Failed: false), second);

        Assert.Equal(RequestStatus.SubmittedCommitted, await StatusOfAsync(SubmittedRef));
        Assert.Equal(RequestStatus.NotSubmitted, await StatusOfAsync(DraftRef));
        Assert.Equal(RequestStatus.InProgress, await StatusOfAsync(EnquiryDraftRef));

        await using var ctx = fixture.CreateContext();

        // One message, not two: the second run had nothing left to send.
        var submittedRef = SubmittedRef;
        Assert.Equal(1, await ctx.QueueMessages.CountAsync(
            m => m.QueueName == QueueOptions.ZendeskQueue && m.Payload.Contains(submittedRef)));

        // One audit row, not two: a run that did nothing writes none.
        var windowText = _windowId.ToString();
        var audit = Assert.Single(await ctx.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == AuditActivities.WindowAdmin && a.EntityId == windowText)
            .ToListAsync());
        Assert.Equal(AuditActivities.RequestsSentAutomaticallyAction, audit.Action);
        Assert.Null(audit.UserId);
        Assert.Equal(NowUtc.UtcDateTime, audit.Timestamp);

        using var payload = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal("PupilData", payload.RootElement.GetProperty("exerciseType").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("requestsSent").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("draftsCancelled").GetInt32());
    }

    [Fact]
    public async Task The_exercise_is_not_due_a_second_early_nor_once_the_catch_up_day_has_passed()
    {
        var oneSecondEarly = await Sut(NowUtc.AddSeconds(-1)).FindDueAsync(CancellationToken.None);
        var aDayLater = await Sut(NowUtc.AddHours(24)).FindDueAsync(CancellationToken.None);

        Assert.DoesNotContain(oneSecondEarly, d => d.WindowId == _windowId);
        Assert.DoesNotContain(aDayLater, d => d.WindowId == _windowId);
    }
}
