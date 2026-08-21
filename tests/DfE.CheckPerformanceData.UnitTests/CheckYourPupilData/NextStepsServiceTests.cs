using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// #317: the check-your-pupil-data page offers whatever is open now, for any number of exercises.
// The mapping from exercise to options is domain knowledge, so it lives here rather than in the
// controller — adding a future exercise type must mean adding a mapping entry, never editing
// branching logic, and no branch here may look at the window type.
public sealed class NextStepsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static NextStepsService Sut() =>
        new(new CheckingExerciseService(new FixedTimeProvider(Now)));

    private static readonly DateTime Yesterday = new(2026, 8, 19);
    private static readonly DateTime Tomorrow = new(2026, 8, 21);
    private static readonly DateTime LastMonth = new(2026, 7, 1);
    private static readonly DateTime NextMonth = new(2026, 9, 30);

    private static CheckingExerciseDto Open(CheckingExerciseType type, int sortOrder = 0) =>
        new() { ExerciseType = type, StartDate = Yesterday, EndDate = Tomorrow, SortOrder = sortOrder };

    private static CheckingExerciseDto Closed(CheckingExerciseType type, int sortOrder = 0) =>
        new() { ExerciseType = type, StartDate = LastMonth, EndDate = Yesterday, SortOrder = sortOrder };

    private static CheckingExerciseDto NotYetOpen(CheckingExerciseType type, int sortOrder = 0) =>
        new() { ExerciseType = type, StartDate = Tomorrow, EndDate = NextMonth, SortOrder = sortOrder };

    [Fact]
    public void An_open_pupil_data_exercise_offers_amend_and_confirm()
    {
        Assert.Equal(
            [NextSteps.RequestChange, NextSteps.Confirm],
            Sut().GetAvailableSteps([Open(CheckingExerciseType.PupilData)]));
    }

    [Fact]
    public void An_open_results_enquiry_exercise_offers_the_enquiry_option()
    {
        Assert.Equal(
            [NextSteps.ResultsEnquiry],
            Sut().GetAvailableSteps([Open(CheckingExerciseType.ResultsEnquiry)]));
    }

    [Fact]
    public void Two_open_exercises_offer_both_sets_in_exercise_sort_order()
    {
        var exercises = new[]
        {
            Open(CheckingExerciseType.ResultsEnquiry, sortOrder: 1),
            Open(CheckingExerciseType.PupilData, sortOrder: 0)
        };

        Assert.Equal(
            [NextSteps.RequestChange, NextSteps.Confirm, NextSteps.ResultsEnquiry],
            Sut().GetAvailableSteps(exercises));
    }

    [Fact]
    public void Amend_and_confirm_go_together_when_pupil_data_closes()
    {
        // They belong to the same exercise, so neither survives it.
        var exercises = new[]
        {
            Closed(CheckingExerciseType.PupilData, sortOrder: 0),
            Open(CheckingExerciseType.ResultsEnquiry, sortOrder: 1)
        };

        Assert.Equal([NextSteps.ResultsEnquiry], Sut().GetAvailableSteps(exercises));
    }

    [Fact]
    public void An_exercise_that_has_not_started_offers_nothing_yet()
    {
        var exercises = new[]
        {
            Open(CheckingExerciseType.PupilData, sortOrder: 0),
            NotYetOpen(CheckingExerciseType.ResultsEnquiry, sortOrder: 1)
        };

        Assert.Equal(
            [NextSteps.RequestChange, NextSteps.Confirm],
            Sut().GetAvailableSteps(exercises));
    }

    [Fact]
    public void No_open_exercise_offers_nothing_at_all()
    {
        var exercises = new[]
        {
            Closed(CheckingExerciseType.PupilData, sortOrder: 0),
            Closed(CheckingExerciseType.ResultsEnquiry, sortOrder: 1)
        };

        Assert.Empty(Sut().GetAvailableSteps(exercises));
    }

    [Fact]
    public void A_window_with_no_exercises_at_all_offers_nothing()
    {
        // Fails closed, via ICheckingExerciseService: a half-configured window must not open a
        // journey by accident.
        Assert.Empty(Sut().GetAvailableSteps([]));
    }

    [Fact]
    public void An_exercise_type_with_no_mapping_contributes_no_options()
    {
        // Fail closed rather than throw: an unmapped exercise must not offer a journey that has
        // nothing behind it, but it must also not take the whole page down.
        var unmapped = new CheckingExerciseDto
        {
            ExerciseType = (CheckingExerciseType)999,
            StartDate = Yesterday,
            EndDate = Tomorrow,
            SortOrder = 0
        };

        Assert.Empty(Sut().GetAvailableSteps([unmapped]));
    }

    [Fact]
    public void Every_exercise_type_that_exists_has_a_mapping()
    {
        // The page is the only way in to these journeys, so a new exercise type that nobody mapped
        // would ship as a silently unreachable feature.
        foreach (var type in Enum.GetValues<CheckingExerciseType>())
        {
            Assert.NotEmpty(Sut().GetAvailableSteps([Open(type)]));
        }
    }
}
