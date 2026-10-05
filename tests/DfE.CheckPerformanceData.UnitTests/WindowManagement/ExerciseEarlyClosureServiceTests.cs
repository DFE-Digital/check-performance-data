using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#301022: the pre-close checks and the clock. "Open" is asked of the real
// CheckingExerciseService on the same TimeProvider, so these tests fail if the two ever disagree
// about what time it is. The local zone is one hour ahead of UTC on purpose: exercise dates are
// local wall-clock values and the audit timestamp is UTC, and a test in UTC could not tell them apart.
public sealed class ExerciseEarlyClosureServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const CheckingExerciseType Exercise = CheckingExerciseType.ResultsEnquiry;

    // The row being closed. Closing is addressed by id (#466): a window may hold several releases
    // of one kind.
    private static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // 09:39:27.450 UTC is 10:39:27.450 on the local wall clock.
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 3, 9, 39, 27, 450, TimeSpan.Zero);
    private static readonly DateTime LocalNowToTheSecond = new(2026, 9, 3, 10, 39, 27);

    private static readonly DateTime LastMonth = new(2026, 8, 1, 0, 0, 0);
    private static readonly DateTime Yesterday = new(2026, 9, 2, 17, 0, 0);
    private static readonly DateTime Tomorrow = new(2026, 9, 4, 0, 0, 0);
    private static readonly DateTime ScheduledEnd = new(2027, 12, 15, 17, 0, 0);

    private static readonly EarlyClosureActor Actor = new("sub-1", "Banks Jamgbadi");

    private readonly IWindowRepository _windows = Substitute.For<IWindowRepository>();
    private readonly FixedTimeProvider _clock = new(NowUtc);

    private ExerciseEarlyClosureService Sut() => new(_windows, new CheckingExerciseService(_clock), _clock);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    }

    // The Exercise kind's row is the target unless a test says otherwise.
    private static CheckingExerciseDto Row(
        CheckingExerciseType? type, DateTime start, DateTime end, Guid? id = null) => new()
    {
        Id = id ?? (type == Exercise ? TargetId : Guid.NewGuid()),
        ExerciseType = type,
        StartDate = start,
        EndDate = end,
        TabOrder = (int)(type ?? 0)
    };

    private void WindowHolds(params CheckingExerciseDto[] exercises) =>
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "Key Stage 4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = exercises.Min(e => e.StartDate),
            EndDate = exercises.Max(e => e.EndDate),
            Exercises = [.. exercises]
        });

    // Captures what the service asked the repository to write, and answers that it was written.
    private Func<ExerciseEarlyClosure?> RepositoryWrites(bool written = true)
    {
        ExerciseEarlyClosure? captured = null;
        _windows.CloseExerciseEarlyAsync(
                Arg.Do<ExerciseEarlyClosure>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(written);
        return () => captured;
    }

    [Fact]
    public async Task An_unknown_window_is_not_found()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotFound, result.Status);
        await _windows.DidNotReceiveWithAnyArgs().CloseExerciseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task A_window_that_does_not_run_the_exercise_is_not_found()
    {
        WindowHolds(Row(CheckingExerciseType.PupilData, LastMonth, ScheduledEnd));

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotFound, result.Status);
        await _windows.DidNotReceiveWithAnyArgs().CloseExerciseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task A_data_share_is_not_found()
    {
        // No kind: no journey to shut and no requests to hand over.
        WindowHolds(new CheckingExerciseDto
        {
            Id = TargetId, ExerciseType = null, DisplayOnly = true, StartDate = LastMonth, EndDate = ScheduledEnd
        });

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotFound, result.Status);
        await _windows.DidNotReceiveWithAnyArgs().CloseExerciseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task Only_the_named_release_is_closed_when_the_window_holds_two_of_its_kind()
    {
        // #466: an autumn and a revised release of one kind. The closure names the row by id, so
        // the repository's guarded write cannot move the other.
        WindowHolds(Row(Exercise, LastMonth, ScheduledEnd, Guid.NewGuid()), Row(Exercise, LastMonth, ScheduledEnd));
        var written = RepositoryWrites();

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.Closed, result.Status);
        Assert.Equal(TargetId, written()!.ExerciseId);
    }

    [Fact]
    public async Task A_release_that_has_ended_is_not_open_though_another_of_its_kind_is()
    {
        // Asked of the row, not of the kind: the other release being open closes nothing here.
        WindowHolds(Row(Exercise, LastMonth, ScheduledEnd, Guid.NewGuid()), Row(Exercise, LastMonth, Yesterday));

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotOpen, result.Status);
        await _windows.DidNotReceiveWithAnyArgs().CloseExerciseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task An_exercise_that_has_already_ended_is_not_open()
    {
        // "A window that is already closed cannot be closed again."
        WindowHolds(Row(Exercise, LastMonth, Yesterday));

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotOpen, result.Status);
        await _windows.DidNotReceiveWithAnyArgs().CloseExerciseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task An_exercise_that_has_not_started_is_not_open()
    {
        WindowHolds(Row(Exercise, Tomorrow, ScheduledEnd));

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotOpen, result.Status);
        await _windows.DidNotReceiveWithAnyArgs().CloseExerciseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task An_open_exercise_is_closed_one_second_before_now_on_the_local_clock()
    {
        WindowHolds(Row(Exercise, LastMonth, ScheduledEnd));
        var written = RepositoryWrites();

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.Closed, result.Status);
        Assert.Equal(LocalNowToTheSecond, result.ClosedAt);
        Assert.Equal(ScheduledEnd, result.ScheduledEnd);

        var closure = written()!;
        Assert.Equal(LocalNowToTheSecond.AddSeconds(-1), closure.NewEndDate);
        Assert.Equal(DateTimeKind.Unspecified, closure.NewEndDate.Kind);
        Assert.Equal(NowUtc.UtcDateTime, closure.ClosedAtUtc);
        Assert.Equal(DateTimeKind.Utc, closure.ClosedAtUtc.Kind);
    }

    [Fact]
    public async Task The_closure_names_the_window_the_exercise_the_scheduled_end_and_who_closed_it()
    {
        WindowHolds(Row(CheckingExerciseType.PupilData, LastMonth, Tomorrow), Row(Exercise, LastMonth, ScheduledEnd));
        var written = RepositoryWrites();

        await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        var closure = written()!;
        Assert.Equal(WindowId, closure.WindowId);
        Assert.Equal(TargetId, closure.ExerciseId);
        Assert.Equal(Exercise, closure.Exercise);
        Assert.Equal(ScheduledEnd, closure.ScheduledEnd);
        Assert.Equal("sub-1", closure.UserId);
        Assert.Equal("Banks Jamgbadi", closure.ClosedByName);
    }

    [Fact]
    public async Task A_lost_race_is_reported_as_not_open()
    {
        // The repository's compare-and-set found the end date had changed since it was read.
        WindowHolds(Row(Exercise, LastMonth, ScheduledEnd));
        RepositoryWrites(written: false);

        var result = await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotOpen, result.Status);
    }

    [Fact]
    public async Task The_new_end_date_is_already_closed_at_the_instant_of_the_close()
    {
        // IsOpen keeps an exercise's last instant open (EndDate >= now), so ending it AT now would
        // leave it open for the rest of that instant. One second earlier is shut straight away,
        // by the same clock, with no waiting for time to move on.
        WindowHolds(Row(Exercise, LastMonth, ScheduledEnd));
        var written = RepositoryWrites();

        await Sut().CloseEarlyAsync(WindowId, TargetId, Actor, CancellationToken.None);

        var after = new[] { Row(Exercise, LastMonth, written()!.NewEndDate) };
        var clock = new CheckingExerciseService(_clock);
        Assert.False(clock.IsOpen(after, Exercise));
        Assert.True(clock.HasClosed(after, Exercise));
    }
}
