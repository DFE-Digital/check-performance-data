namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public enum ConflictType { SelfSubmitted, OtherSubmitted }

public sealed class DuplicateRequestException : Exception
{
    public ConflictType ConflictType { get; }
    public string ConflictingReasonType { get; }
    public string ConflictingRequestCategory { get; }
    public string ConflictingUserName { get; }
    public bool ReasonsMatch { get; }

    public DuplicateRequestException(ConflictType conflictType)
        : base("A request for this pupil already exists for this checking window.")
    {
        ConflictType = conflictType;
        ConflictingReasonType = string.Empty;
        ConflictingRequestCategory = string.Empty;
        ConflictingUserName = string.Empty;
    }

    public DuplicateRequestException(ConflictType conflictType, string conflictingReasonType, string conflictingRequestCategory, string conflictingUserName, bool reasonsMatch)
        : base("A request for this pupil already exists for this checking window.")
    {
        ConflictType = conflictType;
        ConflictingReasonType = conflictingReasonType;
        ConflictingRequestCategory = conflictingRequestCategory;
        ConflictingUserName = conflictingUserName;
        ReasonsMatch = reasonsMatch;
    }
}
