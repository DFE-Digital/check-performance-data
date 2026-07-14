namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>One selected request as classified for the bulk review page.</summary>
public sealed class BulkReviewItem
{
    public required string ReferenceNumber { get; init; }
    public required string PupilName { get; init; }
    public required string RequestTypeDescription { get; init; }
    /// <summary>Set only for duplicates; explains why the item is excluded from submission.</summary>
    public string? DuplicateReason { get; init; }
}

/// <summary>The classified selection shown on the bulk review page.</summary>
public sealed class BulkReviewResult
{
    public required IReadOnlyList<BulkReviewItem> Submittable { get; init; }
    public required IReadOnlyList<BulkReviewItem> Duplicates { get; init; }
}

/// <summary>Outcome of a bulk submit: which references were submitted and which were skipped as duplicates.</summary>
public sealed class BulkSubmissionResult
{
    public required IReadOnlyList<string> Submitted { get; init; }
    public required IReadOnlyList<string> Skipped { get; init; }
}
