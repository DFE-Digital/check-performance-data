using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// The windows list showed two copies of one window identically, though one had no files at all.
// "Visible" says schools see the exercise; this says whether it holds anything for them.
public sealed class ExerciseDataStatusTests
{
    private static readonly Guid DatasetId = Guid.NewGuid();

    private static CheckingWindowDatasetDto Slot(string ingress = "c1", bool retired = false) => new()
    {
        Id = DatasetId, Name = "pupils", Retired = retired,
        IngressFile = ingress == "" ? "" : "file.csv", IngressFileChecksum = ingress,
        SchemaFile = "schema.json", SchemaFileChecksum = "s1"
    };

    private static CheckingExerciseDto Exercise(bool released, params CheckingWindowDatasetDto[] slots)
    {
        var release = new CheckingExerciseReleaseDto
        {
            Id = Guid.NewGuid(),
            Files =
            [
                new CheckingExerciseReleaseFileDto
                {
                    DatasetId = DatasetId, DatasetName = "pupils",
                    IngressFileChecksum = "c1", SchemaFileChecksum = "s1"
                }
            ]
        };
        return new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 6, 1),
            CurrentReleaseId = released ? release.Id : null,
            Releases = released ? [release] : [],
            Datasets = [.. slots]
        };
    }

    [Fact]
    public void An_exercise_with_no_slots_has_no_files()
        => Assert.Equal(ExerciseDataStatus.NoFiles, Exercise(released: false).DataStatus);

    [Fact]
    public void An_exercise_whose_slots_are_empty_has_no_files()
        => Assert.Equal(ExerciseDataStatus.NoFiles, Exercise(released: false, Slot("")).DataStatus);

    [Fact]
    public void Uploaded_files_with_no_live_release_are_not_validated()
        => Assert.Equal(ExerciseDataStatus.NotValidated, Exercise(released: false, Slot()).DataStatus);

    [Fact]
    public void A_live_release_that_read_every_file_is_live()
        => Assert.Equal(ExerciseDataStatus.Live, Exercise(released: true, Slot()).DataStatus);

    [Fact]
    public void A_live_release_with_a_replaced_file_has_new_files()
        => Assert.Equal(ExerciseDataStatus.LiveWithNewFiles, Exercise(released: true, Slot("c2")).DataStatus);

    [Fact]
    public void An_empty_optional_slot_does_not_count_as_a_new_file()
    {
        var exercise = Exercise(released: true, Slot());
        exercise.Datasets.Add(new CheckingWindowDatasetDto { Id = Guid.NewGuid(), Name = "late", Required = false });

        Assert.Equal(ExerciseDataStatus.Live, exercise.DataStatus);
    }

    [Fact]
    public void A_retired_slot_does_not_count_as_a_new_file()
    {
        var exercise = Exercise(released: true, Slot());
        exercise.Datasets.Add(new CheckingWindowDatasetDto
        {
            Id = Guid.NewGuid(), Name = "old", Retired = true,
            IngressFile = "old.csv", IngressFileChecksum = "o", SchemaFile = "old.json", SchemaFileChecksum = "o"
        });

        Assert.Equal(ExerciseDataStatus.Live, exercise.DataStatus);
    }

    [Fact]
    public void A_validated_legacy_exercise_is_live_without_a_release()
    {
        var slot = Slot();
        var exercise = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData, UsesExerciseStorage = false,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 1),
            Datasets = [slot]
        };
        exercise.ValidatedAt = new DateTime(2026, 1, 2);
        exercise.ValidatedIngressChecksum = exercise.CurrentIngressChecksum;
        exercise.ValidatedSchemaChecksum = exercise.CurrentSchemaChecksum;

        Assert.Equal(ExerciseDataStatus.Live, exercise.DataStatus);
    }
}
