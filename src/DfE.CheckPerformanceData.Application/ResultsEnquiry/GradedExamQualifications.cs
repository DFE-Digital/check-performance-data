using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The KS4 graded exam qualifications, by QAN (#899). The list is
/// <c>Data/graded-exam-qans.csv</c>, an embedded resource. When DCI updates the list, update the
/// CSV file.
/// </summary>
public static class GradedExamQualifications
{
    private const string ResourceSuffix = "ResultsEnquiry.Data.graded-exam-qans.csv";

    private static readonly Lazy<IReadOnlySet<string>> Qans = new(() => QanListResource.Load(ResourceSuffix));

    /// <summary>True when the QAN is on the graded exam list. The check ignores case and spaces around the QAN.</summary>
    public static bool Contains(string? qan) =>
        !string.IsNullOrWhiteSpace(qan) && Qans.Value.Contains(qan.Trim());

    /// <summary>Every QAN on the list.</summary>
    public static IReadOnlySet<string> All => Qans.Value;

    /// <summary>
    /// The graded exam answer for a results enquiry in a window of this type. A KS4 Autumn enquiry
    /// has an answer: true when the QAN is on the list, else false. Every other window type has
    /// no answer (null).
    /// </summary>
    public static bool? ForEnquiry(CheckingWindowType? windowType, string? qan) =>
        windowType == CheckingWindowType.KS4Autumn ? Contains(qan) : null;
}
