namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The one thing Zendesk is asked: what decision each ticket carries, and — when the ticket
/// carries one for its decision — a human-readable reason for it. Keyed by ticket id; a ticket
/// Zendesk did not return, or returned without a readable decision, is absent — the caller shows
/// it as EgressDecisions.NotFound, never as approved. The reason label is resolved at the Zendesk
/// edge; this interface never sees a raw option code.
/// </summary>
public interface IEgressTicketSource
{
    /// <exception cref="EgressTicketSourceException">The field is not configured for this environment or Zendesk could not be read.</exception>
    Task<IReadOnlyDictionary<long, EgressTicketDecision>> GetTicketDecisionsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct);
}

/// <summary>
/// What one ticket carries. <see cref="Decision"/> is the raw "Decision status" value, trimmed and
/// lower-invariant. <see cref="Reason"/> is a human-readable label, or null when the ticket has no
/// reason for its decision (scrutiny, a missing field, or an unset field id).
/// </summary>
public sealed record EgressTicketDecision(string? Decision, string? Reason);

public sealed class EgressTicketSourceException(string message, Exception? inner = null) : Exception(message, inner);
