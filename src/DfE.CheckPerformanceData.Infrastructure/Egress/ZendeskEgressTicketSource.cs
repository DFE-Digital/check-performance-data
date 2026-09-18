using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Resilience;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>
/// Reads the "Decision status" custom field from real Zendesk tickets, by id, 100 at a time.
/// The field id comes from configuration (ZendeskTicketFields:DecisionStatusId); production
/// currently configures 0, and this refuses rather than treating every ticket as undecided.
/// </summary>
public sealed class ZendeskEgressTicketSource(
    IZendeskApi api,
    IOptions<ZendeskTicketFieldSettings> fieldSettings,
    IOptions<PollySettings> pollySettings,
    ILogger<ZendeskEgressTicketSource> logger) : IEgressTicketSource
{
    public const int BatchSize = 100;
    private readonly ResiliencePipeline _retry = ResiliencePipelineFactory.CreateRetryPipeline(pollySettings.Value, logger);

    public async Task<IReadOnlyDictionary<long, string>> GetDecisionStatusesAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct)
    {
        var fieldId = fieldSettings.Value.DecisionStatusId;
        if (fieldId is null or <= 0)
            throw new EgressTicketSourceException(
                "The Zendesk \"Decision status\" field id is not configured for this environment (ZendeskTicketFields:DecisionStatusId), so decisions cannot be read.");

        var result = new Dictionary<long, string>();
        if (ticketIds.Count == 0) return result;

        foreach (var batch in ticketIds.Distinct().Chunk(BatchSize))
        {
            var ids = string.Join(",", batch);
            var response = await _retry.ExecuteAsync(async token =>
            {
                try { return await api.ShowManyTickets(ids); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new EgressTicketSourceException($"Zendesk could not return tickets {ids}.", ex);
                }
            }, ct);

            foreach (var ticket in response.Tickets)
            {
                var value = ticket.AllCustomFields.FirstOrDefault(f => f.Id == fieldId)?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    result[ticket.Id] = value.Trim().ToLowerInvariant();
            }
        }
        return result;
    }
}
