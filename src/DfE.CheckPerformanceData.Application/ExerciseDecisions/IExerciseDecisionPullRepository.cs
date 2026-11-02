using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ExerciseDecisions;

/// <summary>
/// The storage side of the decision pull (#746): the exercise's submitted rows in, the final
/// decisions out. The save updates the rows and writes the deliberate WindowAdmin audit row in
/// one transaction — the two commit or do not commit together.
/// </summary>
public interface IExerciseDecisionPullRepository
{
    /// <summary>
    /// The window's rows for the exercise with Status = Submitted, across schools and any request
    /// type. A row with no CrmId is returned as-is: "no ticket" is a result, not a filter.
    /// </summary>
    Task<IReadOnlyList<ExerciseDecisionPullRow>> GetSubmittedRowsAsync(Guid windowId, Guid exerciseId, CancellationToken ct);

    /// <summary>
    /// Stores the given decisions (decision and reason together) and writes the audit row. Returns
    /// how many rows were updated.
    /// </summary>
    Task<int> SaveDecisionsAsync(IReadOnlyList<SavedDecision> saved, DecisionsPulledAudit audit, CancellationToken ct);
}

/// <summary>One submitted row the pull reads. Ids and counts only — no names or dates of birth.</summary>
public sealed record ExerciseDecisionPullRow(
    Guid ChangeRequestId,
    string ReferenceNumber,
    string? CrmId,
    ZendeskDecision? ZendeskDecision,
    string? ZendeskDecisionReason);

/// <summary>A final decision to store. A null reason clears the stored reason (latest wins).</summary>
public sealed record SavedDecision(Guid ChangeRequestId, ZendeskDecision Decision, string? Reason);

/// <summary>
/// The deliberate WindowAdmin / DecisionsPulled audit row one pull writes: who, what, when and
/// the bucket counts. No personal data.
/// </summary>
public sealed record DecisionsPulledAudit
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required int Accepted { get; init; }
    public required int Rejected { get; init; }
    public required int Unchanged { get; init; }
    public required int NoTicket { get; init; }
    public required int TicketNotFound { get; init; }

    public int SubmittedTotal => Accepted + Rejected + Unchanged + NoTicket + TicketNotFound;

    /// <summary>The instant of the pull in UTC — the audit row's Timestamp.</summary>
    public required DateTime RanAtUtc { get; init; }

    /// <summary>The signed-in admin's user id; never null for this action.</summary>
    public required string UserId { get; init; }
}
