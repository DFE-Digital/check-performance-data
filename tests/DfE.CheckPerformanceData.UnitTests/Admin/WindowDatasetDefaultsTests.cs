using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// Pupil data checking and data shares start with no slots: the admin adds the files. A results
// enquiry ingests one file per source in the results feed (#324), each slot named by the tag it
// stamps, so it still starts with those slots.
public class WindowDatasetDefaultsTests
{
    [Theory]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void Pupil_data_checking_starts_with_no_slots(CheckingWindowType type)
    {
        // The admin adds the pupil files: one for KS4, two (included + non-included) for 16-19.
        Assert.Empty(WindowDatasets.DefaultsFor(type, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void A_data_share_starts_with_no_slots()
    {
        // A share has no supplier feed. A default slot was labelled "Pupils" and was required,
        // though the share needed neither.
        Assert.Empty(WindowDatasets.DefaultsFor(CheckingWindowType.Post16, null));
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, null, true)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, null, false)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, ResultsFileTags.Post16LateResults1, true)]
    [InlineData(null, null, false)]
    [InlineData(null, ResultsFileTags.Post16LateResults1, false)]
    public void An_added_file_feeds_the_journey_on_pupil_data_or_as_a_results_source(
        CheckingExerciseType? type, string? source, bool feeds)
        // A results file with a source is supplier results, so the journey reads it. A results
        // file with no source is display only, and a data share has no journey.
        => Assert.Equal(feeds, WindowDatasets.AddedSlotFeedsJourney(type, source));

    [Fact]
    public void A_KS4_results_tag_is_stale_on_a_16_to_19_window_but_an_admin_slot_never_is()
    {
        Assert.True(WindowDatasets.IsStaleSupplierSlot(
            CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry, ResultsFileTags.Ks4Main));
        Assert.False(WindowDatasets.IsStaleSupplierSlot(
            CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry, ResultsFileTags.Post16Main));
        Assert.False(WindowDatasets.IsStaleSupplierSlot(
            CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry, "revised-summary"));
        Assert.False(WindowDatasets.IsStaleSupplierSlot(
            CheckingWindowType.KS4June, CheckingExerciseType.PupilData, "included"));
    }

    [Fact]
    public void A_16_to_19_results_enquiry_gets_a_slot_per_file_of_the_year()
    {
        // October: included, non-included, late 1. November: late 2. February: the two revised
        // files replace the first four. March: revised with retention replaces included revised.
        // The admin retires a slot when its file is replaced, so every slot exists from the start.
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        Assert.Equal(
            [
                ResultsFileTags.Post16Included,
                ResultsFileTags.Post16NonIncluded,
                ResultsFileTags.Post16LateResults1,
                ResultsFileTags.Post16LateResults2,
                ResultsFileTags.Post16IncludedRevised,
                ResultsFileTags.Post16NonIncludedRevised,
                ResultsFileTags.Post16IncludedRevisedWithRetention
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
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], datasets.Select(d => d.SortOrder));
    }

    [Fact]
    public void Only_the_first_results_file_is_required()
    {
        // The late, revised and retention files land weeks apart and one may never land. Requiring
        // them would leave an exercise that can never be validated and a school with no results.
        // A retired slot is never required, so the first slot stops blocking once it is replaced.
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        Assert.True(datasets[0].Required);
        Assert.All(datasets.Skip(1), dataset => Assert.False(dataset.Required));
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
