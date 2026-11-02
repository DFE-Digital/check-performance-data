namespace DfE.CheckPerformanceData.Application.ExerciseDecisions;

/// <summary>
/// The admin per-exercise decision pull (#746): read the decision Zendesk holds for each of the
/// exercise's submitted requests and store the final ones. Refuses before writing anything when
/// Zendesk cannot be read; a per-request outcome is never a failure of the pull.
/// </summary>
public interface IExerciseDecisionPullService
{
    /// <summary>The counts the confirm page shows before anything is read.</summary>
    Task<ExerciseDecisionPullPreview> PreviewAsync(Guid windowId, Guid exerciseId, CancellationToken ct);

    /// <summary>
    /// Reads and stores. <see cref="ExerciseDecisionPullResult.FailureReason"/> is set when Zendesk
    /// could not be read, in which case nothing was written (all-or-nothing per pull).
    /// </summary>
    Task<ExerciseDecisionPullResult> PullAsync(Guid windowId, Guid exerciseId, CancellationToken ct);
}

/// <summary>What the confirm page shows: how many requests the pull will check.</summary>
public sealed record ExerciseDecisionPullPreview(int SubmittedTotal, int WithTicket);

/// <summary>
/// The pull's run summary: five buckets that always add up to the submitted total. A request is
/// counted once, in the first matching bucket: no ticket id, then ticket not found (or no
/// readable decision), then a non-final decision, then the final decision it carries.
/// </summary>
public sealed record ExerciseDecisionPullResult
{
    public required int Accepted { get; init; }
    public required int Rejected { get; init; }
    public required int Unchanged { get; init; }
    public required int NoTicket { get; init; }
    public required int TicketNotFound { get; init; }

    public int SubmittedTotal => Accepted + Rejected + Unchanged + NoTicket + TicketNotFound;

    /// <summary>The source's message when Zendesk could not be read; null on a completed pull.</summary>
    public string? FailureReason { get; init; }
}
