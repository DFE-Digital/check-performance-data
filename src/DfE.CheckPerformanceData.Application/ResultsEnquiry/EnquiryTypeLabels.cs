using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The name of each results enquiry type, as a school reads it. The issue page, the summary and
/// the stored description of a submitted enquiry use it.
/// </summary>
public static class EnquiryTypeLabels
{
    private const string DescriptionPrefix = "Results enquiry - ";

    /// <summary>The name of an enquiry type. A change type that is not a results enquiry throws.</summary>
    public static string For(WhatToChange type, LearnerNoun noun) => type switch
    {
        WhatToChange.IncorrectGrade => "Incorrect grade",
        WhatToChange.MissingQualification => "Missing qualification",
        WhatToChange.ResultDoesNotBelong => $"Result does not belong to {noun.Singular}",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "This change type is not a results enquiry.")
    };

    /// <summary>The stored description of a submitted enquiry, for example "Results enquiry - Incorrect grade".</summary>
    public static string DescriptionFor(WhatToChange type, LearnerNoun noun) =>
        $"{DescriptionPrefix}{For(type, noun)}";
}
