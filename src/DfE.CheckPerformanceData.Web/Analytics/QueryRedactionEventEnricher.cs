using Dfe.Analytics.AspNetCore;

namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Masks pupil-name query-string values (AB#286387 R3) on the <c>web_request</c>
/// event before it reaches BigQuery. <c>Event.RequestQuery</c> is populated by the
/// library as an <c>IDictionary&lt;string, string[]&gt;</c> (see
/// <c>AspNetCoreEventSender.PopulateEventFromRequest</c>), not a raw query string,
/// so matching entries are masked in place on the real dictionary rather than via
/// string parsing. <see cref="QueryRedaction"/> still owns the denylist and mask
/// token as a pure, independently-tested unit (and as the raw-string fallback used
/// e.g. for logging); this enricher only asks it which keys to mask.
/// </summary>
public sealed class QueryRedactionEventEnricher : IWebRequestEventEnricher
{
    public Task EnrichEventAsync(EnrichWebRequestEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestQuery = context.Event.RequestQuery;
        if (requestQuery is null)
        {
            return Task.CompletedTask;
        }

        foreach (var key in requestQuery.Keys.ToList())
        {
            if (!QueryRedaction.IsRedactedParam(key))
            {
                continue;
            }

            requestQuery[key] = requestQuery[key]
                .Select(value => string.IsNullOrEmpty(value) ? value : QueryRedaction.Mask)
                .ToArray();
        }

        return Task.CompletedTask;
    }
}
