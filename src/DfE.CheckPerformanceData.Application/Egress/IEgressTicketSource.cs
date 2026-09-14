namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The one thing egress asks Zendesk: what decision each ticket carries. Keyed by ticket id;
/// a ticket Zendesk did not return, or returned without a readable decision, is absent — the
/// caller shows it as EgressDecisions.NotFound, never as approved.
/// </summary>
public interface IEgressTicketSource
{
    /// <exception cref="EgressTicketSourceException">The field is not configured for this environment or Zendesk could not be read.</exception>
    Task<IReadOnlyDictionary<long, string>> GetDecisionStatusesAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct);
}

public sealed class EgressTicketSourceException(string message, Exception? inner = null) : Exception(message, inner);
