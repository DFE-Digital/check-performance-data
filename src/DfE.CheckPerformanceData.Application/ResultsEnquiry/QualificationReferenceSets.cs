using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>Which qualification reference documents a results enquiry reads, by window type.</summary>
public static class QualificationReferenceSets
{
    private static readonly IReadOnlyList<QualificationReferenceSet> Post16Only =
        [QualificationReferenceSet.Post16];

    private static readonly IReadOnlyList<QualificationReferenceSet> Ks4ThenPost16 =
        [QualificationReferenceSet.Ks4, QualificationReferenceSet.Post16];

    /// <summary>The document whose qualifications a school can select as a missing qualification.</summary>
    public static QualificationReferenceSet ForSelection(CheckingWindowType? windowType) =>
        windowType == CheckingWindowType.KS4Autumn ? QualificationReferenceSet.Ks4 : QualificationReferenceSet.Post16;

    /// <summary>The documents that describe a result a learner holds, in the order to search them.</summary>
    public static IReadOnlyList<QualificationReferenceSet> ForHeldResult(CheckingWindowType? windowType) =>
        windowType == CheckingWindowType.KS4Autumn ? Ks4ThenPost16 : Post16Only;
}
