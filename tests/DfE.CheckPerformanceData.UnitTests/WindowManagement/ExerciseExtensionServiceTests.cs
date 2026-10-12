using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#307773: the rules behind an extension. "Open" and "closed" are asked of the real
// CheckingExerciseService on the same TimeProvider, as ExerciseEarlyClosureServiceTests does, so
// the stamp and the comparison cannot disagree. The local zone is one hour ahead of UTC on
// purpose: extension dates are local wall-clock values and the audit stamps are UTC.
public sealed class ExerciseExtensionServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ShareId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid XId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid NewId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    // 09:39:27.450 UTC is 10:39:27.450 on the local wall clock.
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 3, 9, 39, 27, 450, TimeSpan.Zero);
    private static readonly DateTime LocalNowToTheSecond = new(2026, 9, 3, 10, 39, 27);

    private static readonly DateTime LastMonth = new(2026, 8, 1, 0, 0, 0);
    private static readonly DateTime Yesterday = new(2026, 9, 2, 17, 0, 0);
    private static readonly DateTime Tomorrow = new(2026, 9, 4, 17, 0, 0);
    private static readonly DateTime NextWeek = new(2026, 9, 10, 17, 0, 0);

    private static readonly ExtensionActor Actor = new("sub-1", "Banks Jamgbadi");

    private readonly IWindowRepository _windows = Substitute.For<IWindowRepository>();
    private readonly IExerciseExtensionRepository _extensions = Substitute.For<IExerciseExtensionRepository>();
    private readonly ICheckingDataReader _checkingData = Substitute.For<ICheckingDataReader>();
    private readonly FixedTimeProvider _clock = new(NowUtc);

    private ExerciseExtensionService Sut() =>
        new(_windows, _extensions, new CheckingExerciseService(_clock), _checkingData, _clock);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    }

    // X: open until tomorrow, names 933/4290.
    private static CheckingExerciseExtensionDto X() =>
        new() { Id = XId, EndDate = Tomorrow, Laestabs = ["9334290"] };

    // Ended yesterday with the exercise (closed early), so read only.
    private static CheckingExerciseExtensionDto ClosedExtension() =>
        new() { Id = OtherId, EndDate = Yesterday, Laestabs = ["1112222"] };

    // The pupil-data exercise ended yesterday; its extensions are X unless a test names others.
    // The window also holds a data share, which has no kind and so cannot be extended.
    private CheckingExerciseDto WindowHolds(
        Guid? currentReleaseId = null, params CheckingExerciseExtensionDto[] extensions)
    {
        var exercise = new CheckingExerciseDto
        {
            Id = ExerciseId,
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = LastMonth,
            EndDate = Yesterday,
            CurrentReleaseId = currentReleaseId,
            Extensions = extensions.Length == 0 ? [X()] : [.. extensions]
        };
        var share = new CheckingExerciseDto
        {
            Id = ShareId, ExerciseType = null, DisplayOnly = true, StartDate = LastMonth, EndDate = NextWeek
        };
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "Key Stage 4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = LastMonth,
            EndDate = NextWeek,
            Exercises = [exercise, share]
        });
        return exercise;
    }

    private static ExtensionCommand Command(DateTime endDate, IReadOnlyList<string> laestabs, Guid? exerciseId = null) =>
        new(WindowId, exerciseId ?? ExerciseId, endDate, laestabs, Actor);

    // ---- Add ----

    [Fact]
    public async Task An_end_date_on_the_exercise_end_adds_no_time_and_is_refused()
    {
        WindowHolds();

        var result = await Sut().AddAsync(Command(Yesterday, ["8604070"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.EndDateNotAfterExercise, result.Status);
        Assert.Equal(Yesterday, result.ExerciseEnd);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task An_end_date_before_the_exercise_end_is_refused()
    {
        WindowHolds();

        var result = await Sut().AddAsync(Command(LastMonth, ["8604070"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.EndDateNotAfterExercise, result.Status);
        Assert.Equal(Yesterday, result.ExerciseEnd);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task An_add_naming_no_school_is_refused()
    {
        // The form says so first; this is the backstop for a caller that does not.
        WindowHolds();

        var result = await Sut().AddAsync(Command(NextWeek, []), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.NoSchools, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task A_school_already_in_another_extension_is_refused_with_that_extensions_end()
    {
        WindowHolds();

        var result = await Sut().AddAsync(Command(NextWeek, ["8604070", "933/4290"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.SchoolInAnotherExtension, result.Status);
        Assert.Equal([new ExtensionConflict("9334290", Tomorrow)], result.Conflicts!);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task A_school_in_a_closed_extension_is_still_in_it()
    {
        // A school is in at most one extension of an exercise, whether or not that one has ended.
        WindowHolds(null, X(), ClosedExtension());

        var result = await Sut().AddAsync(Command(NextWeek, ["1112222"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.SchoolInAnotherExtension, result.Status);
        Assert.Equal([new ExtensionConflict("1112222", Yesterday)], result.Conflicts!);
    }

    [Fact]
    public async Task A_data_share_cannot_be_extended()
    {
        WindowHolds();

        var result = await Sut().AddAsync(Command(NextWeek, ["8604070"], ShareId), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task An_exercise_not_on_the_window_is_not_found()
    {
        WindowHolds();

        var result = await Sut().AddAsync(Command(NextWeek, ["8604070"], Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task An_unknown_window_is_not_found()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        var result = await Sut().AddAsync(Command(NextWeek, ["8604070"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task A_valid_add_is_written_as_digits_with_its_admin_and_a_utc_stamp()
    {
        WindowHolds();
        NewExtension? written = null;
        _extensions.AddAsync(Arg.Do<NewExtension>(e => written = e), Arg.Any<CancellationToken>()).Returns(NewId);

        var result = await Sut().AddAsync(Command(NextWeek, ["860/4070", "1112222"]), CancellationToken.None);

        Assert.Equal(new ExtensionSaveResult(ExtensionSaveStatus.Saved, NewId), result);
        Assert.NotNull(written);
        Assert.Equal(WindowId, written.WindowId);
        Assert.Equal(ExerciseId, written.ExerciseId);
        Assert.Equal(CheckingExerciseType.PupilData, written.Exercise);
        Assert.Equal(NextWeek, written.EndDate);
        Assert.Equal(["8604070", "1112222"], written.Laestabs);
        Assert.Equal(Actor, written.Actor);
        Assert.Equal(NowUtc.UtcDateTime, written.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, written.CreatedAtUtc.Kind);
    }

    // ---- Edit ----

    [Fact]
    public async Task An_edit_may_keep_its_own_school_and_add_another()
    {
        // The school's own extension does not conflict with itself.
        WindowHolds();
        ExtensionEdit? written = null;
        _extensions.UpdateAsync(Arg.Do<ExtensionEdit>(e => written = e), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Sut().EditAsync(
            XId, Tomorrow, Command(NextWeek, ["933/4290", "8604070"]), CancellationToken.None);

        Assert.Equal(new ExtensionSaveResult(ExtensionSaveStatus.Saved, XId), result);
        Assert.NotNull(written);
        Assert.Equal(WindowId, written.WindowId);
        Assert.Equal(ExerciseId, written.ExerciseId);
        Assert.Equal(CheckingExerciseType.PupilData, written.Exercise);
        Assert.Equal(XId, written.ExtensionId);
        Assert.Equal(Tomorrow, written.PreviousEndDate);
        Assert.Equal(NextWeek, written.NewEndDate);
        Assert.Equal(["9334290", "8604070"], written.Laestabs);
        Assert.Equal(Actor, written.Actor);
        Assert.Equal(NowUtc.UtcDateTime, written.EditedAtUtc);
    }

    [Fact]
    public async Task An_edit_may_not_take_a_school_from_another_extension()
    {
        var other = new CheckingExerciseExtensionDto { Id = OtherId, EndDate = NextWeek, Laestabs = ["1112222"] };
        WindowHolds(null, X(), other);

        var result = await Sut().EditAsync(
            XId, Tomorrow, Command(NextWeek, ["9334290", "1112222"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.SchoolInAnotherExtension, result.Status);
        Assert.Equal([new ExtensionConflict("1112222", NextWeek)], result.Conflicts!);
        await _extensions.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task A_closed_extension_cannot_be_edited()
    {
        WindowHolds(null, X(), ClosedExtension());

        var result = await Sut().EditAsync(
            OtherId, Yesterday, Command(NextWeek, ["1112222"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.Closed, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task An_edit_to_an_end_date_on_the_exercise_end_is_refused()
    {
        WindowHolds();

        var result = await Sut().EditAsync(XId, Tomorrow, Command(Yesterday, ["9334290"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.EndDateNotAfterExercise, result.Status);
        Assert.Equal(Yesterday, result.ExerciseEnd);
        await _extensions.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task An_edit_removing_every_school_is_refused()
    {
        WindowHolds();

        var result = await Sut().EditAsync(XId, Tomorrow, Command(NextWeek, []), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.NoSchools, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task An_edit_of_an_extension_not_on_the_exercise_is_not_found()
    {
        WindowHolds();

        var result = await Sut().EditAsync(
            Guid.NewGuid(), Tomorrow, Command(NextWeek, ["9334290"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task An_edit_that_lost_the_race_is_stale()
    {
        // The repository's compare-and-set found the end date had moved since the admin read it.
        WindowHolds();
        _extensions.UpdateAsync(Arg.Any<ExtensionEdit>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sut().EditAsync(XId, Tomorrow, Command(NextWeek, ["9334290"]), CancellationToken.None);

        Assert.Equal(ExtensionSaveStatus.Stale, result.Status);
    }

    // ---- Schools without data ----

    [Fact]
    public async Task A_school_with_no_file_in_what_the_exercise_shows_is_listed()
    {
        var exercise = WindowHolds(currentReleaseId: Guid.NewGuid());
        _checkingData.HasSchoolDataAsync(WindowId, exercise, "9334290", Arg.Any<CancellationToken>()).Returns(true);
        _checkingData.HasSchoolDataAsync(WindowId, exercise, "8604070", Arg.Any<CancellationToken>()).Returns(false);

        var warnings = await Sut().FindSchoolsWithoutDataAsync(
            WindowId, ExerciseId, ["933/4290", "8604070"], CancellationToken.None);

        Assert.Equal(["8604070"], warnings.LaestabsWithoutData);
        Assert.False(warnings.ExerciseHasNoRelease);
    }

    [Fact]
    public async Task With_no_release_every_school_is_listed_and_no_file_is_looked_for()
    {
        WindowHolds(currentReleaseId: null);

        var warnings = await Sut().FindSchoolsWithoutDataAsync(
            WindowId, ExerciseId, ["933/4290", "8604070"], CancellationToken.None);

        Assert.Equal(["9334290", "8604070"], warnings.LaestabsWithoutData);
        Assert.True(warnings.ExerciseHasNoRelease);
        await _checkingData.DidNotReceiveWithAnyArgs().HasSchoolDataAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task An_unknown_exercise_warns_of_nothing()
    {
        WindowHolds(currentReleaseId: Guid.NewGuid());

        var warnings = await Sut().FindSchoolsWithoutDataAsync(
            WindowId, Guid.NewGuid(), ["8604070"], CancellationToken.None);

        Assert.Empty(warnings.LaestabsWithoutData);
        Assert.False(warnings.ExerciseHasNoRelease);
        await _checkingData.DidNotReceiveWithAnyArgs().HasSchoolDataAsync(default, default!, default!, default);
    }

    // ---- Close early ----

    [Fact]
    public async Task An_open_extension_is_closed_one_second_before_now_on_the_local_clock()
    {
        WindowHolds();
        ExtensionEarlyClosure? written = null;
        _extensions.CloseEarlyAsync(Arg.Do<ExtensionEarlyClosure>(c => written = c), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await Sut().CloseEarlyAsync(WindowId, ExerciseId, XId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.Closed, result.Status);
        Assert.Equal(LocalNowToTheSecond, result.ClosedAt);
        Assert.Equal(Tomorrow, result.ScheduledEnd);

        Assert.NotNull(written);
        Assert.Equal(WindowId, written.WindowId);
        Assert.Equal(ExerciseId, written.ExerciseId);
        Assert.Equal(CheckingExerciseType.PupilData, written.Exercise);
        Assert.Equal(XId, written.ExtensionId);
        Assert.Equal(Tomorrow, written.ScheduledEnd);
        Assert.Equal(LocalNowToTheSecond.AddSeconds(-1), written.NewEndDate);
        Assert.Equal(DateTimeKind.Unspecified, written.NewEndDate.Kind);
        Assert.Equal(NowUtc.UtcDateTime, written.ClosedAtUtc);
        Assert.Equal(DateTimeKind.Utc, written.ClosedAtUtc.Kind);
        Assert.Equal(Actor, written.Actor);

        // Already shut at the instant of the close, by the same clock.
        var after = new CheckingExerciseExtensionDto { Id = XId, EndDate = written.NewEndDate };
        Assert.False(new CheckingExerciseService(_clock).IsOpen(after));
    }

    [Fact]
    public async Task A_closed_extension_is_not_open()
    {
        WindowHolds(null, X(), ClosedExtension());

        var result = await Sut().CloseEarlyAsync(WindowId, ExerciseId, OtherId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotOpen, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().CloseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task An_unknown_extension_is_not_found()
    {
        WindowHolds();

        var result = await Sut().CloseEarlyAsync(WindowId, ExerciseId, Guid.NewGuid(), Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().CloseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task An_extension_addressed_through_the_data_share_is_not_found()
    {
        WindowHolds();

        var result = await Sut().CloseEarlyAsync(WindowId, ShareId, XId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().CloseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task A_close_in_an_unknown_window_is_not_found()
    {
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        var result = await Sut().CloseEarlyAsync(WindowId, ExerciseId, XId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotFound, result.Status);
        await _extensions.DidNotReceiveWithAnyArgs().CloseEarlyAsync(default!, default);
    }

    [Fact]
    public async Task A_close_that_lost_the_race_is_reported_as_not_open()
    {
        WindowHolds();
        _extensions.CloseEarlyAsync(Arg.Any<ExtensionEarlyClosure>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sut().CloseEarlyAsync(WindowId, ExerciseId, XId, Actor, CancellationToken.None);

        Assert.Equal(EarlyClosureStatus.NotOpen, result.Status);
    }
}
