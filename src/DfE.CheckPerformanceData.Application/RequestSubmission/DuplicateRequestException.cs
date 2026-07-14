namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public enum ConflictType { SelfSubmitted, OtherSubmitted }

public sealed class DuplicateRequestException(ConflictType conflictType)
    : Exception("A request for this pupil already exists for this checking window.")
{
    public ConflictType ConflictType { get; } = conflictType;
}
