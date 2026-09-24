using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #316: each checking exercise owns a prefix inside the window's container, so one exercise's
// ingress run — and its clear sweep — can never destroy another's output. These assertions pin the
// exact strings, because a change to any of them orphans blobs that are already written.
public sealed class CheckingExerciseBlobPathsTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    [Fact]
    public void Pupil_data_keeps_the_bare_data_prefix_so_no_blob_has_to_move()
    {
        Assert.Equal("data/", CheckingExerciseBlobPaths.DataPrefix(CheckingExerciseType.PupilData));
    }

    [Fact]
    public void Results_enquiry_keeps_the_prefix_it_already_writes_to()
    {
        Assert.Equal("results-enquiry/data/",
            CheckingExerciseBlobPaths.DataPrefix(CheckingExerciseType.ResultsEnquiry));
    }

    [Fact]
    public void The_prefix_is_a_kebab_case_slug_not_the_enums_ToString()
    {
        // $"{exercise}/data/" would emit "ResultsEnquiry/data/" and orphan every results blob
        // already written.
        Assert.DoesNotContain(nameof(CheckingExerciseType.ResultsEnquiry),
            CheckingExerciseBlobPaths.DataPrefix(CheckingExerciseType.ResultsEnquiry),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_unmapped_exercise_type_fails_loudly_rather_than_sharing_another_prefix()
    {
        // There is no default case. Silently sharing a prefix is the failure this ticket exists
        // to prevent, so a new enum member must throw until it is given a prefix.
        var unmapped = (CheckingExerciseType)999;

        Assert.Throws<ArgumentOutOfRangeException>(() => CheckingExerciseBlobPaths.DataPrefix(unmapped));
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData)]
    [InlineData(CheckingExerciseType.ResultsEnquiry)]
    public void No_exercises_data_prefix_is_a_prefix_of_anothers(CheckingExerciseType exercise)
    {
        // Blob prefixes match as plain strings, so a sweep of one prefix must not reach another's
        // blobs. "data/" is not a prefix of "results-enquiry/data/", which is what makes the bare
        // pupil-data prefix safe to keep.
        var others = Enum.GetValues<CheckingExerciseType>().Where(e => e != exercise);

        foreach (var other in others)
        {
            Assert.False(
                CheckingExerciseBlobPaths.DataPrefix(other)
                    .StartsWith(CheckingExerciseBlobPaths.DataPrefix(exercise), StringComparison.Ordinal),
                $"{other} blobs sit under the {exercise} prefix, so an {exercise} sweep would delete them.");
        }
    }

    [Fact]
    public void The_summary_prefix_is_exercise_scoped_so_one_run_cannot_delete_anothers_summaries()
    {
        Assert.NotEqual(
            CheckingExerciseBlobPaths.SummaryPrefix(CheckingExerciseType.PupilData, WindowId),
            CheckingExerciseBlobPaths.SummaryPrefix(CheckingExerciseType.ResultsEnquiry, WindowId));
    }

    [Fact]
    public void Pupil_datas_summary_and_error_log_stay_at_the_container_root()
    {
        // Unchanged paths: an already-ingested window's summaries and error log are still found.
        Assert.Equal($"{WindowId}_summary_",
            CheckingExerciseBlobPaths.SummaryPrefix(CheckingExerciseType.PupilData, WindowId));
        Assert.Equal($"{WindowId}_error_log.txt",
            CheckingExerciseBlobPaths.ErrorLogBlobName(CheckingExerciseType.PupilData, WindowId));
    }

    [Fact]
    public void The_error_log_is_exercise_scoped_too()
    {
        Assert.Equal($"results-enquiry/{WindowId}_error_log.txt",
            CheckingExerciseBlobPaths.ErrorLogBlobName(CheckingExerciseType.ResultsEnquiry, WindowId));
    }

    [Fact]
    public void A_pupil_blob_is_still_named_exactly_as_it_is_today()
    {
        Assert.Equal("data/9334290_pupils.json",
            CheckingExerciseBlobPaths.PupilsBlobName(CheckingExerciseType.PupilData, "933/4290"));
    }

    [Fact]
    public void A_results_blob_is_still_named_exactly_as_it_is_today()
    {
        Assert.Equal("results-enquiry/data/9334070_results.json",
            CheckingExerciseBlobPaths.ResultsBlobName("933/4070"));
    }

    // #324: an ingress run asks for its output name by exercise, so the two naming rules below are
    // chosen deliberately rather than by whichever method the caller reached for.
    [Fact]
    public void An_ingress_run_writes_the_pupil_name_for_pupil_data()
    {
        Assert.Equal("data/9334290_pupils.json",
            CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.PupilData, "933/4290"));
    }

    [Fact]
    public void An_ingress_run_writes_the_results_name_for_a_results_enquiry()
    {
        // The name the results reader looks for. A results run that wrote the pupil-data name would
        // produce files the enquiry journey cannot find.
        Assert.Equal("results-enquiry/data/9334070_results.json",
            CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.ResultsEnquiry, "933/4070"));
    }

    [Fact]
    public void The_two_names_normalise_a_laestab_differently_and_that_is_the_point()
    {
        // Pupil data strips only the slash, because it has to keep finding blobs already written
        // from a verbatim supplier LAESTAB; results runs LaestabNormaliser, which is what turns a
        // DfE Sign-in claim into a blob name.
        Assert.Equal("data/933 4290_pupils.json",
            CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.PupilData, "933 /4290"));
        Assert.Equal("results-enquiry/data/9334070_results.json",
            CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.ResultsEnquiry, "933 /4070"));
    }

    [Fact]
    public void An_unmapped_exercise_type_has_no_output_name_either()
    {
        var unmapped = (CheckingExerciseType)999;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CheckingExerciseBlobPaths.DataBlobName(unmapped, "933/4070"));
    }

    [Fact]
    public void DataBlobName_ByExerciseId_NamesTheFileByDataType()
    {
        var exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var name = CheckingExerciseBlobPaths.DataBlobName(exerciseId, CheckingDataType.Pupil, "933/4290");

        Assert.Equal("exercises/11111111-1111-1111-1111-111111111111/data/9334290_pupils.json", name);
    }

    [Fact]
    public void DataBlobName_ByExerciseId_KeepsOtherDataUnderItsOwnSuffix()
    {
        var exerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var name = CheckingExerciseBlobPaths.DataBlobName(exerciseId, CheckingDataType.Other, "933/4290");

        Assert.Equal("exercises/22222222-2222-2222-2222-222222222222/data/9334290_data.json", name);
    }

    [Fact]
    public void DataBlobName_ForARelease_IsUnderThatReleasesOwnPrefix()
    {
        // A run writes a new release beside the live one, never over it.
        var exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var releaseId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var name = CheckingExerciseBlobPaths.DataBlobName(exerciseId, CheckingDataType.Results, "933/4070", releaseId);

        Assert.Equal(
            "exercises/11111111-1111-1111-1111-111111111111/releases/99999999-9999-9999-9999-999999999999/data/9334070_results.json",
            name);
    }

    [Fact]
    public void DataPrefix_WithNoRelease_IsTheUnversionedPrefix()
    {
        // Output written before releases existed, and by the dev seeders, is still found.
        var exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal("exercises/11111111-1111-1111-1111-111111111111/data/",
            CheckingExerciseBlobPaths.DataPrefix(exerciseId, null));
    }

    [Fact]
    public void TwoReleasePrefixes_NeverContainEachOther()
    {
        // A prefix sweep of one release must not reach another, or the unversioned output.
        var exerciseId = Guid.NewGuid();
        var first = CheckingExerciseBlobPaths.ReleaseDataPrefix(exerciseId, Guid.NewGuid());
        var second = CheckingExerciseBlobPaths.ReleaseDataPrefix(exerciseId, Guid.NewGuid());
        var unversioned = CheckingExerciseBlobPaths.DataPrefix(exerciseId);

        Assert.False(first.StartsWith(second, StringComparison.Ordinal));
        Assert.False(second.StartsWith(first, StringComparison.Ordinal));
        Assert.False(first.StartsWith(unversioned, StringComparison.Ordinal));
        Assert.False(unversioned.StartsWith(first, StringComparison.Ordinal));
    }

    [Fact]
    public void DatasetBlobName_IsUnderTheReleaseButOutsideItsMergedDataPrefix()
    {
        // The journeys list the merged data/ prefix. A dataset's own file must never appear there.
        var exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var releaseId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var datasetId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var name = CheckingExerciseBlobPaths.DatasetBlobName(exerciseId, releaseId, datasetId, "933/4290");

        Assert.Equal(
            "exercises/11111111-1111-1111-1111-111111111111/releases/99999999-9999-9999-9999-999999999999/datasets/77777777-7777-7777-7777-777777777777/9334290.json",
            name);
        Assert.False(name.StartsWith(CheckingExerciseBlobPaths.ReleaseDataPrefix(exerciseId, releaseId), StringComparison.Ordinal));
    }

    [Fact]
    public void DefinitionFile_WithAChecksum_KeepsAnEarlierUploadOfTheSameName()
    {
        var exerciseId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var definitionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var first = CheckingExerciseBlobPaths.DefinitionFile(exerciseId, definitionId, "ABCDEF0123456789FFFF", "main.csv");
        var second = CheckingExerciseBlobPaths.DefinitionFile(exerciseId, definitionId, "0000000000000000FFFF", "main.csv");

        Assert.Equal(
            "ingress/44444444-4444-4444-4444-444444444444/55555555-5555-5555-5555-555555555555/abcdef0123456789/main.csv",
            first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DefinitionFile_WithNoChecksum_FallsBackToTheFileName()
        => Assert.Equal(
            CheckingExerciseBlobPaths.DefinitionFile(Guid.Empty, Guid.Empty, "main.csv"),
            CheckingExerciseBlobPaths.DefinitionFile(Guid.Empty, Guid.Empty, "", "main.csv"));

    [Fact]
    public void LogPrefix_IsScopedToTheExercise()
    {
        var exerciseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        Assert.Equal("exercises/33333333-3333-3333-3333-333333333333/logs/",
            CheckingExerciseBlobPaths.LogPrefix(exerciseId));
    }

    [Fact]
    public void DefinitionFile_KeepsOnlyTheFileName()
    {
        var exerciseId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var definitionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var name = CheckingExerciseBlobPaths.DefinitionFile(exerciseId, definitionId, "c:\\upload\\main.csv");

        Assert.Equal("ingress/44444444-4444-4444-4444-444444444444/55555555-5555-5555-5555-555555555555/main.csv", name);
    }

    [Theory]
    [InlineData("main.csv", "ingress/main.csv")]
    [InlineData("ingress/abc/def/main.csv", "ingress/abc/def/main.csv")]
    public void IngressBlobName_KeepsLegacyRelativePathsReadable(string stored, string expected)
        => Assert.Equal(expected, CheckingExerciseBlobPaths.IngressBlobName(stored));

    [Theory]
    [InlineData("main.json", "schema/main.json")]
    [InlineData("ingress/abc/def/main.json", "ingress/abc/def/main.json")]
    public void SchemaBlobName_KeepsLegacyRelativePathsReadable(string stored, string expected)
        => Assert.Equal(expected, CheckingExerciseBlobPaths.SchemaBlobName(stored));

    [Fact]
    public void DefaultDataType_HasNoDefaultCase()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            CheckingExerciseBlobPaths.DefaultDataType((CheckingExerciseType)999));

    [Fact]
    public void DefaultDataType_MapsAnUntypedExerciseToOther()
        => Assert.Equal(CheckingDataType.Other, CheckingExerciseBlobPaths.DefaultDataType(null));
}
