namespace DfE.CheckPerformanceData.Application.Observability;

// The server-side allow-list mapping a grouped-transactions sort key to its SQL ORDER BY
// expression. Like the flat-list TransactionSort, sorting is chosen from this list before any query
// runs, so a hand-edited ?sort= value can never reach the ORDER BY as raw SQL — an unknown key
// snaps to the default (most recently active first). The keys are the stable identifiers the
// grouped column headers submit; the values reference the columns of the aggregated subquery the
// grouped page wraps (some are derived per-queue waits), never caller text.
public static class GroupedTransactionSort
{
    public const string DefaultKey = "last";

    private static readonly IReadOnlyDictionary<string, string> Expressions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reference"] = "reference_number",
            ["submit"] = "submitted_at",
            // The per-queue waits and engine processing sort by the SAME real spans the grouped view
            // shows: queue wait = enqueue → dequeue (started), processing = dequeue → done. Rows
            // missing the started time (legacy) sort null and sink via the NULLS LAST in the query.
            ["rulesqueue"] = "(rules_started - submitted_at)",   // submit → rules dequeue
            ["rulesengine"] = "(rules_at - rules_started)",      // rules dequeue → done (processing)
            ["status"] = "decision",
            ["zendeskqueue"] = "(ticket_started - rules_at)",    // rules done → Zendesk dequeue
            ["zendeskticket"] = "ticket_at",
            ["last"] = "last_at",
        };

    public static bool IsKnown(string? key) => key is not null && Expressions.ContainsKey(key);

    // The validated sort key — the supplied one if recognised, else the default. Never returns an
    // arbitrary string, so callers can use it directly in markup without re-checking.
    public static string Resolve(string? key) => IsKnown(key) ? key!.ToLowerInvariant() : DefaultKey;

    // The SQL ORDER BY expression for a key. Throws for an unknown key — callers resolve first, so
    // this is the only place a key becomes SQL and an out-of-allow-list value can never get here.
    public static string OrderExpression(string key) =>
        Expressions.TryGetValue(key, out var expression)
            ? expression
            : throw new ArgumentException($"Unknown grouped transactions sort key '{key}'.", nameof(key));
}
