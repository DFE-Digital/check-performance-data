using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #466 slice 1: an exercise with no Kind (ExerciseType == null) is display-only. It can be named,
// dated and given files, but it has no blob prefix until slice 2, so it cannot be validated yet.
public class CheckingExerciseDtoTests
{
    private static CheckingWindowDatasetDto CompleteSlot(string name) => new()
    {
        Name = name, IngressFile = $"{name}.csv", SchemaFile = $"{name}.json", SortOrder = 0
    };

    [Fact]
    public void CanValidate_is_false_for_a_display_only_exercise_even_with_its_files()
    {
        var exercise = new CheckingExerciseDto
        {
            ExerciseType = null,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14),
            Datasets = [CompleteSlot("data")]
        };

        Assert.True(exercise.HasRequiredFiles);
        Assert.False(exercise.CanValidate);
    }

    [Fact]
    public void CanValidate_is_false_for_a_kind_exercise_missing_its_files()
    {
        var exercise = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14),
            Datasets = [new CheckingWindowDatasetDto { Name = "pupils" }]
        };

        Assert.False(exercise.CanValidate);
    }

    [Fact]
    public void CanValidate_is_true_for_a_kind_exercise_with_its_files()
    {
        var exercise = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14),
            Datasets = [CompleteSlot("pupils")]
        };

        Assert.True(exercise.CanValidate);
    }

    [Fact]
    public void FindExercise_by_id_finds_a_display_only_exercise()
    {
        var id = Guid.NewGuid();
        var window = new CheckingWindowDto
        {
            Title = "w", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14),
            Exercises =
            [
                new CheckingExerciseDto { Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14) },
                new CheckingExerciseDto { Id = id, ExerciseType = null, Name = "Summary data (Autumn)",
                    StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14) }
            ]
        };

        Assert.Equal("Summary data (Autumn)", window.FindExercise(id)!.Name);
        Assert.Null(window.FindExercise(Guid.NewGuid()));
    }

    [Fact]
    public void FindExercise_by_kind_never_matches_a_display_only_exercise()
    {
        var window = new CheckingWindowDto
        {
            Title = "w", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14),
            Exercises =
            [
                new CheckingExerciseDto { ExerciseType = null, Name = "Summary data (Autumn)",
                    StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14) }
            ]
        };

        Assert.Null(window.FindExercise(CheckingExerciseType.PupilData));
        Assert.Null(window.FindExercise(CheckingExerciseType.ResultsEnquiry));
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, "Pupil data checking", "Pupils")]
    [InlineData(CheckingExerciseType.ResultsEnquiry, "Results enquiry", "Results")]
    public void Every_kind_has_a_default_name_and_tab_name(
        CheckingExerciseType kind, string name, string tabName)
    {
        Assert.Equal(name, CheckingExerciseNames.NameFor(kind));
        Assert.Equal(tabName, CheckingExerciseNames.TabNameFor(kind));
    }

    [Fact]
    public void Every_kind_is_named()
    {
        // No default case in the switch: a new Kind must be given its wording here, not inherit
        // another's.
        foreach (var kind in Enum.GetValues<CheckingExerciseType>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CheckingExerciseNames.NameFor(kind)));
            Assert.False(string.IsNullOrWhiteSpace(CheckingExerciseNames.TabNameFor(kind)));
        }
    }

    [Fact]
    public void An_unmapped_kind_throws_rather_than_borrowing_a_name()
    {
        var unmapped = (CheckingExerciseType)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckingExerciseNames.NameFor(unmapped));
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckingExerciseNames.TabNameFor(unmapped));
    }
}
