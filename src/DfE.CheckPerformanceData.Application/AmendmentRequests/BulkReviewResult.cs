using DfE.CheckPerformanceData.Domain.Enums;

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

/// <summary>
/// Outcome of a bulk submit: which references were submitted and which were skipped — as
/// duplicates, as drafts that could not be resumed, or because their checking exercise has closed.
/// </summary>
public sealed class BulkSubmissionResult
{
    public required IReadOnlyList<string> Submitted { get; init; }
    public required IReadOnlyList<string> Skipped { get; init; }

    /// <summary>
    /// AB#301022: set when at least one draft was skipped because its checking exercise is no
    /// longer open — the first such exercise. Null when nothing was skipped for that reason. It is
    /// what lets the caller tell a school why nothing was submitted.
    /// </summary>
    public CheckingExerciseType? ClosedExercise { get; init; }
}
