using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #315: the only place in the solution that compares a checking exercise's dates against the
// clock. Every case below is an "is this exercise open" answer some caller would otherwise have
// worked out for itself with its own DateTime.Now.
public sealed class CheckingExerciseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static CheckingExerciseService Sut() => new(new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static CheckingExerciseDto Exercise(
        CheckingExerciseType type, DateTime start, DateTime end, int sortOrder = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExerciseType = type,
            StartDate = start,
            EndDate = end,
            TabOrder = sortOrder,
            // Enabled, as a configured exercise is: IdFor and EndDateFor prefer the live row
            // (AB#307773), and a disabled row is never live.
            IsEnabled = true
        };

    // A display-only row of a real kind: the shape a pupil-data exercise takes once it is showing
    // refreshed data that nobody may act on (#466).
    private static CheckingExerciseDto DisplayOnly(
        CheckingExerciseType type, DateTime start, DateTime end, int sortOrder = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExerciseType = type,
            StartDate = start,
            EndDate = end,
            TabOrder = sortOrder,
            IsEnabled = true,
            DisplayOnly = true
        };

    private static readonly DateTime Yesterday = new(2026, 8, 18);
    private static readonly DateTime Tomorrow = new(2026, 8, 20);
    private static readonly DateTime LastMonth = new(2026, 7, 1);
    private static readonly DateTime NextMonth = new(2026, 9, 30);

    [Fact]
    public void IsOpen_is_true_when_the_exercise_brackets_now()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.True(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void IsOpen_is_false_before_the_exercise_starts()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Tomorrow, NextMonth) };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void IsOpen_is_false_after_the_exercise_ends()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday) };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void IsOpen_is_false_when_the_window_holds_no_row_for_that_type()
    {
        // Fail closed: a half-configured window must not open a journey by accident.
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void IsOpen_is_false_for_an_empty_exercise_list()
    {
        Assert.False(Sut().IsOpen([], CheckingExerciseType.PupilData));
    }

    [Fact]
    public void OpenCheckingExercises_returns_only_the_open_ones_in_sort_order()
    {
        var exercises = new[]
        {
            Exercise(CheckingExerciseType.ResultsEnquiry, Yesterday, Tomorrow, sortOrder: 5),
            Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow, sortOrder: 1)
        };

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            Sut().OpenCheckingExercises(exercises));
    }

    [Fact]
    public void OpenCheckingExercises_omits_an_exercise_that_has_not_started_or_has_ended()
    {
        // A window with three exercises in three different states.
        var exercises = new[]
        {
            Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow, sortOrder: 0),
            Exercise(CheckingExerciseType.ResultsEnquiry, Tomorrow, NextMonth, sortOrder: 1)
        };

        Assert.Equal([CheckingExerciseType.PupilData], Sut().OpenCheckingExercises(exercises));
    }

    [Fact]
    public void OpenCheckingExercises_is_empty_for_an_empty_exercise_list()
    {
        Assert.Empty(Sut().OpenCheckingExercises([]));
    }

    [Fact]
    public void EndDateFor_returns_the_exercises_own_end_date()
    {
        var exercises = new[]
        {
            Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow),
            Exercise(CheckingExerciseType.ResultsEnquiry, Yesterday, NextMonth)
        };

        Assert.Equal(NextMonth, Sut().EndDateFor(exercises, CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void EndDateFor_returns_the_end_date_of_a_closed_exercise_too()
    {
        // "Closed" means no actions, never "no content" — a caller still shows the date it ended.
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday) };

        Assert.Equal(Yesterday, Sut().EndDateFor(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void EndDateFor_is_null_when_there_is_no_row_for_that_type()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.Null(Sut().EndDateFor(exercises, CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void EndDateFor_is_null_for_an_empty_exercise_list()
    {
        Assert.Null(Sut().EndDateFor([], CheckingExerciseType.PupilData));
    }

    [Fact]
    public void IdFor_returns_the_exercises_own_id()
    {
        var pupilData = Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow);
        var enquiry = Exercise(CheckingExerciseType.ResultsEnquiry, Yesterday, NextMonth);

        Assert.Equal(enquiry.Id, Sut().IdFor([pupilData, enquiry], CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void IdFor_returns_the_id_of_a_closed_exercise_too()
    {
        // A request is stamped with the exercise it belongs to, and a draft can be saved and a
        // submitted request read back after that exercise has ended. Withholding the id once the
        // dates lapse would blank the stamp on exactly the rows an admin most needs to group.
        var closed = Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday);

        Assert.Equal(closed.Id, Sut().IdFor([closed], CheckingExerciseType.PupilData));
    }

    [Fact]
    public void IdFor_is_null_when_there_is_no_row_for_that_type()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.Null(Sut().IdFor(exercises, CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void IdFor_is_null_for_an_empty_exercise_list()
    {
        Assert.Null(Sut().IdFor([], CheckingExerciseType.PupilData));
    }

    // AB#298317: the landing card prints the pupil-data range, so the start is needed beside the
    // end. A lookup, not a clock question — same shape as EndDateFor.
    [Fact]
    public void StartDateFor_returns_the_exercises_own_start_or_null_when_absent()
    {
        var pupilData = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2026, 10, 5),
            EndDate = new DateTime(2026, 10, 16, 17, 0, 0),
            TabOrder = 0
        };

        Assert.Equal(new DateTime(2026, 10, 5), Sut().StartDateFor([pupilData], CheckingExerciseType.PupilData));
        Assert.Null(Sut().StartDateFor([pupilData], CheckingExerciseType.ResultsEnquiry));
        Assert.Null(Sut().StartDateFor([], CheckingExerciseType.PupilData));
    }

    [Fact]
    public void An_exercise_is_open_on_its_first_and_last_instant()
    {
        // The boundaries are inclusive, matching the outer window's own StartDate <= now <= EndDate.
        var startsNow = new[] { Exercise(CheckingExerciseType.PupilData, Now.DateTime, NextMonth) };
        var endsNow = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Now.DateTime) };

        Assert.True(Sut().IsOpen(startsNow, CheckingExerciseType.PupilData));
        Assert.True(Sut().IsOpen(endsNow, CheckingExerciseType.PupilData));
    }

    // ── HasClosed (AB#298317 review) ─────────────────────────────────────────
    // "Closed" was being derived as !IsOpen, which has no lower bound: an exercise that has not
    // started yet is not open either, and a "the window has closed" notice about it is wrong.

    [Fact]
    public void HasClosed_is_true_after_the_exercise_ends()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday) };

        Assert.True(Sut().HasClosed(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void HasClosed_is_false_while_the_exercise_is_open()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.False(Sut().HasClosed(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void HasClosed_is_false_before_the_exercise_starts()
    {
        // The whole point of the method: not open, but not closed either.
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Tomorrow, NextMonth) };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
        Assert.False(Sut().HasClosed(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void HasClosed_is_false_when_the_window_holds_no_row_for_that_type()
    {
        // Fail closed means "no actions", and here it must also mean "no closed notice" — a window
        // that never ran an exercise has nothing to have closed.
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday) };

        Assert.False(Sut().HasClosed(exercises, CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void HasClosed_is_false_for_an_empty_exercise_list()
    {
        Assert.False(Sut().HasClosed([], CheckingExerciseType.PupilData));
    }

    [Fact]
    public void HasClosed_is_false_on_the_exercises_last_instant()
    {
        // IsOpen brackets inclusively, so an exercise ending exactly now is still open — HasClosed
        // must agree, or one instant would be both open and closed.
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Now.DateTime) };

        Assert.True(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
        Assert.False(Sut().HasClosed(exercises, CheckingExerciseType.PupilData));
    }

    // #466: "open" means a school may act on it, not merely that the dates bracket now. A
    // display-only exercise is view-only however its dates read, so an admin can mark one
    // read-only without having to move its dates into the past to make that stick.
    [Fact]
    public void IsOpen_is_false_for_a_display_only_exercise_inside_its_dates()
    {
        var exercises = new[] { DisplayOnly(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void OpenCheckingExercises_leaves_out_a_display_only_exercise_inside_its_dates()
    {
        var exercises = new[]
        {
            DisplayOnly(CheckingExerciseType.PupilData, Yesterday, Tomorrow),
            Exercise(CheckingExerciseType.ResultsEnquiry, Yesterday, NextMonth, sortOrder: 1)
        };

        Assert.Equal(
            [CheckingExerciseType.ResultsEnquiry],
            Sut().OpenCheckingExercises(exercises));
    }

    // The dates still answer on their own for an ordinary exercise — this rule adds a reason to be
    // closed, it does not replace the date comparison.
    [Fact]
    public void An_ordinary_exercise_inside_its_dates_is_still_open()
    {
        var exercises = new[] { Exercise(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.True(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
        Assert.Equal(
            [CheckingExerciseType.PupilData],
            Sut().OpenCheckingExercises(exercises));
    }

    // HasClosed is untouched: it reports a lapsed end date, which is a fact about the dates alone.
    // A display-only exercise still inside its dates has not closed — it was never open.
    [Fact]
    public void HasClosed_still_answers_from_the_end_date_alone()
    {
        var exercises = new[] { DisplayOnly(CheckingExerciseType.PupilData, Yesterday, Tomorrow) };

        Assert.False(Sut().HasClosed(exercises, CheckingExerciseType.PupilData));
    }

    // The admin summary's one tag per exercise: what schools can see and do with it now.
    private static CheckingExerciseDto Live(
        CheckingExerciseType? type, DateTime start, DateTime end, bool displayOnly = false) =>
        new()
        {
            Id = Guid.NewGuid(), ExerciseType = type, StartDate = start, EndDate = end,
            IsEnabled = true, DisplayOnly = displayOnly
        };

    [Fact]
    public void StatusOf_is_visible_for_a_live_exercise_inside_its_dates()
    {
        var exercise = Live(CheckingExerciseType.PupilData, Yesterday, Tomorrow);

        Assert.Equal(ExerciseSchoolStatus.Visible, Sut().StatusOf([exercise], exercise));
    }

    [Fact]
    public void StatusOf_is_hidden_for_a_disabled_exercise_inside_its_dates()
    {
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Yesterday, EndDate = Tomorrow, IsEnabled = false
        };

        Assert.Equal(ExerciseSchoolStatus.Hidden, Sut().StatusOf([exercise], exercise));
    }

    [Fact]
    public void StatusOf_is_hidden_outside_its_visibility_dates()
    {
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Yesterday, EndDate = Tomorrow, IsEnabled = true,
            VisibleUntil = Now.DateTime.AddHours(-1)
        };

        Assert.Equal(ExerciseSchoolStatus.Hidden, Sut().StatusOf([exercise], exercise));
    }

    [Fact]
    public void StatusOf_is_hidden_when_the_window_itself_is_outside_its_dates()
    {
        // One exercise, so the window's dates are its dates: before the start schools see nothing.
        var exercise = Live(CheckingExerciseType.PupilData, Tomorrow, NextMonth);

        Assert.Equal(ExerciseSchoolStatus.Hidden, Sut().StatusOf([exercise], exercise));
    }

    [Fact]
    public void StatusOf_is_visible_closed_before_its_start_while_another_exercise_keeps_the_window_shown()
    {
        var pupilData = Live(CheckingExerciseType.PupilData, LastMonth, Tomorrow);
        var enquiry = Live(CheckingExerciseType.ResultsEnquiry, Tomorrow, NextMonth);

        Assert.Equal(ExerciseSchoolStatus.VisibleClosed, Sut().StatusOf([pupilData, enquiry], enquiry));
    }

    [Fact]
    public void StatusOf_is_visible_closed_after_its_end_while_another_exercise_keeps_the_window_shown()
    {
        var pupilData = Live(CheckingExerciseType.PupilData, LastMonth, Yesterday);
        var enquiry = Live(CheckingExerciseType.ResultsEnquiry, LastMonth, NextMonth);

        Assert.Equal(ExerciseSchoolStatus.VisibleClosed, Sut().StatusOf([pupilData, enquiry], pupilData));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CheckingExerciseType.PupilData)]
    public void StatusOf_a_display_only_exercise_is_never_visible_closed(CheckingExerciseType? type)
    {
        // No journey, so there are no changes to close: outside its dates it is simply visible.
        var pupilData = Live(CheckingExerciseType.PupilData, LastMonth, NextMonth);
        var share = Live(type, LastMonth, Yesterday, displayOnly: true);

        Assert.Equal(ExerciseSchoolStatus.Visible, Sut().StatusOf([pupilData, share], share));
    }

    // A window with one exercise (KS4 June) used to vanish the moment that exercise closed,
    // because the window's dates are the union of its exercises' dates. VisibleUntil keeps it
    // shown, read only, after the end date.
    private static CheckingExerciseDto LiveUntil(
        CheckingExerciseType type, DateTime start, DateTime end, DateTime? visibleUntil) =>
        new()
        {
            Id = Guid.NewGuid(), ExerciseType = type, StartDate = start, EndDate = end,
            IsEnabled = true, VisibleUntil = visibleUntil
        };

    [Fact]
    public void IsWindowShown_inside_the_exercise_dates()
    {
        Assert.True(Sut().IsWindowShown([Live(CheckingExerciseType.PupilData, Yesterday, Tomorrow)]));
    }

    [Fact]
    public void IsWindowShown_is_false_before_the_first_exercise_starts()
    {
        var exercise = LiveUntil(CheckingExerciseType.PupilData, Tomorrow, NextMonth, NextMonth.AddDays(7));

        Assert.False(Sut().IsWindowShown([exercise]));
    }

    [Fact]
    public void IsWindowShown_is_false_after_the_end_date_when_no_visible_until_is_set()
    {
        Assert.False(Sut().IsWindowShown([Live(CheckingExerciseType.PupilData, LastMonth, Yesterday)]));
    }

    [Fact]
    public void IsWindowShown_after_the_end_date_while_visible_until_is_ahead()
    {
        var exercise = LiveUntil(CheckingExerciseType.PupilData, LastMonth, Yesterday, NextMonth);

        Assert.True(Sut().IsWindowShown([exercise]));
    }

    [Fact]
    public void IsWindowShown_is_false_once_visible_until_has_passed()
    {
        // VisibleUntil is exclusive, as in IsLiveAt.
        var exercise = LiveUntil(CheckingExerciseType.PupilData, LastMonth, Yesterday, Now.DateTime);

        Assert.False(Sut().IsWindowShown([exercise]));
    }

    [Fact]
    public void IsWindowShown_ignores_the_visible_until_of_a_disabled_exercise()
    {
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            StartDate = LastMonth, EndDate = Yesterday, IsEnabled = false, VisibleUntil = NextMonth
        };

        Assert.False(Sut().IsWindowShown([exercise]));
    }

    [Fact]
    public void IsWindowShown_is_false_for_a_window_with_no_exercises()
    {
        Assert.False(Sut().IsWindowShown([]));
    }

    [Fact]
    public void StatusOf_is_visible_closed_after_its_end_while_its_visible_until_keeps_the_window_shown()
    {
        var exercise = LiveUntil(CheckingExerciseType.PupilData, LastMonth, Yesterday, NextMonth);

        Assert.Equal(ExerciseSchoolStatus.VisibleClosed, Sut().StatusOf([exercise], exercise));
    }

    // AB#307773: the extension helpers on the DTO. Pure date arithmetic, no clock: whether an
    // extension is open is the service's question, asked in a later task.
    private static CheckingExerciseExtensionDto Extension(DateTime end, params string[] laestabs) =>
        new() { Id = Guid.NewGuid(), EndDate = end, Laestabs = laestabs };

    [Fact]
    public void EndDateFor_is_the_extension_end_for_a_named_school_in_either_form()
    {
        var exercise = Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday);
        exercise.Extensions.Add(Extension(NextMonth, "9334290"));

        Assert.Equal(NextMonth, exercise.EndDateFor("933/4290"));
        Assert.Equal(NextMonth, exercise.EndDateFor("9334290"));
        Assert.Equal(Yesterday, exercise.EndDateFor("8604070"));
        Assert.Equal(Yesterday, exercise.EndDateFor(null));
        Assert.Equal(Yesterday, exercise.EndDateFor(""));
    }

    [Fact]
    public void EndDateFor_never_moves_a_school_earlier_than_the_exercise_end()
    {
        var exercise = Exercise(CheckingExerciseType.PupilData, LastMonth, NextMonth);
        exercise.Extensions.Add(Extension(Yesterday, "9334290"));   // closed early before the exercise ended

        Assert.Equal(NextMonth, exercise.EndDateFor("9334290"));
    }

    [Fact]
    public void DeriveDatesFromExercises_includes_the_latest_extension_end()
    {
        var exercise = Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday);
        exercise.Extensions.Add(Extension(NextMonth, "9334290"));
        var window = new CheckingWindowDto
        {
            Title = "w", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = LastMonth, EndDate = Yesterday, Exercises = [exercise]
        };

        window.DeriveDatesFromExercises();

        Assert.Equal(NextMonth, window.EndDate);
    }

    // ── AB#307773: school-aware answers ──────────────────────────────────────

    private static CheckingExerciseDto Extended(CheckingExerciseType type, DateTime start, DateTime end, DateTime extensionEnd, params string[] laestabs)
    {
        var exercise = Exercise(type, start, end);
        exercise.Extensions.Add(Extension(extensionEnd, laestabs));
        return exercise;
    }

    [Fact]
    public void A_named_school_is_open_after_the_exercise_end_until_its_extension_end()
    {
        var exercises = new[] { Extended(CheckingExerciseType.PupilData, LastMonth, Yesterday, Tomorrow, "9334290") };

        Assert.True(Sut().IsOpen(exercises, CheckingExerciseType.PupilData, "933/4290"));
        Assert.False(Sut().HasClosed(exercises, CheckingExerciseType.PupilData, "933/4290"));
        Assert.Equal(Tomorrow, Sut().EndDateFor(exercises, CheckingExerciseType.PupilData, "933/4290"));
    }

    [Fact]
    public void Every_other_school_sees_the_exercise_closed()
    {
        var exercises = new[] { Extended(CheckingExerciseType.PupilData, LastMonth, Yesterday, Tomorrow, "9334290") };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData, "860/4070"));
        Assert.True(Sut().HasClosed(exercises, CheckingExerciseType.PupilData, "860/4070"));
        Assert.Equal(Yesterday, Sut().EndDateFor(exercises, CheckingExerciseType.PupilData, "860/4070"));
        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData, null));
        // The exercise-level answer is unchanged: the exercise's own dates.
        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData));
        Assert.True(Sut().HasClosed(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void A_named_school_closes_once_its_extension_has_ended()
    {
        var exercises = new[] { Extended(CheckingExerciseType.PupilData, LastMonth, LastMonth.AddDays(7), Yesterday, "9334290") };

        Assert.False(Sut().IsOpen(exercises, CheckingExerciseType.PupilData, "9334290"));
        Assert.True(Sut().HasClosed(exercises, CheckingExerciseType.PupilData, "9334290"));
    }

    [Fact]
    public void OpenCheckingExercises_follows_the_school()
    {
        var exercises = new[]
        {
            Extended(CheckingExerciseType.PupilData, LastMonth, Yesterday, Tomorrow, "9334290"),
            Exercise(CheckingExerciseType.ResultsEnquiry, LastMonth, NextMonth, sortOrder: 1)
        };

        Assert.Equal([CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            Sut().OpenCheckingExercises(exercises, "9334290"));
        Assert.Equal([CheckingExerciseType.ResultsEnquiry], Sut().OpenCheckingExercises(exercises, "8604070"));
        Assert.Equal([CheckingExerciseType.ResultsEnquiry], Sut().OpenCheckingExercises(exercises));
    }

    [Fact]
    public void IsWindowShown_stays_true_for_a_named_school_while_its_extension_runs()
    {
        var exercises = new[] { Extended(CheckingExerciseType.PupilData, LastMonth, Yesterday, Tomorrow, "9334290") };

        Assert.True(Sut().IsWindowShown(exercises, "9334290"));
        Assert.False(Sut().IsWindowShown(exercises, "8604070"));
        // With no school, the outer end includes the extension — that is what the landing page's
        // date bracket sees in the window's own EndDate.
        Assert.True(Sut().IsWindowShown(exercises, null));
        Assert.True(Sut().IsWindowShown(exercises));
    }

    [Fact]
    public void An_extension_is_open_until_its_end_and_closed_after()
    {
        Assert.True(Sut().IsOpen(Extension(Tomorrow, "9334290")));
        Assert.False(Sut().HasClosed(Extension(Tomorrow, "9334290")));
        Assert.False(Sut().IsOpen(Extension(Yesterday, "9334290")));
        Assert.True(Sut().HasClosed(Extension(Yesterday, "9334290")));
        // The last instant is open, as for an exercise.
        Assert.True(Sut().IsOpen(Extension(Now.DateTime, "9334290")));
    }

    // ── Related fix: IdFor / EndDateFor take the live row of a kind, not the first ────────

    [Fact]
    public void IdFor_prefers_the_one_live_interactive_row_of_a_kind()
    {
        var retired = Exercise(CheckingExerciseType.PupilData, LastMonth, Yesterday);   // IsEnabled false below
        var live = Exercise(CheckingExerciseType.PupilData, LastMonth, NextMonth, sortOrder: 1);
        var exercises = new[]
        {
            new CheckingExerciseDto { Id = retired.Id, ExerciseType = retired.ExerciseType, StartDate = retired.StartDate, EndDate = retired.EndDate, IsEnabled = false },
            new CheckingExerciseDto { Id = live.Id, ExerciseType = live.ExerciseType, StartDate = live.StartDate, EndDate = live.EndDate, IsEnabled = true, TabOrder = 1 }
        };

        Assert.Equal(live.Id, Sut().IdFor(exercises, CheckingExerciseType.PupilData));
        Assert.Equal(NextMonth, Sut().EndDateFor(exercises, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void IdFor_falls_back_to_the_first_row_in_tab_order_when_no_row_is_live()
    {
        var exercises = new[]
        {
            new CheckingExerciseDto { Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData, StartDate = LastMonth, EndDate = Yesterday, IsEnabled = false, TabOrder = 2 },
            new CheckingExerciseDto { Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData, StartDate = LastMonth, EndDate = Yesterday, IsEnabled = false, TabOrder = 1 }
        };

        Assert.Equal(exercises[1].Id, Sut().IdFor(exercises, CheckingExerciseType.PupilData));
    }
}
