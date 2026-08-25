using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319: which exercises a window type starts ticked with. A starting point rather than a rule — the
// admin may tick or untick any of them, which is how KS4 Autumn can be given a results enquiry
// without a code change.
public class WindowExercisesDefaultsTests
{
    [Fact]
    public void Post16_starts_with_both_exercises()
        => Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            WindowExercises.DefaultsFor(CheckingWindowType.Post16));

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void Every_other_type_starts_with_pupil_data_only(CheckingWindowType type)
        => Assert.Equal(CheckingExerciseType.PupilData, Assert.Single(WindowExercises.DefaultsFor(type)));

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void Every_window_type_starts_with_at_least_one_exercise(CheckingWindowType type)
        => Assert.NotEmpty(WindowExercises.DefaultsFor(type));

    [Fact]
    public void Sort_order_is_stable_and_distinct_across_every_exercise_type()
    {
        // The order drives the wizard's date pages and every per-exercise list, so two types must
        // never share a position.
        var orders = Enum.GetValues<CheckingExerciseType>()
            .Select(WindowExercises.SortOrderFor)
            .ToList();

        Assert.Equal(orders.Count, orders.Distinct().Count());
    }
}
