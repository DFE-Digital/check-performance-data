using Dfe.Analytics.AspNetCore;

namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Masks pupil-name query-string values (AB#286387 R3) on the <c>web_request</c>
/// event before it reaches BigQuery. <c>Event.RequestQuery</c> is populated by the
/// library as an <c>IDictionary&lt;string, string[]&gt;</c> (see
/// <c>AspNetCoreEventSender.PopulateEventFromRequest</c>), not a raw query string,
/// so matching entries are masked in place on the real dictionary rather than via
/// string parsing. <c>Event.RequestReferer</c> (the raw <c>Referer</c> header,
/// populated verbatim by the same method) is a separate field that can carry the
/// same pupil-name query string on the very next same-origin navigation after a
/// search, so it is scrubbed too via <see cref="QueryRedaction.Redact(string?)"/>
/// on its query portion.
/// <see cref="QueryRedaction"/> owns the denylist and mask token as a pure,
/// independently-tested unit in its own right; this enricher asks it which keys
/// to mask for <c>RequestQuery</c>, and calls its string-based <c>Redact</c>
/// directly to scrub <c>RequestReferer</c>'s query string.
/// </summary>
public sealed class QueryRedactionEventEnricher : IWebRequestEventEnricher
{
    public Task EnrichEventAsync(EnrichWebRequestEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestQuery = context.Event.RequestQuery;
        if (requestQuery is not null)
        {
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
        }

        var redactedReferer = RedactReferer(context.Event.RequestReferer);
        if (redactedReferer != context.Event.RequestReferer)
        {
            context.Event.RequestReferer = redactedReferer;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Scrubs denylisted query-string params from a raw Referer header value.
    /// Returns <paramref name="referer"/> unchanged when it is null/empty, has no
    /// query string, is not a well-formed absolute URI, or has nothing to redact
    /// — the enricher must never throw on an attacker-controlled header.
    /// </summary>
    private static string? RedactReferer(string? referer)
    {
        if (string.IsNullOrEmpty(referer))
        {
            return referer;
        }

        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
        {
            return referer;
        }

        var redactedQuery = QueryRedaction.Redact(uri.Query);
        if (redactedQuery == uri.Query)
        {
            return referer;
        }

        return uri.GetLeftPart(UriPartial.Path) + redactedQuery + uri.Fragment;
    }
}
