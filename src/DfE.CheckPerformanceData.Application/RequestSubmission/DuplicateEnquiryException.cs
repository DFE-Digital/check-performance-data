namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>The school already has a submitted results enquiry for the same student and subject.</summary>
public sealed class DuplicateEnquiryException(string existingReferenceNumber)
    : Exception("A results enquiry for this student and subject already exists for this checking window.")
{
    public string ExistingReferenceNumber { get; } = existingReferenceNumber;
}
