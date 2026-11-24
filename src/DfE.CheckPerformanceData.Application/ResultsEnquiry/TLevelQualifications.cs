namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The T Level qualifications that DCI gave for this year, by QAN. The results enquiry ticket sets
/// "T Level Exams (CYPMD)" from this list (#859). The list is <c>Data/t-level-qans.csv</c>, an
/// embedded resource. When DCI updates the list, update the CSV file.
/// </summary>
public static class TLevelQualifications
{
    private const string ResourceSuffix = "ResultsEnquiry.Data.t-level-qans.csv";

    private static readonly Lazy<IReadOnlySet<string>> Qans = new(() => QanListResource.Load(ResourceSuffix));

    /// <summary>True when the QAN is on the T Level list. The test ignores case and spaces.</summary>
    public static bool Contains(string? qan) =>
        !string.IsNullOrWhiteSpace(qan) && Qans.Value.Contains(qan.Trim());

    /// <summary>Every QAN on the list.</summary>
    public static IReadOnlySet<string> All => Qans.Value;
}
