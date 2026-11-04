using System.Text.Json;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>
/// Development stand-in: reads decisions from the DevZendeskTickets outbox the worker's fake
/// Zendesk writes. The captured ticket's "Decision status" custom field wins (so a dev seeder can
/// stage a human "approved"); otherwise the subject prefix the ticket builder always writes
/// decides. The matching "Decision Reason" field value is the reason, as-is — the dev seeder
/// writes the label text straight into the field, so no option lookup. Selected via
/// Egress:UseDevOutbox (web egress reads), decoupled from the worker's Zendesk:UseFake write-path
/// flag.
/// </summary>
public sealed class DevOutboxEgressTicketSource(IPortalDbContext db, IOptions<ZendeskTicketFieldSettings> fieldSettings) : IEgressTicketSource
{
    // The dev/preprod Zendesk instance's field ids, used when the environment configures none.
    public const long WellKnownDecisionFieldId = 19056253670034;
    public const long WellKnownApprovedReasonFieldId = 19056477269010;
    public const long WellKnownRejectedReasonFieldId = 19197936168210;

    public async Task<IReadOnlyDictionary<long, EgressTicketDecision>> GetTicketDecisionsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct)
    {
        var result = new Dictionary<long, EgressTicketDecision>();
        if (ticketIds.Count == 0) return result;

        var settings = fieldSettings.Value;
        var decisionFieldId = settings.DecisionStatusId is > 0 and var configured ? configured : WellKnownDecisionFieldId;
        var ids = ticketIds.Distinct().ToList();
        var rows = await db.DevZendeskTickets.AsNoTracking().Where(t => ids.Contains(t.TicketId)).ToListAsync(ct);

        foreach (var row in rows)
        {
            var decision = FromCustomField(row.RawJson, decisionFieldId) ?? FromSubject(row.Subject);
            if (decision is null) continue;
            result[row.TicketId] = new EgressTicketDecision(decision, ReasonFor(row.RawJson, decision, settings));
        }
        return result;
    }

    private static string? ReasonFor(string rawJson, string decision, ZendeskTicketFieldSettings settings)
    {
        if (decision is ZendeskTicketFieldOptions.DecisionStatus.Approved or ZendeskTicketFieldOptions.DecisionStatus.AutoApproved)
        {
            var fieldId = settings.DecisionReasonApprovedId is > 0 ? settings.DecisionReasonApprovedId : WellKnownApprovedReasonFieldId;
            return FromCustomFieldRaw(rawJson, fieldId.Value);
        }
        if (decision is ZendeskTicketFieldOptions.DecisionStatus.Rejected or ZendeskTicketFieldOptions.DecisionStatus.AutoRejected)
        {
            var fieldId = settings.DecisionReasonRejectedId is > 0 ? settings.DecisionReasonRejectedId : WellKnownRejectedReasonFieldId;
            return FromCustomFieldRaw(rawJson, fieldId.Value);
        }
        return null;
    }

    private static string? FromCustomField(string rawJson, long fieldId)
    {
        var value = FromCustomFieldRaw(rawJson, fieldId);
        return value is null ? null : value.ToLowerInvariant();
    }

    private static string? FromCustomFieldRaw(string rawJson, long fieldId)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CreateTicketRequestDto>(rawJson);
            var value = request?.Ticket.CustomFields.FirstOrDefault(f => f.Id == fieldId)?.Value?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (JsonException)
        {
            return null;
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
