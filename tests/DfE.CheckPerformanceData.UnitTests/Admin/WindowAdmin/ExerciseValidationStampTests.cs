using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319: the validation stamp moved from the window down to the exercise, and gained a meaning it
// never had. The old window-level stamp was written unconditionally on every create and update, so
// it recorded nothing; this one is taken over the exercise's dataset checksums, so it can be shown
// to be stale once a file behind it is replaced.
public class ExerciseValidationStampTests
{
    [Fact]
    public void An_exercise_that_has_never_validated_is_not_validated()
    {
        Assert.False(Exercise().IsValidated);
    }

    [Fact]
    public void An_exercise_stamped_over_its_current_files_is_validated()
    {
        CheckingExerciseDto exercise = Exercise();
        Stamp(exercise);

        Assert.True(exercise.IsValidated);
    }

    [Fact]
    public void Replacing_an_ingress_file_makes_the_stamp_stale()
    {
        CheckingExerciseDto exercise = Exercise();
        Stamp(exercise);

        exercise.Datasets[0].IngressFileChecksum = "A-DIFFERENT-INGRESS-CHECKSUM";

        Assert.False(exercise.IsValidated);
        Assert.NotNull(exercise.ValidatedAt);
    }

    [Fact]
    public void Replacing_a_schema_file_makes_the_stamp_stale()
    {
        CheckingExerciseDto exercise = Exercise();
        Stamp(exercise);

        exercise.Datasets[0].SchemaFileChecksum = "A-DIFFERENT-SCHEMA-CHECKSUM";

        Assert.False(exercise.IsValidated);
    }

    [Fact]
    public void Adding_a_dataset_makes_the_stamp_stale()
    {
        // The stamp covers the whole exercise, because the datasets ingest in one run. A second
        // file appearing means the run that produced the stamp did not read everything.
        CheckingExerciseDto exercise = Exercise();
        Stamp(exercise);

        exercise.Datasets.Add(new CheckingWindowDatasetDto
        {
            Name = "nonincluded", SortOrder = 1,
            IngressFileChecksum = "N1", SchemaFileChecksum = "N2"
        });

        Assert.False(exercise.IsValidated);
    }

    [Fact]
    public void The_combined_checksum_fits_the_column_however_many_datasets_there_are()
    {
        // Six datasets is the results-enquiry shape. Joining SHA-256 hex values would overflow the
        // 256-character column, which is why the parts are hashed rather than concatenated.
        CheckingExerciseDto exercise = new()
        {
            ExerciseType = CheckingExerciseType.ResultsEnquiry,
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 6, 1),
            Datasets = Enumerable.Range(0, 6)
                .Select(i => new CheckingWindowDatasetDto
                {
                    Name = $"file{i}",
                    SortOrder = i,
                    IngressFileChecksum = new string('A', 64),
                    SchemaFileChecksum = new string('B', 64)
                })
                .ToList()
        };

        Assert.True(exercise.CurrentIngressChecksum.Length <= 256);
        Assert.True(exercise.CurrentSchemaChecksum.Length <= 256);
    }

    [Fact]
    public void An_exercise_with_no_datasets_has_no_required_files_to_supply()
    {
        CheckingExerciseDto exercise = Exercise();
        exercise.Datasets = [];

        Assert.False(exercise.HasRequiredFiles);
    }

    [Fact]
    public void An_exercise_missing_one_of_a_datasets_two_files_is_not_ready()
    {
        CheckingExerciseDto exercise = Exercise();
        exercise.Datasets[0].SchemaFile = string.Empty;

        Assert.False(exercise.HasRequiredFiles);
    }

    private static void Stamp(CheckingExerciseDto exercise)
    {
        exercise.ValidatedAt = new DateTime(2027, 1, 2, 9, 0, 0);
        exercise.ValidatedIngressChecksum = exercise.CurrentIngressChecksum;
        exercise.ValidatedSchemaChecksum = exercise.CurrentSchemaChecksum;
    }

    private static CheckingExerciseDto Exercise() => new()
    {
        ExerciseType = CheckingExerciseType.PupilData,
        StartDate = new DateTime(2027, 1, 1),
        EndDate = new DateTime(2027, 1, 14),
        Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "pupils",
                SortOrder = 0,
                IngressFile = "pupils.csv",
                IngressFileChecksum = "INGRESS-1",
                SchemaFile = "pupils.json",
                SchemaFileChecksum = "SCHEMA-1"
            }
        ]
    };
}
