using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// Pupil data checking (except KS4 June) and data shares start with no slots: the admin adds the
// files. KS4 June pupil data starts with its one supplier file. A results
// enquiry ingests one file per source in the results feed (#324), each slot named by the tag it
// stamps, so it still starts with those slots.
public class WindowDatasetDefaultsTests
{
    [Theory]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void Pupil_data_checking_starts_with_no_slots(CheckingWindowType type)
    {
        // The admin adds the pupil files, for example two (included + non-included) for 16-19.
        Assert.Empty(WindowDatasets.DefaultsFor(type, CheckingExerciseType.PupilData));
    }

    [Fact]
    public void KS4_June_pupil_data_checking_starts_with_one_pupils_slot()
    {
        // KS4 June has one supplier pupil file. Each pupil carries their own P_INCL, so the slot
        // stamps no inclusion, and the journey reads it.
        var dataset = Assert.Single(
            WindowDatasets.DefaultsFor(CheckingWindowType.KS4June, CheckingExerciseType.PupilData));

        Assert.Equal(WindowDatasets.Pupils, dataset.Name);
        Assert.Null(dataset.Included);
        Assert.Null(dataset.SourceFile);
        Assert.True(dataset.FeedsJourney);
        Assert.True(dataset.Required);
        Assert.Equal(0, dataset.SortOrder);
    }

    [Fact]
    public void A_data_share_starts_with_no_slots()
    {
        // A share has no supplier feed. A default slot was labelled "Pupils" and was required,
        // though the share needed neither.
        Assert.Empty(WindowDatasets.DefaultsFor(CheckingWindowType.Post16, null));
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, false, true)]
    [InlineData(CheckingExerciseType.PupilData, true, false)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, false, true)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, true, false)]
    [InlineData(null, false, false)]
    public void An_added_file_feeds_the_journey_unless_it_is_a_data_share(
        CheckingExerciseType? type, bool dataShare, bool feeds)
        // A file added as a data share (previously published data) is only shown. A data share
        // exercise has no journey.
        => Assert.Equal(feeds, WindowDatasets.AddedSlotFeedsJourney(type, dataShare));

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

    [Theory]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void A_pupils_slot_is_never_stale_on_another_window_type(CheckingWindowType type)
    {
        // "pupils" is the KS4 June default, but it is a plain name an admin may also give a slot.
        // Treating it as stale would delete that slot, and its file, when the window is saved.
        Assert.False(WindowDatasets.IsStaleSupplierSlot(type, CheckingExerciseType.PupilData, WindowDatasets.Pupils));
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
    public void A_16_to_19_results_enquiry_requires_the_three_files_it_starts_with()
    {
        // Included, non-included and late results 1 are the minimum the exercise starts with. The
        // later files land weeks apart and one may never land. Requiring them would leave an
        // exercise that can never be validated and a school with no results.
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        Assert.Equal(
            [ResultsFileTags.Post16Included, ResultsFileTags.Post16NonIncluded, ResultsFileTags.Post16LateResults1],
            datasets.Where(d => d.Required).Select(d => d.Name));
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void A_KS4_results_enquiry_requires_only_the_main_results_file(CheckingWindowType type)
    {
        var datasets = WindowDatasets.DefaultsFor(type, CheckingExerciseType.ResultsEnquiry);

        Assert.Equal([ResultsFileTags.Ks4Main], datasets.Where(d => d.Required).Select(d => d.Name));
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
