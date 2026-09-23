using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.WindowManagement;

public sealed class WindowDatasetsTests
{
    [Fact]
    public void AnUntypedShare_GetsOneSlotToUploadInto()
    {
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, null);

        var dataset = Assert.Single(datasets);
        Assert.Equal("data", dataset.Name);
        Assert.Equal(0, dataset.SortOrder);
    }

    [Fact]
    public void FindExercise_OfNoType_FindsNothing()
    {
        var window = new CheckingWindowDto
        {
            Title = "t", StartDate = DateTime.Today, EndDate = DateTime.Today,
            KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
            Exercises =
            [
                new CheckingExerciseDto { ExerciseType = null, StartDate = DateTime.Today, EndDate = DateTime.Today },
                new CheckingExerciseDto { ExerciseType = null, StartDate = DateTime.Today, EndDate = DateTime.Today }
            ]
        };

        // Two typeless rows are legal. Asking for "the exercise of no type" must not throw.
        Assert.Null(window.FindExercise(null));
    }
}
