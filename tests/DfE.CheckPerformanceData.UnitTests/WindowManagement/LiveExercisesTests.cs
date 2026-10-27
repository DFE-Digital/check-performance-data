using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// "Live" is the one rule for whether schools see an exercise: enabled, and inside its visibility
// dates. A window with no live exercise is not set up yet and is hidden from schools. Two
// exercises of one kind must never exist, live or not, because the kind lookups could not tell
// which one to use.
public sealed class LiveExercisesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0);

    [Fact]
    public void An_enabled_exercise_with_no_visibility_dates_is_live()
    {
        Assert.True(Exercise().IsLiveAt(Now));
    }

    [Fact]
    public void A_disabled_exercise_is_not_live()
    {
        Assert.False(Exercise(enabled: false).IsLiveAt(Now));
    }

    [Fact]
    public void An_exercise_is_not_live_before_VisibleFrom()
    {
        Assert.False(Exercise(from: Now.AddSeconds(1)).IsLiveAt(Now));
        Assert.True(Exercise(from: Now).IsLiveAt(Now));
    }

    [Fact]
    public void VisibleUntil_is_exclusive()
    {
        Assert.False(Exercise(until: Now).IsLiveAt(Now));
        Assert.True(Exercise(until: Now.AddSeconds(1)).IsLiveAt(Now));
    }

    [Fact]
    public void Two_exercises_of_one_kind_clash()
    {
        var first = Exercise();
        var second = Exercise();

        Assert.Same(first, OneExercisePerKind.FindClash(second, [first, second]));
    }

    [Fact]
    public void Exercises_of_one_kind_clash_even_when_their_visibility_does_not_overlap()
    {
        var autumn = Exercise(until: Now);
        var revised = Exercise(from: Now);

        Assert.Same(autumn, OneExercisePerKind.FindClash(revised, [autumn, revised]));
    }

    [Fact]
    public void A_disabled_exercise_still_clashes_with_another_of_its_kind()
    {
        var live = Exercise();
        var disabled = Exercise(enabled: false);

        Assert.Same(live, OneExercisePerKind.FindClash(disabled, [live, disabled]));
        Assert.Same(disabled, OneExercisePerKind.FindClash(live, [live, disabled]));
    }

    [Fact]
    public void An_exercise_does_not_clash_with_itself()
    {
        var exercise = Exercise();

        Assert.Null(OneExercisePerKind.FindClash(exercise, [exercise]));
    }

    [Fact]
    public void Exercises_of_different_kinds_never_clash()
    {
        var pupils = Exercise(CheckingExerciseType.PupilData);
        var results = Exercise(CheckingExerciseType.ResultsEnquiry);

        Assert.Null(OneExercisePerKind.FindClash(results, [pupils, results]));
    }

    [Fact]
    public void Data_shares_with_no_kind_never_clash()
    {
        var first = Exercise(type: null);
        var second = Exercise(type: null);

        Assert.Null(OneExercisePerKind.FindClash(second, [first, second]));
    }

    [Fact]
    public void A_window_with_one_live_exercise_has_a_live_exercise()
    {
        var window = Window(Exercise(enabled: false), Exercise(type: null));

        Assert.True(window.HasLiveExerciseAt(Now));
    }

    [Fact]
    public void A_window_whose_exercises_are_all_disabled_or_out_of_visibility_has_no_live_exercise()
    {
        var window = Window(Exercise(enabled: false), Exercise(until: Now));

        Assert.False(window.HasLiveExerciseAt(Now));
    }

    [Fact]
    public void A_window_with_no_exercises_has_no_live_exercise()
    {
        Assert.False(Window().HasLiveExerciseAt(Now));
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June, CheckingExerciseType.PupilData, "Pupils")]
    [InlineData(CheckingWindowType.KS2, CheckingExerciseType.PupilData, "Pupils")]
    [InlineData(CheckingWindowType.Post16, CheckingExerciseType.PupilData, "Students")]
    [InlineData(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry, "Results")]
    [InlineData(CheckingWindowType.KS4Autumn, CheckingExerciseType.ResultsEnquiry, "Results")]
    public void Each_kind_has_a_default_tab_name(CheckingWindowType window, CheckingExerciseType kind,
        string expected)
    {
        Assert.Equal(expected, WindowExercises.DefaultTabName(window, kind));
    }

    [Fact]
    public void Every_kind_has_a_default_tab_name_for_every_window_type()
    {
        foreach (var window in Enum.GetValues<CheckingWindowType>())
        foreach (var kind in Enum.GetValues<CheckingExerciseType>())
            Assert.False(string.IsNullOrWhiteSpace(WindowExercises.DefaultTabName(window, kind)));
    }

    private static CheckingWindowDto Window(params CheckingExerciseDto[] exercises) => new()
    {
        Title = "Window",
        StartDate = Now.AddDays(-1),
        EndDate = Now.AddDays(1),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        Exercises = [.. exercises]
    };

    private static CheckingExerciseDto Exercise(CheckingExerciseType? type = CheckingExerciseType.PupilData,
        bool enabled = true, DateTime? from = null, DateTime? until = null) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseType = type,
        TabName = "Tab",
        IsEnabled = enabled,
        VisibleFrom = from,
        VisibleUntil = until,
        StartDate = Now.AddDays(-1),
        EndDate = Now.AddDays(1)
    };
}
