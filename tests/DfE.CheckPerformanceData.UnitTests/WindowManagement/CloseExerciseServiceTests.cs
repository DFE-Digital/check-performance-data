using DfE.CheckPerformanceData.Application.AdminRequests;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// The per-exercise close sweep (#536): send this exercise's decided amendments to Zendesk using
// the document saved at submit, mark them TicketQueued, report undecided ones as waiting, and
// cancel leftover drafts. Which rows count as decided or waiting is the repository's query, pinned
// by AdminRequestsRepositoryExerciseScopeTests; this pins what the sweep does with them.
public sealed class CloseExerciseServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    // The sweep is addressed by the exercise's row id (#466).
    private static readonly Guid ExerciseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IAdminRequestsRepository _repository = Substitute.For<IAdminRequestsRepository>();
    private readonly IRequestBlobClient _documents = Substitute.For<IRequestBlobClient>();
    private readonly IQueueService _queue = Substitute.For<IQueueService>();
    private readonly IWindowRepository _windows = Substitute.For<IWindowRepository>();
    private readonly IExerciseExtensionRepository _extensions = Substitute.For<IExerciseExtensionRepository>();
    private readonly CloseExerciseService _sut;
    private readonly FixedTimeProvider _clock = new(Now);

    // AB#307773: the exercise ended yesterday. Extension A is still open, B has closed but has not
    // been swept, and C has closed and been swept.
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Yesterday = new(2026, 11, 14, 23, 59, 59);
    private static readonly DateTime Tomorrow = new(2026, 11, 16, 23, 59, 59);

    private static readonly CheckingExerciseExtensionDto A = new()
    {
        Id = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000001"), EndDate = Tomorrow, Laestabs = ["9334290"]
    };

    private static readonly CheckingExerciseExtensionDto B = new()
    {
        Id = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000002"), EndDate = Yesterday, Laestabs = ["8604070"]
    };

    private static readonly CheckingExerciseExtensionDto C = new()
    {
        Id = Guid.Parse("CCCCCCCC-0000-0000-0000-000000000003"), EndDate = Yesterday, Laestabs = ["1111111"],
        RequestsSentAt = new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc)
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        // Settable so a fact can move the clock from inside a stubbed sweep.
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public CloseExerciseServiceTests()
    {
        _repository.MarkTicketQueuedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<Task>>()());
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2026, 11, 1),
            EndDate = Tomorrow,
            Exercises =
            [
                new CheckingExerciseDto
                {
                    Id = ExerciseId,
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2026, 11, 1),
                    EndDate = Yesterday,
                    Extensions = [A, B, C]
                }
            ]
        });
        _sut = new CloseExerciseService(
            _repository, _documents, _queue, _windows, new CheckingExerciseService(_clock), _extensions, _clock);
    }

    // Compares the filter's schools as a set; the order they were named in does not matter.
    private static bool Is(SchoolFilter schools, SchoolFilterMode mode, params string[] laestabs) =>
        schools.Mode == mode && schools.Laestabs.Order().SequenceEqual(laestabs.Order());

    private static ReplayRequestRow Row(string reference) => new()
    {
        ChangeRequestId = Guid.NewGuid(), WindowId = WindowId, ReferenceNumber = reference
    };

    private static RequestDocument Document(string reference) => new()
    {
        ChangeRequestId = Guid.NewGuid(),
        ReferenceNumber = reference,
        SubmittedBy = new UserDetails { UserId = "u1", DisplayName = "Ada Editor" },
        CheckingWindowId = WindowId,
        CheckingWindowType = "KS4June",
        RequestTypeCode = "Add",
        School = new SchoolDetails { Urn = "142313", Name = "Kingsmead School" },
        Pupil = new PupilDetails
        {
            Id = "p1", CypmdId = "c1", Firstname = "Alice", Surname = "Newpupil",
            DateOfBirth = "01/09/2010", Sex = "F", Age = 15, Upn = "A123456789012"
        },
        Answers = []
    };

    private void Decided(params ReplayRequestRow[] rows)
    {
        _repository.GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>())
            .Returns(rows);
        foreach (var row in rows)
            _documents.GetRequestAsync(WindowId, row.ReferenceNumber).Returns(Document(row.ReferenceNumber));
    }

    [Fact]
    public async Task A_decided_request_is_sent_with_its_saved_document_and_marked_queued()
    {
        var row = Row("CYPMD_KS4June_AAAAAA1");
        Decided(row);

        var result = await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
        await _queue.Received(1).EnqueueAsync(
            QueueOptions.ZendeskQueue,
            Arg.Is<RequestDocument>(d => d.ReferenceNumber == row.ReferenceNumber && d.School.Name == "Kingsmead School"),
            Arg.Any<CancellationToken>());
        await _repository.Received(1).MarkTicketQueuedAsync(row.ChangeRequestId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Waiting_requests_are_counted_and_left_alone()
    {
        Decided();
        _repository.CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>()).Returns(2);

        var result = await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(0, result.Enqueued);
        Assert.Equal(2, result.Waiting);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!, default);
    }

    [Fact]
    public async Task The_sweep_never_changes_the_schools_status()
    {
        // Status says what the school did. The sweep writes only ProcessingStatus (and cancels
        // drafts), so a submitted request stays Submitted.
        Decided(Row("CYPMD_KS4June_AAAAAA1"));

        await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.DoesNotContain(_repository.ReceivedCalls(), c => c.GetMethodInfo().Name.Contains("Status")
            && c.GetMethodInfo().Name != nameof(IAdminRequestsRepository.MarkTicketQueuedAsync));
    }

    [Fact]
    public async Task A_row_another_run_queued_first_is_not_counted()
    {
        var row = Row("CYPMD_KS4June_AAAAAA1");
        Decided(row);
        _repository.MarkTicketQueuedAsync(row.ChangeRequestId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(0, result.Enqueued);
        // The other run sent its message; a second one would only fail its claim.
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!, default);
    }

    [Fact]
    public async Task The_row_is_marked_queued_before_its_message_is_sent_in_one_transaction()
    {
        // The ticket maker claims only TicketQueued rows. If the message were on the queue first,
        // it could be taken while the row still said Decided, fail its claim and be retried.
        var row = Row("CYPMD_KS4June_AAAAAA1");
        Decided(row);
        var insideTransaction = false;
        var steps = new List<string>();
        _repository.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(async c =>
            {
                insideTransaction = true;
                await c.Arg<Func<Task>>()();
                insideTransaction = false;
            });
        _repository.MarkTicketQueuedAsync(row.ChangeRequestId, Arg.Any<CancellationToken>())
            .Returns(_ => { steps.Add($"mark:{insideTransaction}"); return true; });
        _queue.EnqueueAsync(QueueOptions.ZendeskQueue, Arg.Any<RequestDocument>(), Arg.Any<CancellationToken>())
            .Returns(_ => { steps.Add($"enqueue:{insideTransaction}"); return Guid.NewGuid(); });

        await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(["mark:True", "enqueue:True"], steps);
    }

    [Fact]
    public async Task A_failed_send_fails_the_sweep()
    {
        // The transaction rolls the mark back, so the row stays Decided for the next run.
        Decided(Row("CYPMD_KS4June_AAAAAA1"));
        _queue.EnqueueAsync(QueueOptions.ZendeskQueue, Arg.Any<RequestDocument>(), Arg.Any<CancellationToken>())
            .Returns<Guid>(_ => throw new InvalidOperationException("queue down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_saved_document_fails_naming_the_reference()
    {
        // The service is not live, so every submitted amendment has a saved document. A missing
        // one is a fault to see, not a case to work around.
        var row = Row("CYPMD_KS4June_MISSING");
        _repository.GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>())
            .Returns([row]);
        _documents.GetRequestAsync(WindowId, row.ReferenceNumber).Returns((RequestDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None));

        Assert.Contains("CYPMD_KS4June_MISSING", ex.Message);
        await _repository.DidNotReceive().MarkTicketQueuedAsync(row.ChangeRequestId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_sweep_asks_only_for_the_named_window_and_exercise()
    {
        Decided();

        await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        await _repository.Received(1).GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drafts_for_the_exercise_are_cancelled_and_counted()
    {
        Decided();
        _repository.MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>()).Returns(3);

        var result = await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(3, result.DraftsCancelled);
    }

    [Fact]
    public async Task The_preview_counts_what_the_sweep_would_send_wait_on_and_cancel()
    {
        Decided(Row("A"), Row("B"));
        _repository.CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>()).Returns(1);
        _repository.CountDraftsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>()).Returns(4);

        var preview = await _sut.PreviewAsync(WindowId, ExerciseId, CancellationToken.None);

        Assert.Equal(2, preview.RequestsToClose);
        Assert.Equal(1, preview.RequestsWaiting);
        Assert.Equal(4, preview.DraftsToCancel);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!, default);
    }

    [Fact]
    public async Task The_exercise_sweep_leaves_out_only_schools_in_open_extensions_and_stamps_the_closed_ones()
    {
        // Review finding 4 (15 Oct): A is open, so its school is held back. B has closed but was
        // never swept (its hand-over may have failed every tick): the exercise's sweep takes its
        // school and stamps B, so the summary says its requests went. C was swept already and is
        // left alone. Sending twice is impossible: MarkTicketQueuedAsync guards every row.
        Decided();
        var ct = CancellationToken.None;

        await _sut.CloseAsync(WindowId, ExerciseId, ct);
        await _sut.PreviewAsync(WindowId, ExerciseId, ct);

        await _repository.Received(2).GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Exclude, "9334290")), ct);
        await _repository.Received(2).CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Exclude, "9334290")), ct);
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Exclude, "9334290")), ct);
        await _extensions.Received(1).MarkRequestsSentAsync(B.Id, Now.UtcDateTime, ct);
        await _extensions.DidNotReceive().MarkRequestsSentAsync(A.Id, Arg.Any<DateTime>(), ct);
        await _extensions.DidNotReceive().MarkRequestsSentAsync(C.Id, Arg.Any<DateTime>(), ct);
    }

    [Fact]
    public async Task An_extension_held_back_by_the_filter_is_not_stamped_even_if_it_closes_during_the_sweep()
    {
        // Final review F1: the held set and the stamp set come from one reading of the clock, taken
        // before the sweep. A ends (tomorrow 23:59:59) while the sweep runs; its school was left out
        // of the sweep, so stamping it "Requests sent" would be wrong. B, closed at the start, is
        // still stamped.
        Decided();
        _repository.CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId, Arg.Any<SchoolFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _clock.Now = new DateTimeOffset(Tomorrow.AddHours(1), TimeSpan.Zero);
                return 0;
            });

        await _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None);

        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Exclude, "9334290")), Arg.Any<CancellationToken>());
        await _extensions.DidNotReceive().MarkRequestsSentAsync(A.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _extensions.Received(1).MarkRequestsSentAsync(B.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_exercise_sweep_stamps_no_extension()
    {
        Decided(Row("CYPMD_1"));
        _queue.EnqueueAsync(QueueOptions.ZendeskQueue, Arg.Any<RequestDocument>(), Arg.Any<CancellationToken>())
            .Returns<Guid>(_ => throw new InvalidOperationException("queue down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CloseAsync(WindowId, ExerciseId, CancellationToken.None));

        await _extensions.DidNotReceiveWithAnyArgs().MarkRequestsSentAsync(default, default, default);
    }

    [Fact]
    public async Task The_preview_stamps_nothing()
    {
        Decided();

        await _sut.PreviewAsync(WindowId, ExerciseId, CancellationToken.None);

        await _extensions.DidNotReceiveWithAnyArgs().MarkRequestsSentAsync(default, default, default);
    }

    [Fact]
    public async Task An_extension_closed_early_keeps_its_schools_held_while_the_exercise_runs()
    {
        // AB#307773 review C1: D's own end has passed and it was even swept once, but the exercise
        // runs until tomorrow, so its school is still inside the exercise's dates. The held set is
        // judged on the effective end, max(exercise end, extension end), so D's school stays out.
        var d = new CheckingExerciseExtensionDto
        {
            Id = Guid.Parse("DDDDDDDD-0000-0000-0000-000000000004"), EndDate = Yesterday, Laestabs = ["2222222"],
            RequestsSentAt = new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc)
        };
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2026, 11, 1),
            EndDate = Tomorrow,
            Exercises =
            [
                new CheckingExerciseDto
                {
                    Id = ExerciseId,
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2026, 11, 1),
                    EndDate = Tomorrow,
                    Extensions = [d]
                }
            ]
        });
        Decided();
        var ct = CancellationToken.None;

        await _sut.CloseAsync(WindowId, ExerciseId, ct);

        await _repository.Received(1).GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Exclude, "2222222")), ct);
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Exclude, "2222222")), ct);
    }

    [Fact]
    public async Task The_extension_sweep_takes_only_that_extensions_schools_and_stamps_it_sent_once()
    {
        Decided();
        var ct = CancellationToken.None;

        await _sut.CloseExtensionAsync(WindowId, ExerciseId, A.Id, ct);

        await _repository.Received(1).GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "9334290")), ct);
        await _repository.Received(1).CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "9334290")), ct);
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "9334290")), ct);
        await _extensions.Received(1).MarkRequestsSentAsync(A.Id, Now.UtcDateTime, ct);
    }

    [Fact]
    public async Task A_failed_extension_sweep_leaves_it_unsent()
    {
        // Unsent keeps the school out of the exercise's sweep, so neither sweep can take its
        // requests before the extension's own sweep has succeeded.
        Decided(Row("CYPMD_KS4June_AAAAAA1"));
        _queue.EnqueueAsync(QueueOptions.ZendeskQueue, Arg.Any<RequestDocument>(), Arg.Any<CancellationToken>())
            .Returns<Guid>(_ => throw new InvalidOperationException("queue down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CloseExtensionAsync(WindowId, ExerciseId, B.Id, CancellationToken.None));

        await _extensions.DidNotReceiveWithAnyArgs().MarkRequestsSentAsync(default, default, default);
    }

    [Fact]
    public async Task An_unknown_extension_throws()
    {
        var ct = CancellationToken.None;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CloseExtensionAsync(WindowId, ExerciseId, Guid.NewGuid(), ct));

        await _repository.DidNotReceiveWithAnyArgs().GetDecidedRequestsForExerciseAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Preview_uses_the_same_filter_as_the_sweep()
    {
        Decided();
        var ct = CancellationToken.None;

        await _sut.PreviewExtensionAsync(WindowId, ExerciseId, A.Id, ct);

        await _repository.Received(1).GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "9334290")), ct);
        await _repository.Received(1).CountWaitingRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "9334290")), ct);
        await _repository.Received(1).CountDraftsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "9334290")), ct);
        await _extensions.DidNotReceiveWithAnyArgs().MarkRequestsSentAsync(default, default, default);
    }

    [Fact]
    public async Task Sweep_schools_takes_only_the_named_schools_and_stamps_nothing()
    {
        // Review finding 2: the extension edit sweeps a school it removed after the exercise closed.
        Decided();
        var ct = CancellationToken.None;

        await _sut.SweepSchoolsAsync(WindowId, ExerciseId, ["8604070", "1111111"], ct);

        await _repository.Received(1).GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "8604070", "1111111")), ct);
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, ExerciseId,
            Arg.Is<SchoolFilter>(f => Is(f, SchoolFilterMode.Only, "8604070", "1111111")), ct);
        await _extensions.DidNotReceiveWithAnyArgs().MarkRequestsSentAsync(default, default, default);
    }
}
