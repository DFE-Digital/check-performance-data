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
/// results-enquiry flavour of those paths, plus the grade-reference blob, which lives in the
/// rules-config container and is not part of a window's layout at all.
/// </summary>
public static class ResultsEnquiryBlobPaths
{
    public static string ResultsPrefix => CheckingExerciseBlobPaths.DataPrefix(CheckingExerciseType.ResultsEnquiry);
    public const string ResultsSuffix = CheckingExerciseBlobPaths.ResultsSuffix;

    /// <summary>The grade-reference blob, seeded alongside <c>rules.json</c> in the rules-config container.</summary>
    public const string GradeReferenceBlobName = "grade-reference.json";

    /// <summary>The QualList qualification reference blob (AB#297848), beside the grade reference.</summary>
    public const string QualificationReferenceBlobName = "qualification-reference.json";

    /// <summary>e.g. "933/4070" -> "results-enquiry/data/9334070_results.json".</summary>
    public static string ResultsBlobName(string laestab)
        => CheckingExerciseBlobPaths.ResultsBlobName(laestab);
}
