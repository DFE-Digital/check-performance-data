using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#302158: which exercises the service hands over by itself, and what one hand-over does.
// The local zone is one hour ahead of UTC on purpose: exercise dates are local wall-clock values,
// so "two hours after the end" has to be measured on the local clock. With now = 18:00 UTC =
// 19:00 local and an end of 17:00, an implementation that read UTC would say "not due yet".
public sealed class AutomaticExerciseHandOverTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset NowUtc = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0);
    private static readonly DateTime EndedTwoHoursAgo = new(2026, 11, 2, 17, 0, 0);

    private static readonly DueExercise Due =
        new(WindowId, "Key Stage 4 June", CheckingExerciseType.PupilData, EndedTwoHoursAgo);

    private readonly IWindowRepository _windows = Substitute.For<IWindowRepository>();
    private readonly ICloseExerciseService _sweep = Substitute.For<ICloseExerciseService>();
    private readonly IWindowAdminAuditWriter _audit = Substitute.For<IWindowAdminAuditWriter>();
    private readonly FixedTimeProvider _clock = new(NowUtc);

    private AutomaticExerciseHandOver Sut() => new(
        _windows, _sweep, _audit, _clock,
        Options.Create(new ExerciseHandOverSettings()),
        NullLogger<AutomaticExerciseHandOver>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    }

    private static CheckingExerciseDto Exercise(CheckingExerciseType type, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseType = type,
        StartDate = Start,
        EndDate = end,
        SortOrder = (int)type
    };

    private static CheckingWindowDto Window(Guid id, string title, params CheckingExerciseDto[] exercises) => new()
    {
        Id = id,
        Title = title,
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = Start,
        EndDate = exercises.Max(e => e.EndDate),
        Exercises = [.. exercises]
    };

    private void WindowsAre(params CheckingWindowDto[] windows) =>
        _windows.GetAllWindowsAsync(Arg.Any<CancellationToken>()).Returns([.. windows]);

    private void SweepDoes(int sent, int cancelled) =>
        _sweep.CloseAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(new CloseExerciseResult { Enqueued = sent, DraftsCancelled = cancelled });

    [Fact]
    public async Task An_exercise_two_hours_past_its_end_on_the_local_clock_is_due()
    {
        WindowsAre(Window(WindowId, "Key Stage 4 June", Exercise(CheckingExerciseType.PupilData, EndedTwoHoursAgo)));

        var due = Assert.Single(await Sut().FindDueAsync(CancellationToken.None));

        Assert.Equal(Due, due);
    }

    [Fact]
    public async Task An_exercise_that_is_open_or_just_closed_or_long_closed_is_not_due()
    {
        WindowsAre(
            Window(Guid.NewGuid(), "Still open", Exercise(CheckingExerciseType.PupilData, new DateTime(2026, 11, 3, 17, 0, 0))),
            Window(Guid.NewGuid(), "Closed one second short of two hours ago", Exercise(CheckingExerciseType.PupilData, new DateTime(2026, 11, 2, 17, 0, 1))),
            Window(Guid.NewGuid(), "Closed twenty-six hours ago", Exercise(CheckingExerciseType.PupilData, new DateTime(2026, 11, 1, 17, 0, 0))));

        Assert.Empty(await Sut().FindDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Each_exercise_of_a_window_is_judged_on_its_own_end_date()
    {
        // The ticket's warning: this is exercise closing, not window closing. The window itself
        // is open until its results enquiry ends in March.
        WindowsAre(Window(WindowId, "Key Stage 4 June",
            Exercise(CheckingExerciseType.PupilData, EndedTwoHoursAgo),
            Exercise(CheckingExerciseType.ResultsEnquiry, new DateTime(2027, 3, 31, 17, 0, 0))));

        var due = Assert.Single(await Sut().FindDueAsync(CancellationToken.None));

        Assert.Equal(CheckingExerciseType.PupilData, due.Exercise);
    }

    [Fact]
    public async Task Handing_over_runs_the_sweep_for_that_window_and_that_exercise()
    {
        SweepDoes(sent: 3, cancelled: 2);

        var outcome = await Sut().HandOverAsync(Due, CancellationToken.None);

        Assert.Equal(new AutomaticHandOverOutcome(WindowId, CheckingExerciseType.PupilData, 3, 2, Failed: false), outcome);
        await _sweep.Received(1).CloseAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_run_that_sent_or_cancelled_something_is_audited_with_the_counts()
    {
        SweepDoes(sent: 3, cancelled: 2);
        AutomaticHandOverAudit? written = null;
        await _audit.RecordAutomaticHandOverAsync(
            Arg.Do<AutomaticHandOverAudit>(a => written = a), Arg.Any<CancellationToken>());

        await Sut().HandOverAsync(Due, CancellationToken.None);

        Assert.NotNull(written);
        Assert.Equal(WindowId, written!.WindowId);
        Assert.Equal("Key Stage 4 June", written.WindowTitle);
        Assert.Equal(CheckingExerciseType.PupilData, written.Exercise);
        Assert.Equal(EndedTwoHoursAgo, written.ExerciseEnd);
        Assert.Equal(3, written.RequestsSent);
        Assert.Equal(2, written.DraftsCancelled);
        // The audit timestamp is the UTC instant, not the local wall clock the due rule reads.
        Assert.Equal(NowUtc.UtcDateTime, written.RanAtUtc);
        Assert.Equal(DateTimeKind.Utc, written.RanAtUtc.Kind);
    }

    [Fact]
    public async Task A_run_with_nothing_to_do_writes_no_audit_row()
    {
        // Every tick of the catch-up day after the first is this case: a day of empty retries
        // must not write a day of audit rows.
        SweepDoes(sent: 0, cancelled: 0);

        var outcome = await Sut().HandOverAsync(Due, CancellationToken.None);

        Assert.False(outcome.Failed);
        await _audit.DidNotReceiveWithAnyArgs().RecordAutomaticHandOverAsync(default!, default);
    }

    [Fact]
    public async Task A_sweep_that_fails_is_reported_not_thrown_and_not_audited()
    {
        _sweep.CloseAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CloseExerciseResult>(new InvalidOperationException("blob storage is down")));

        var outcome = await Sut().HandOverAsync(Due, CancellationToken.None);

        Assert.Equal(new AutomaticHandOverOutcome(WindowId, CheckingExerciseType.PupilData, 0, 0, Failed: true), outcome);
        await _audit.DidNotReceiveWithAnyArgs().RecordAutomaticHandOverAsync(default!, default);
    }

    [Fact]
    public async Task A_failed_audit_write_does_not_undo_the_outcome()
    {
        // The hand-over has happened and cannot be taken back; losing the audit row must not
        // turn it into a reported failure, or the next tick's log would contradict the queue.
        SweepDoes(sent: 3, cancelled: 2);
        _audit.RecordAutomaticHandOverAsync(Arg.Any<AutomaticHandOverAudit>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database is down")));

        var outcome = await Sut().HandOverAsync(Due, CancellationToken.None);

        Assert.Equal(new AutomaticHandOverOutcome(WindowId, CheckingExerciseType.PupilData, 3, 2, Failed: false), outcome);
    }

    [Fact]
    public async Task The_audit_row_is_written_even_when_the_host_is_stopping()
    {
        // A deploy fires the stop token. The sweep has already sent the requests by the time the
        // audit row is written, so the row must not be abandoned with it: no later run would
        // write it, because a later run finds nothing left to do.
        SweepDoes(sent: 3, cancelled: 2);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        await Sut().HandOverAsync(Due, stopping.Token);

        await _audit.Received(1).RecordAutomaticHandOverAsync(
            Arg.Any<AutomaticHandOverAudit>(), CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        // The host is shutting down: the job must stop, not log a failure and carry on.
        _sweep.CloseAsync(WindowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<CloseExerciseResult>(new CancellationToken(canceled: true)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Sut().HandOverAsync(Due, CancellationToken.None));
    }
}
