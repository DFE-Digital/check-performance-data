using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// A Post16 window ingests TWO supplier pupil files (included + non-included) because the
// non-included file has no P_INCL column; every other window type ingests one. A results enquiry
// ingests one file per source in the results feed (#324), each slot named by the tag it stamps.
public class WindowDatasetDefaultsTests
{
    [Fact]
    public void Post16_defaults_to_an_included_and_a_non_included_dataset()
    {
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.PupilData);

        Assert.Equal(2, datasets.Count);
        Assert.Equal("included", datasets[0].Name);
        Assert.True(datasets[0].Included);
        Assert.Equal(0, datasets[0].SortOrder);
        Assert.Equal("nonincluded", datasets[1].Name);
        Assert.False(datasets[1].Included);
        Assert.Equal(1, datasets[1].SortOrder);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void Other_window_types_default_to_one_dataset_with_no_stamped_inclusion(CheckingWindowType type)
    {
        var datasets = WindowDatasets.DefaultsFor(type, CheckingExerciseType.PupilData);

        var only = Assert.Single(datasets);
        Assert.Equal("pupils", only.Name);
        Assert.Null(only.Included);
    }

    [Fact]
    public void No_pupil_dataset_stamps_a_source_file()
    {
        // Provenance is a results concept. A pupil record has no SOURCE column to stamp, and
        // stamping one would fail validation against a schema that forbids extra properties.
        foreach (CheckingWindowType type in Enum.GetValues<CheckingWindowType>())
        {
            Assert.All(
                WindowDatasets.DefaultsFor(type, CheckingExerciseType.PupilData),
                dataset => Assert.Null(dataset.SourceFile));
        }
    }

    [Fact]
    public void A_16_to_19_results_enquiry_gets_a_slot_per_source_file()
    {
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        Assert.Equal(
            [
                ResultsFileTags.Post16Main,
                ResultsFileTags.Post16LateResults1,
                ResultsFileTags.Post16LateResults2,
                ResultsFileTags.Post16Revised,
                ResultsFileTags.Post16Retention
            ],
            datasets.Select(d => d.Name));
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void A_KS4_results_enquiry_gets_the_KS4_source_files(CheckingWindowType type)
    {
        var datasets = WindowDatasets.DefaultsFor(type, CheckingExerciseType.ResultsEnquiry);

        Assert.Equal(
            [
                ResultsFileTags.Ks4Main,
                ResultsFileTags.Ks4LateResults1,
                ResultsFileTags.Ks4LateResults2,
                ResultsFileTags.Ks4Revised
            ],
            datasets.Select(d => d.Name));
    }

    [Fact]
    public void A_results_slot_stamps_the_tag_it_is_named_after()
    {
        // The slot's name is what the admin matches a delivered file to, and its SourceFile is what
        // every row from that file is stamped with. If the two could differ, a file uploaded to the
        // right-looking slot could be stamped as another file entirely.
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        Assert.All(datasets, dataset =>
        {
            Assert.Equal(dataset.Name, dataset.SourceFile);
            Assert.Null(dataset.Included);
        });
        Assert.Equal([0, 1, 2, 3, 4], datasets.Select(d => d.SortOrder));
    }

    [Fact]
    public void Only_the_main_results_file_is_required()
    {
        // The late, revised and retention files land weeks apart and one may never land. Requiring
        // them would leave an exercise that can never be validated and a school with no results.
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        Assert.True(datasets[0].Required);
        Assert.All(datasets.Skip(1), dataset => Assert.False(dataset.Required));
    }

    [Fact]
    public void Every_pupil_file_is_required()
    {
        // Both 16-19 pupil files ingest in one run and each carries a whole population, so a run
        // missing one would write a blob missing half the school.
        foreach (CheckingWindowType type in Enum.GetValues<CheckingWindowType>())
        {
            Assert.All(
                WindowDatasets.DefaultsFor(type, CheckingExerciseType.PupilData),
                dataset => Assert.True(dataset.Required));
        }
    }

    [Fact]
    public void A_KS2_results_enquiry_has_no_source_files_to_load()
    {
        // KS2 has no results feed. An empty set is honest — the summary page says the exercise has
        // no ingress files — where inventing KS4's slots would give an admin six uploads to guess at.
        Assert.Empty(WindowDatasets.DefaultsFor(CheckingWindowType.KS2, CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void An_unmapped_exercise_type_gets_no_slots_rather_than_throwing()
    {
        // Unlike CheckingExerciseBlobPaths, a missing row here cannot misfile anything: an exercise
        // is allowed to hold no datasets, so a new type simply ingests nothing until it is mapped.
        var unmapped = (CheckingExerciseType)999;

        Assert.Empty(WindowDatasets.DefaultsFor(CheckingWindowType.Post16, unmapped));
    }
}
