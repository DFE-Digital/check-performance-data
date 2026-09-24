using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The blob layout of a checking window's container, in one place (#316). Every exercise owns its
/// own prefix inside the existing <c>{windowId}</c> container, so one exercise's ingress run — and
/// in particular its clear sweep — can never destroy another's output. One exercise's data must
/// never have to be re-uploaded to correct another's.
/// </summary>
/// <remarks>
/// Two things here are deliberate and must survive a tidy-up:
/// <list type="bullet">
/// <item>The prefix is a kebab-case slug, never the enum's <c>ToString()</c>. <c>$"{exercise}/"</c>
/// would emit <c>ResultsEnquiry/</c> and orphan every results blob already written.</item>
/// <item>Pupil data keeps the bare prefix, so its blobs stay exactly where they are and this ticket
/// needs no blob migration. Because blob prefixes match as plain strings, a <c>data/</c> sweep does
/// not reach <c>results-enquiry/data/</c> — the two are already isolated. The cost is one
/// legacy-looking row in the lookup, which is cheap next to migrating every window's blobs. Do not
/// move pupil data under a <c>pupil-data/</c> prefix without budgeting that migration.</item>
/// </list>
/// </remarks>
public static class CheckingExerciseBlobPaths
{
    // ---- Exercise-id storage (#466) -------------------------------------------------
    // An exercise is identified by its own Id, not by its type, so one window can hold several
    // releases of one activity and any number of display-only data shares. Everything below is
    // addressed by that Id. The type-based methods further down are untouched and still serve
    // every row with UsesExerciseStorage = false, which is why this ticket needs no blob migration.

    /// <summary>Where an uploaded ingress or schema file is stored for one dataset slot.</summary>
    // Path.GetFileName only recognises '\' as a separator on Windows, so an upload's original path
    // (which may be a Windows path even when this runs on Linux) is trimmed by hand rather than
    // relying on it to strip the directory.
    public static string DefinitionFile(Guid exerciseId, Guid definitionId, string filename)
        => $"ingress/{exerciseId}/{definitionId}/{filename.Split('/', '\\')[^1]}";

    /// <summary>
    /// Where an uploaded file is stored when its content is known. The checksum is part of the
    /// path, so a new upload with the same file name as an earlier one gets a new blob and does not
    /// overwrite it. A release records the path of each file it read, so that file must stay where
    /// it is for as long as the release does.
    /// </summary>
    public static string DefinitionFile(Guid exerciseId, Guid definitionId, string checksum, string filename)
        => string.IsNullOrEmpty(checksum)
            ? DefinitionFile(exerciseId, definitionId, filename)
            : $"ingress/{exerciseId}/{definitionId}/{checksum[..Math.Min(16, checksum.Length)].ToLowerInvariant()}/{filename.Split('/', '\\')[^1]}";

    // A dataset row stores a complete blob name once it has been uploaded through the new screens.
    // Rows written before that store a path relative to the separate ingress/ and schema/ roots.
    // Both are read without moving anything.
    public static string IngressBlobName(string storedPath)
        => storedPath.StartsWith("ingress/", StringComparison.Ordinal) ? storedPath : $"ingress/{storedPath}";

    public static string SchemaBlobName(string storedPath)
        => storedPath.StartsWith("ingress/", StringComparison.Ordinal) ? storedPath : $"schema/{storedPath}";

    /// <summary>
    /// The exercise's per-school output files. With no release, the unversioned prefix: where
    /// every run wrote before releases existed, and where the dev seeders still write.
    /// </summary>
    public static string DataPrefix(Guid exerciseId, Guid? releaseId = null)
        => releaseId is { } release ? ReleaseDataPrefix(exerciseId, release) : $"exercises/{exerciseId}/data/";

    /// <summary>
    /// The per-school output of one release. Each run writes under a new release id, so it never
    /// overwrites the output that schools see now. The switch to the new output is one change to
    /// the exercise's current release, after every file is written.
    /// </summary>
    public static string ReleaseDataPrefix(Guid exerciseId, Guid releaseId)
        => $"exercises/{exerciseId}/releases/{releaseId}/data/";

    /// <summary>
    /// One dataset's per-school output in one release, e.g.
    /// <c>exercises/{id}/releases/{release}/datasets/{dataset}/9334290.json</c>.
    /// </summary>
    /// <remarks>
    /// Each dataset has its own file, so the display never has to guess which dataset a record came
    /// from, and datasets with unrelated schemas never share a file. The merged file under
    /// <see cref="ReleaseDataPrefix"/> still exists for the journeys, and holds only the slots that
    /// feed them. The two prefixes do not overlap, so listing <c>data/</c> never finds a dataset
    /// file. The dataset id, not its name, names the folder: an admin types the name, and the id
    /// is always safe in a path.
    /// </remarks>
    public static string DatasetBlobName(Guid exerciseId, Guid releaseId, Guid datasetId, string laestab)
        => $"exercises/{exerciseId}/releases/{releaseId}/datasets/{datasetId}/{laestab.Replace("/", string.Empty)}.json";

    /// <summary>
    /// True when a release's merged file already holds exactly one dataset's records, so a
    /// per-dataset file would be a copy of it. This is an exercise with one dataset that feeds the
    /// journey, e.g. KS4 pupil data. The release then writes only the merged file, and the display
    /// reads that dataset from it.
    /// </summary>
    /// <remarks>
    /// The run and the display must use the same rule, so it is here. A release written before
    /// this rule also has the per-dataset file. That file is not read, but its content is the same.
    /// </remarks>
    public static bool MergedFileIsDatasetFile(IReadOnlyList<bool> datasetsFeedJourney)
        => datasetsFeedJourney is [true];

