namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public abstract record DuplicateCheckResult
{
    public sealed record NoConflict : DuplicateCheckResult;

    public sealed record SelfSubmitted(string ReferenceNumber) : DuplicateCheckResult;

    public sealed record OtherSubmitted(string ReferenceNumber) : DuplicateCheckResult;
}
