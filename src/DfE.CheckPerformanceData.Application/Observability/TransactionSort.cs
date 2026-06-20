namespace DfE.CheckPerformanceData.Application.Observability;

// The server-side allow-list mapping a transactions-table sort key to its SQL column. Sorting is
// chosen from this list before any query runs, so a hand-edited ?sort= value can never reach the
// ORDER BY as raw SQL — an unknown key snaps to the default (newest first). The keys are the
// stable identifiers the column headers submit.
public static class TransactionSort
{
    public const string DefaultKey = "time";

    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["time"] = "recorded_at_utc",
            ["reference"] = "reference_number",
            ["stage"] = "stage",
            ["queue"] = "queue_name",
            ["decision"] = "decision_status",
            ["latency"] = "latency_ms",
        };

    public static bool IsKnown(string? key) => key is not null && Columns.ContainsKey(key);

    // The validated sort key — the supplied one if recognised, else the default. Never returns an
    // arbitrary string, so callers can use it directly in markup without re-checking.
    public static string Resolve(string? key) => IsKnown(key) ? key!.ToLowerInvariant() : DefaultKey;

    // The SQL column for a key. Throws for an unknown key — callers resolve first, so this is the
    // only place a key becomes SQL and an out-of-allow-list value can never get here.
    public static string Column(string key) =>
        Columns.TryGetValue(key, out var column)
            ? column
            : throw new ArgumentException($"Unknown transactions sort key '{key}'.", nameof(key));
}