    /// <summary>The exercise's run summaries and error log.</summary>
    public static string LogPrefix(Guid exerciseId) => $"exercises/{exerciseId}/logs/";

    /// <summary>
    /// The data type an exercise holds when nothing says otherwise.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No default case, for the same reason as <see cref="ExercisePrefix"/>: a new exercise type
    /// must fail loudly rather than quietly share another exercise's file names.
    /// </exception>
    public static CheckingDataType DefaultDataType(CheckingExerciseType? type) => type switch
    {
        CheckingExerciseType.PupilData => CheckingDataType.Pupil,
        CheckingExerciseType.ResultsEnquiry => CheckingDataType.Results,
        null => CheckingDataType.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
            "This checking exercise has no data type. Add one to CheckingExerciseBlobPaths before ingesting it.")
    };

    /// <summary>e.g. "933/4290" -> "exercises/{id}/data/9334290_pupils.json".</summary>
    /// <remarks>
    /// The slash is stripped rather than the laestab being normalised, exactly as
    /// <see cref="PupilsBlobName"/> does, because ingress writes the supplier's LAESTAB column
    /// through verbatim and the two rules differ on any value that is not slash-separated digits.
    /// </remarks>
    public static string DataBlobName(Guid exerciseId, CheckingDataType type, string laestab, Guid? releaseId = null)
        => $"{DataPrefix(exerciseId, releaseId)}{laestab.Replace("/", string.Empty)}_{type switch
        {
            CheckingDataType.Pupil => "pupils",
            CheckingDataType.Results => "results",
            CheckingDataType.PreviouslyPublished => "previously-published",
            CheckingDataType.ValueAdded => "value-added",
            CheckingDataType.Other => "data",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "This data type has no file name.")
        }}.json";

    /// <summary>Everything an exercise writes sits under this prefix. Empty for pupil data.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The exercise has no prefix mapping. There is no default case on purpose: a new exercise type
    /// must fail loudly rather than silently share another exercise's prefix, which is the failure
    /// this whole layout exists to prevent.
    /// </exception>
    public static string ExercisePrefix(CheckingExerciseType exercise) => exercise switch
    {
        CheckingExerciseType.PupilData => string.Empty,
        CheckingExerciseType.ResultsEnquiry => "results-enquiry/",
        _ => throw new ArgumentOutOfRangeException(nameof(exercise), exercise,
            "This checking exercise has no blob prefix. Add one to CheckingExerciseBlobPaths before " +
            "ingesting it — sharing another exercise's prefix would let one run delete the other's data.")
    };

    /// <summary>Where the exercise's per-school data files live, e.g. <c>data/</c>.</summary>
    public static string DataPrefix(CheckingExerciseType exercise) => $"{ExercisePrefix(exercise)}data/";

    /// <summary>The prefix every timestamped run summary for this exercise shares.</summary>
    public static string SummaryPrefix(CheckingExerciseType exercise, Guid windowId)
        => $"{ExercisePrefix(exercise)}{windowId}_summary_";

    /// <summary>The exercise's error log. One per exercise, so two runs cannot overwrite each other.</summary>
    public static string ErrorLogBlobName(CheckingExerciseType exercise, Guid windowId)
        => $"{ExercisePrefix(exercise)}{windowId}_error_log.txt";

    public const string PupilsSuffix = "_pupils.json";

    /// <summary>e.g. "933/4290" -> "data/9334290_pupils.json".</summary>
    /// <remarks>
    /// The slash is stripped rather than the laestab being run through
    /// <see cref="LaestabNormaliser"/>: ingress writes the supplier's LAESTAB column through
    /// verbatim, and the two differ on any value that is not slash-separated digits. Keeping the
    /// weaker rule is what guarantees every pupil blob already written is still found.
    /// </remarks>
    public static string PupilsBlobName(CheckingExerciseType exercise, string laestab)
        => $"{DataPrefix(exercise)}{laestab.Replace("/", string.Empty)}{PupilsSuffix}";

    public const string ResultsSuffix = "_results.json";

    /// <summary>e.g. "933/4070" -> "results-enquiry/data/9334070_results.json".</summary>
    public static string ResultsBlobName(string laestab)
        => $"{DataPrefix(CheckingExerciseType.ResultsEnquiry)}{LaestabNormaliser.Normalise(laestab)}{ResultsSuffix}";

    /// <summary>
    /// The per-school output file an ingress run writes for this exercise (#324).
    /// </summary>
    /// <remarks>
    /// The two names normalise the laestab differently and the difference is deliberate, so the
    /// choice has to be made here rather than left to whichever name a caller reached for.
    /// <see cref="PupilsBlobName"/> only strips the slash, because it has to keep finding every
    /// pupil blob already written from a verbatim supplier LAESTAB; <see cref="ResultsBlobName"/>
    /// runs <see cref="LaestabNormaliser"/>, which is what the results reader uses to turn a DfE
    /// Sign-in claim into a blob name. A results run that wrote the pupil-data name would produce
    /// files the enquiry journey cannot find.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The exercise has no output name. No default case, for the same reason as
    /// <see cref="ExercisePrefix"/>.
    /// </exception>
    public static string DataBlobName(CheckingExerciseType exercise, string laestab) => exercise switch
    {
        CheckingExerciseType.PupilData => PupilsBlobName(exercise, laestab),
        CheckingExerciseType.ResultsEnquiry => ResultsBlobName(laestab),
        _ => throw new ArgumentOutOfRangeException(nameof(exercise), exercise,
            "This checking exercise has no per-school output blob name. Add one to " +
            "CheckingExerciseBlobPaths before ingesting it.")
    };
}
