namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public enum ConflictType { SelfSubmitted, OtherSubmitted }

public sealed class DuplicateRequestException : Exception
{
    public ConflictType ConflictType { get; }
    public string ConflictingReasonType { get; }
    public bool ReasonsMatch { get; }

    public DuplicateRequestException(ConflictType conflictType)
        : base("A request for this pupil already exists for this checking window.")
    {
        ConflictType = conflictType;
        ConflictingReasonType = string.Empty;
    }

    public DuplicateRequestException(ConflictType conflictType, string conflictingReasonType, bool reasonsMatch)
        : base("A request for this pupil already exists for this checking window.")
    {
        ConflictType = conflictType;
        ConflictingReasonType = conflictingReasonType;
        ReasonsMatch = reasonsMatch;
    }
}
