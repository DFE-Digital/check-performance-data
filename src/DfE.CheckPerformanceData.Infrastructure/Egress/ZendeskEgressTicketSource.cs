using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Resilience;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>
/// Reads the "Decision status" custom field from real Zendesk tickets, by id, 100 at a time, and —
/// for a final decision — the matching "Decision Reason" tagger field, resolved to its
/// human-readable label. The decision field id comes from configuration
/// (ZendeskTicketFields:DecisionStatusId); production currently configures 0, and this refuses
/// rather than treating every ticket as undecided. The reason is the best-effort half of the
/// read: a failed ticket_fields.json call, an unset reason id, or an unresolvable code gives a
/// null reason — never a failed pull.
/// </summary>
public sealed class ZendeskEgressTicketSource(
    IZendeskApi api,
    IOptions<ZendeskTicketFieldSettings> fieldSettings,
    IOptions<PollySettings> pollySettings,
    ILogger<ZendeskEgressTicketSource> logger) : IEgressTicketSource
{
    public const int BatchSize = 100;
    private readonly ResiliencePipeline _retry = ResiliencePipelineFactory.CreateRetryPipeline(pollySettings.Value, logger);

    public async Task<IReadOnlyDictionary<long, EgressTicketDecision>> GetTicketDecisionsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct)
    {
        var settings = fieldSettings.Value;
        var decisionFieldId = settings.DecisionStatusId;
        if (decisionFieldId is null or <= 0)
            throw new EgressTicketSourceException(
                "The Zendesk \"Decision status\" field id is not configured for this environment (ZendeskTicketFields:DecisionStatusId), so decisions cannot be read.");

        var result = new Dictionary<long, EgressTicketDecision>();
        if (ticketIds.Count == 0) return result;

        var reasonLabels = await ReasonLabelsAsync(settings, ct);

        foreach (var batch in ticketIds.Distinct().Chunk(BatchSize))
        {
            var ids = string.Join(",", batch);
            var response = await _retry.ExecuteAsync(async token =>
            {
                try { return await api.ShowManyTickets(ids); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new EgressTicketSourceException($"Zendesk could not return tickets {string.Join(", ", batch)}.", ex);
                }
            }, ct);

            foreach (var ticket in response.Tickets)
            {
                var value = ticket.AllCustomFields.FirstOrDefault(f => f.Id == decisionFieldId)?.Value?.ToString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                var decision = value.Trim().ToLowerInvariant();
                result[ticket.Id] = new EgressTicketDecision(decision, ReasonFor(ticket, decision, settings, reasonLabels));
            }
        }
        return result;
    }

    // One ticket_fields.json call per pull, only when a reason field id is configured. A failure
    // here drops the reasons and keeps the decisions (the reason is best-effort, the decision is
    // fail-closed).
    private async Task<Dictionary<long, Dictionary<string, string>>> ReasonLabelsAsync(ZendeskTicketFieldSettings settings, CancellationToken ct)
    {
        var ids = new[] { settings.DecisionReasonApprovedId, settings.DecisionReasonRejectedId }
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return new Dictionary<long, Dictionary<string, string>>();

        TicketFieldsResponse response;
        try
        {
            response = await _retry.ExecuteAsync(async token => await api.GetTicketFields(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Zendesk could not return the ticket field definitions; decision reasons will be empty.");
            return new Dictionary<long, Dictionary<string, string>>();
        }

        return response.TicketFields
            .Where(f => ids.Contains(f.Id) && f.CustomFieldOptions.Count > 0)
            .ToDictionary(f => f.Id,
                f => f.CustomFieldOptions.ToDictionary(o => o.Value, o => o.Name, StringComparer.Ordinal));
    }

    private static string? ReasonFor(
        Ticket ticket,
        string decision,
        ZendeskTicketFieldSettings settings,
        Dictionary<long, Dictionary<string, string>> reasonLabels)
    {
        var reasonFieldId = ReasonFieldIdFor(decision, settings);
        if (reasonFieldId is null) return null;

        var code = ticket.AllCustomFields.FirstOrDefault(f => f.Id == reasonFieldId)?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(code)) return null;

        // A code Zendesk's field definitions do not know maps to nothing, not to the code itself.
        return reasonLabels.TryGetValue(reasonFieldId.Value, out var labels)
               && labels.TryGetValue(code.Trim(), out var label)
            ? label
            : null;
    }

    private static long? ReasonFieldIdFor(string decision, ZendeskTicketFieldSettings settings) =>
        decision switch
        {
            ZendeskTicketFieldOptions.DecisionStatus.Approved or ZendeskTicketFieldOptions.DecisionStatus.AutoApproved
                when settings.DecisionReasonApprovedId is > 0 => settings.DecisionReasonApprovedId,
            ZendeskTicketFieldOptions.DecisionStatus.Rejected or ZendeskTicketFieldOptions.DecisionStatus.AutoRejected
                when settings.DecisionReasonRejectedId is > 0 => settings.DecisionReasonRejectedId,
            _ => null
        };
}
