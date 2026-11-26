using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Every blob path segment the results-enquiry feature uses, in one place. The lead developer's
/// 16-19 window-model note warns the checking-exercise naming may change, so a rename must be
/// mechanical.
///
/// Layout: container <c>{windowId}</c> (the existing per-window container), blob
/// <c>results-enquiry/data/{laestab}_results.json</c> — one merged array per school across all six
/// source files.
///
/// The layout itself is no longer described here. #316 moved it to
/// <see cref="CheckingExerciseBlobPaths"/>, which every exercise's reader and writer shares, so the
/// prefix cannot be changed in one place and missed in another. What is left here is the
/// results-enquiry flavour of those paths, plus the qualification-reference blob, which lives in the
/// rules-config container and is not part of a window's layout at all.
/// </summary>
public static class ResultsEnquiryBlobPaths
{
    public static string ResultsPrefix => CheckingExerciseBlobPaths.DataPrefix(CheckingExerciseType.ResultsEnquiry);
    public const string ResultsSuffix = CheckingExerciseBlobPaths.ResultsSuffix;

    /// <summary>The QualList 16-19 qualification reference blob (AB#297848), beside rules.json in the rules-config container.</summary>
    public const string QualificationReferenceBlobName = "qualification-reference.json";

    /// <summary>The key stage 4 qualification reference blob, beside the 16-19 one.</summary>
    public const string Ks4QualificationReferenceBlobName = "qualification-reference-ks4.json";

    /// <summary>The blob name of a qualification reference document. Its bundled seed file has the same name.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined set.</exception>
    public static string QualificationReferenceBlobNameFor(QualificationReferenceSet set) => set switch
    {
        QualificationReferenceSet.Post16 => QualificationReferenceBlobName,
        QualificationReferenceSet.Ks4 => Ks4QualificationReferenceBlobName,
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, "No blob is defined for this qualification reference.")
    };

    /// <summary>e.g. "933/4070" -> "results-enquiry/data/9334070_results.json".</summary>
    public static string ResultsBlobName(string laestab)
        => CheckingExerciseBlobPaths.ResultsBlobName(laestab);
}
