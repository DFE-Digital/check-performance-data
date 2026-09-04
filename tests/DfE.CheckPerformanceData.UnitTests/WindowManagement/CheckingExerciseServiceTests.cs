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
            SortOrder = sortOrder
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

    [Fact]
    public void An_exercise_is_open_on_its_first_and_last_instant()
    {
        // The boundaries are inclusive, matching the outer window's own StartDate <= now <= EndDate.
        var startsNow = new[] { Exercise(CheckingExerciseType.PupilData, Now.DateTime, NextMonth) };
        var endsNow = new[] { Exercise(CheckingExerciseType.PupilData, LastMonth, Now.DateTime) };

        Assert.True(Sut().IsOpen(startsNow, CheckingExerciseType.PupilData));
        Assert.True(Sut().IsOpen(endsNow, CheckingExerciseType.PupilData));
    }
}
