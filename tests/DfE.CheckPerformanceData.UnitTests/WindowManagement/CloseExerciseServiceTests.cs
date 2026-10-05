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
    private const CheckingExerciseType Exercise = CheckingExerciseType.PupilData;

    private readonly IAdminRequestsRepository _repository = Substitute.For<IAdminRequestsRepository>();
    private readonly IRequestBlobClient _documents = Substitute.For<IRequestBlobClient>();
    private readonly IQueueService _queue = Substitute.For<IQueueService>();
    private readonly CloseExerciseService _sut;

    public CloseExerciseServiceTests()
    {
        _repository.MarkTicketQueuedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _sut = new CloseExerciseService(_repository, _documents, _queue);
    }

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
        _repository.GetDecidedRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns(rows);
        foreach (var row in rows)
            _documents.GetRequestAsync(WindowId, row.ReferenceNumber).Returns(Document(row.ReferenceNumber));
    }

    [Fact]
    public async Task A_decided_request_is_sent_with_its_saved_document_and_marked_queued()
    {
        var row = Row("CYPMD_KS4June_AAAAAA1");
        Decided(row);

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

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
        _repository.CountWaitingRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>()).Returns(2);

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

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

        await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.DoesNotContain(_repository.ReceivedCalls(), c => c.GetMethodInfo().Name.Contains("Status")
            && c.GetMethodInfo().Name != nameof(IAdminRequestsRepository.MarkTicketQueuedAsync));
    }

    [Fact]
    public async Task A_row_another_run_queued_first_is_not_counted()
    {
        var row = Row("CYPMD_KS4June_AAAAAA1");
        Decided(row);
        _repository.MarkTicketQueuedAsync(row.ChangeRequestId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(0, result.Enqueued);
    }

    [Fact]
    public async Task A_missing_saved_document_fails_naming_the_reference()
    {
        // The service is not live, so every submitted amendment has a saved document. A missing
        // one is a fault to see, not a case to work around.
        var row = Row("CYPMD_KS4June_MISSING");
        _repository.GetDecidedRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>())
            .Returns([row]);
        _documents.GetRequestAsync(WindowId, row.ReferenceNumber).Returns((RequestDocument?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CloseAsync(WindowId, Exercise, CancellationToken.None));

        Assert.Contains("CYPMD_KS4June_MISSING", ex.Message);
        await _repository.DidNotReceive().MarkTicketQueuedAsync(row.ChangeRequestId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_sweep_asks_only_for_the_named_window_and_exercise()
    {
        Decided();

        await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        await _repository.Received(1).GetDecidedRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>());
        await _repository.Received(1).CountWaitingRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>());
        await _repository.Received(1).MarkDraftsNotSubmittedForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drafts_for_the_exercise_are_cancelled_and_counted()
    {
        Decided();
        _repository.MarkDraftsNotSubmittedForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>()).Returns(3);

        var result = await _sut.CloseAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(3, result.DraftsCancelled);
    }

    [Fact]
    public async Task The_preview_counts_what_the_sweep_would_send_wait_on_and_cancel()
    {
        Decided(Row("A"), Row("B"));
        _repository.CountWaitingRequestsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>()).Returns(1);
        _repository.CountDraftsForExerciseAsync(WindowId, Exercise, Arg.Any<CancellationToken>()).Returns(4);

        var preview = await _sut.PreviewAsync(WindowId, Exercise, CancellationToken.None);

        Assert.Equal(2, preview.RequestsToClose);
        Assert.Equal(1, preview.RequestsWaiting);
        Assert.Equal(4, preview.DraftsToCancel);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!, default);
    }
}
