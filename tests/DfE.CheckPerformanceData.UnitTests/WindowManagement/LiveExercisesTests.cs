using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// "Live" is the one rule for whether schools see an exercise: enabled, and inside its visibility
// dates. A window with no live exercise is not set up yet and is hidden from schools. Two live
// exercises of one kind must never exist, because the journeys could not tell which one to use.
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
    public void Two_enabled_exercises_of_one_kind_with_open_ended_visibility_clash()
    {
        var first = Exercise();
        var second = Exercise();

        Assert.Same(first, LiveExercises.FindClash(second, [first, second]));
    }

    [Fact]
    public void Exercises_of_one_kind_whose_visibility_does_not_overlap_do_not_clash()
    {
        // An autumn results enquiry followed by a revised one: one ends as the other starts.
        var autumn = Exercise(until: Now);
        var revised = Exercise(from: Now);

        Assert.Null(LiveExercises.FindClash(revised, [autumn, revised]));
    }

    [Fact]
    public void A_disabled_exercise_never_clashes()
    {
        var live = Exercise();
        var disabled = Exercise(enabled: false);

        Assert.Null(LiveExercises.FindClash(disabled, [live, disabled]));
        Assert.Null(LiveExercises.FindClash(live, [live, disabled]));
    }

    [Fact]
    public void Exercises_of_different_kinds_never_clash()
    {
        var pupils = Exercise(CheckingExerciseType.PupilData);
        var results = Exercise(CheckingExerciseType.ResultsEnquiry);

        Assert.Null(LiveExercises.FindClash(results, [pupils, results]));
    }

    [Fact]
    public void Data_shares_with_no_kind_never_clash()
    {
        var first = Exercise(type: null);
        var second = Exercise(type: null);

        Assert.Null(LiveExercises.FindClash(second, [first, second]));
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
