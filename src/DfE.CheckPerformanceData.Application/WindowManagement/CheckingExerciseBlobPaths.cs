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
