using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public class ResultsSourcesTests
{
    [Fact]
    public void Every_offered_tag_has_its_own_label()
    {
        foreach (var type in Enum.GetValues<CheckingWindowType>())
        {
            var sources = ResultsSources.For(type);
            Assert.Equal(sources.Count, sources.Select(s => s.Tag).Distinct().Count());
            Assert.All(sources, s => Assert.NotEqual(s.Tag, s.Label));
        }
    }

    [Theory]
    [InlineData(ResultsFileTags.Post16Included, "Included")]
    [InlineData(ResultsFileTags.Post16NonIncluded, "Non-included")]
    [InlineData(ResultsFileTags.Post16LateResults2, "Late results 2")]
    [InlineData(ResultsFileTags.Post16IncludedRevisedWithRetention, "Included revised with retention")]
    // A tag no longer offered keeps its label: blobs written before the change still carry it.
    [InlineData(ResultsFileTags.Post16Main, "Main results")]
    [InlineData(ResultsFileTags.Post16Retention, "Retention")]
    public void A_tag_is_shown_by_its_label(string tag, string label)
        => Assert.Equal(label, ResultsSources.LabelFor(tag));

    [Fact]
    public void An_unknown_tag_is_shown_as_it_is()
        => Assert.Equal("something-else", ResultsSources.LabelFor("something-else"));

    [Fact]
    public void A_tag_no_longer_offered_is_not_offered()
        => Assert.DoesNotContain(ResultsSources.For(CheckingWindowType.Post16), s => s.Tag == ResultsFileTags.Post16Main);

    [Fact]
    public void KS2_has_no_results_sources()
        => Assert.Empty(ResultsSources.For(CheckingWindowType.KS2));

    [Theory]
    [InlineData(ResultsFileTags.Post16LateResults2, true)]
    [InlineData(ResultsFileTags.Ks4LateResults2, true)]
    [InlineData(ResultsFileTags.Post16LateResults1, false)]
    [InlineData(null, false)]
    public void Only_the_second_late_file_is_the_awaited_late_file(string? tag, bool expected)
        => Assert.Equal(expected, ResultsSources.IsSecondLateResults(tag));
}

public class RetiredDatasetTests
{
    private static CheckingWindowDatasetDto Slot(string name, bool required = false, bool complete = true, bool retired = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Required = required,
        Retired = retired,
        IngressFile = complete ? $"{name}.csv" : string.Empty,
        SchemaFile = complete ? $"{name}.json" : string.Empty,
        IngressFileChecksum = complete ? name : string.Empty,
        SchemaFileChecksum = complete ? name : string.Empty
    };

    private static CheckingExerciseDto Exercise(params CheckingWindowDatasetDto[] slots) => new()
    {
        ExerciseType = CheckingExerciseType.ResultsEnquiry,
        StartDate = DateTime.Today,
        EndDate = DateTime.Today,
        Datasets = [.. slots]
    };

    [Fact]
    public void A_run_does_not_read_a_retired_slot()
    {
        var exercise = Exercise(Slot("inc", retired: true), Slot("inc-rev"));

        Assert.Equal(["inc-rev"], exercise.DatasetsToIngest.Select(d => d.Name));
    }

    [Fact]
    public void A_retired_required_slot_does_not_block_validation()
    {
        // February: the included file is replaced. Its slot stays required, but it is retired.
        var exercise = Exercise(Slot("inc", required: true, complete: false, retired: true), Slot("inc-rev"));

        Assert.True(exercise.HasRequiredFiles);
    }

    [Fact]
    public void An_exercise_with_only_retired_files_cannot_be_validated()
    {
        var exercise = Exercise(Slot("inc", retired: true));

        Assert.False(exercise.HasRequiredFiles);
    }

    [Fact]
    public void Retiring_a_slot_makes_the_validation_stamp_stale()
    {
        var inc = Slot("inc");
        var exercise = Exercise(inc, Slot("late"));
        exercise.ValidatedAt = DateTime.UtcNow;
        exercise.ValidatedIngressChecksum = exercise.CurrentIngressChecksum;
        exercise.ValidatedSchemaChecksum = exercise.CurrentSchemaChecksum;
        Assert.True(exercise.IsValidated);

        inc.Retired = true;

        Assert.False(exercise.IsValidated);
    }
}
