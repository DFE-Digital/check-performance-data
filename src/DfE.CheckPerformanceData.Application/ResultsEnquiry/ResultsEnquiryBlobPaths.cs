using DfE.CheckPerformanceData.Application.Dashboard;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Every blob path segment the results-enquiry feature uses, in one place. The lead developer's
/// 16-19 window-model note warns the checking-exercise naming may change, so a rename must be
/// mechanical.
///
/// Layout: container <c>{windowId}</c> (the existing per-window container), blob
/// <c>results-enquiry/data/{laestab}_results.json</c> — one merged array per school across all six
/// source files. The <c>results-enquiry/</c> prefix is deliberate: per consequence #2 of
/// docs/16-19-window-model.md each checking exercise owns its own blob prefix, so when ingress
/// becomes per-exercise no blob migration is needed and one exercise's sweep cannot destroy
/// another's output. Pupil-data checking keeps its existing bare <c>data/</c> prefix.
/// </summary>
public static class ResultsEnquiryBlobPaths
{
    public const string ResultsPrefix = "results-enquiry/data/";
    public const string ResultsSuffix = "_results.json";

    /// <summary>The grade-reference blob, seeded alongside <c>rules.json</c> in the rules-config container.</summary>
    public const string GradeReferenceBlobName = "grade-reference.json";

    /// <summary>e.g. "933/4070" -> "results-enquiry/data/9334070_results.json".</summary>
    public static string ResultsBlobName(string laestab)
        => $"{ResultsPrefix}{LaestabNormaliser.Normalise(laestab)}{ResultsSuffix}";
}
