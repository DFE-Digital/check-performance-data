using System.Globalization;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>The error a school sees when it already has an enquiry for the same student and subject.</summary>
public static class DuplicateEnquiryMessage
{
    private const string AboutAResult =
        "You have already made an enquiry about this result. Your reference is {0}";

    private const string AboutAQualification =
        "You have already made an enquiry about this qualification. Your reference is {0}";

    public static string For(EnquirySubject subject, string existingReferenceNumber) =>
        string.Format(
            CultureInfo.InvariantCulture,
            subject.IsHeldResult ? AboutAResult : AboutAQualification,
            existingReferenceNumber);
}
