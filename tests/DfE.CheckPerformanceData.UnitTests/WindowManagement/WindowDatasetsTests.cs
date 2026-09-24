using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.WindowManagement;

public sealed class WindowDatasetsTests
{
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

    [Theory]
    [InlineData(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry)]
    [InlineData(CheckingWindowType.KS4Autumn, CheckingExerciseType.ResultsEnquiry)]
    public void EverySupplierSlot_FeedsTheJourney(CheckingWindowType window, CheckingExerciseType exercise)
    {
        // These are the "journey data" files: only a new version of these may change what a
        // journey reads.
        var datasets = WindowDatasets.DefaultsFor(window, exercise);

        Assert.NotEmpty(datasets);
        Assert.All(datasets, d => Assert.True(d.FeedsJourney, d.Name));
    }
}
