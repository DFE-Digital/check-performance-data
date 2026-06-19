using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The submissions picker: a paged, newest-first list of distinct references that entered the
// pipeline, each selectable with a checkbox. Selecting rows and pressing Play opens the
// walkthrough for those references. Paged by Wiki:PageLength; a date/time filter (defaulting to a
// recent window) narrows the list.
public sealed class SubmissionsViewModel
{
    public IReadOnlyList<SubmissionRow> Rows { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

// One selected reference's stage progression: the ordered board-stage keys it actually visited,
// computed from its recorded events. The walkthrough single-steps the cohort by advancing each
// item's position along its own StageKeys, highlighting the active stage at each step.
public sealed class WalkthroughItem
{
    public required string ReferenceNumber { get; init; }
    public required IReadOnlyList<string> StageKeys { get; init; }
    public string? LatestDecision { get; init; }
}

// The interactive replay walkthrough: the five board stages plus the selected references' stage
// progressions, so the page can follow the same chosen items across the stages and single-step
// them. Built from the references' real recorded events (reusing the journey read).
public sealed class WalkthroughViewModel
{
    public IReadOnlyList<WalkthroughItem> Items { get; init; } = [];
}
