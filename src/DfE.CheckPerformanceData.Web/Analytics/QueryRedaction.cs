using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Masks the values of query-string parameters that carry pupil names before
/// the web_request analytics event is sent (AB#286387 R3). The custom events
/// already omit the term; this closes the request_query side channel.
/// </summary>
public static class QueryRedaction
{
    private static readonly HashSet<string> RedactedParams =
        new(StringComparer.OrdinalIgnoreCase) { "includedSearch", "nonIncludedSearch", "query" };

    public const string Mask = "[redacted]";

    /// <summary>
    /// Whether <paramref name="key"/> is one of the pupil-name-carrying params that
    /// must be masked. Shared with <see cref="QueryRedactionEventEnricher"/>, which
    /// applies the same denylist directly to <c>Event.RequestQuery</c>'s
    /// dictionary shape rather than via this string helper.
    /// </summary>
    public static bool IsRedactedParam(string key) => RedactedParams.Contains(key);

    public static string? Redact(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return query;
        }

        var parsed = QueryHelpers.ParseQuery(query.StartsWith('?') ? query : "?" + query);
        if (!parsed.Keys.Any(IsRedactedParam))
        {
            return query;
        }

        var builder = new QueryBuilder();
        foreach (var (key, values) in parsed)
        {
            foreach (var value in values)
            {
                var masked = IsRedactedParam(key) && !string.IsNullOrEmpty(value)
                    ? Mask
                    : value ?? string.Empty;
                builder.Add(key, masked);
            }
        }

        // QueryBuilder emits a leading '?'; mirror the input's shape.
        var result = builder.ToQueryString().Value ?? string.Empty;
        return query.StartsWith('?') ? result : result.TrimStart('?');
    }
}
