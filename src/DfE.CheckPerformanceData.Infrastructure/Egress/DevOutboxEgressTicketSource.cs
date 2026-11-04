using System.Text.Json;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>
/// Development stand-in: reads tickets from the DevZendeskTickets outbox that the worker's fake
/// Zendesk writes. The fields are read as ZendeskEgressTicketSource reads them. The captured
/// ticket's "Decision status" custom field wins (so a dev seeder can stage a human "approved");
/// otherwise the subject prefix the ticket builder always writes decides. Selected via
/// Egress:UseDevOutbox (web egress reads), decoupled from the worker's Zendesk:UseFake write-path flag.
/// </summary>
public sealed class DevOutboxEgressTicketSource(IPortalDbContext db, IOptions<ZendeskTicketFieldSettings> fieldSettings) : IEgressTicketSource
{
    // The dev/preprod Zendesk instance's field id, used when the environment configures none.
    public const long WellKnownDecisionFieldId = 19056253670034;

    public async Task<IReadOnlyDictionary<long, EgressTicket>> GetTicketsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct)
    {
        var result = new Dictionary<long, EgressTicket>();
        if (ticketIds.Count == 0) return result;

        var settings = fieldSettings.Value;
        var decisionFieldId = settings.DecisionStatusId is > 0 and var configured ? configured : WellKnownDecisionFieldId;
        var fieldIds = EgressTicketFieldIds.Resolve(settings);
        var ids = ticketIds.Distinct().ToList();
        var rows = await db.DevZendeskTickets.AsNoTracking().Where(t => ids.Contains(t.TicketId)).ToListAsync(ct);

        foreach (var row in rows)
        {
            var ticket = EgressTicketFieldIds.ToTicket(decisionFieldId, fieldIds, CustomFields(row.RawJson));
            result[row.TicketId] = ticket.Decision is null ? ticket with { Decision = FromSubject(row.Subject) } : ticket;
        }
        return result;
    }

    private static IReadOnlyDictionary<long, string?> CustomFields(string rawJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CreateTicketRequestDto>(rawJson);
            return (request?.Ticket.CustomFields ?? [])
                .Where(f => f.Id is not null)
                .GroupBy(f => f.Id!.Value)
                .ToDictionary(g => g.Key, g => g.First().Value?.ToString());
        }
        catch (JsonException)
        {
            return new Dictionary<long, string?>();
        }
    }

    private static string? FromSubject(string subject)
    {
        if (subject.StartsWith("CPMD Auto-Approved:", StringComparison.OrdinalIgnoreCase)) return ZendeskTicketFieldOptions.DecisionStatus.AutoApproved;
        if (subject.StartsWith("CPMD Auto-Rejected:", StringComparison.OrdinalIgnoreCase)) return ZendeskTicketFieldOptions.DecisionStatus.AutoRejected;
        if (subject.StartsWith("CPMD Requires Scrutiny:", StringComparison.OrdinalIgnoreCase)) return ZendeskTicketFieldOptions.DecisionStatus.Scrutiny;
        return null;
    }
}
