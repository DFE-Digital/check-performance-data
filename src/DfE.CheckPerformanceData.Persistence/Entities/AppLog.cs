namespace DfE.CheckPerformanceData.Persistence.Entities;

// One row per emitted ILogger event. Written asynchronously by
// DatabaseLoggerProvider's background writer; queried by the
// system-administration logs page. Retention is left to the operator
// (there is a "clear all" button on the admin view for now).
public sealed class AppLog
{
    public long Id { get; set; }

    // Server time when the record was emitted; indexed DESC so "most recent
    // first" pagination is a single index scan.
    public DateTime Timestamp { get; set; }

    // Log level as its .NET enum name ("Information", "Warning", "Error", ...).
    // Stored as text so the table stays legible outside EF Core.
    public string Level { get; set; } = string.Empty;

    // The logger source, e.g. "DfE.CheckPerformanceData.Application.ContentStaging.ContentStagingService".
    // Indexed so per-source filtering doesn't scan the whole table.
    public string Category { get; set; } = string.Empty;

    // Rendered message with structured placeholders resolved.
    public string Message { get; set; } = string.Empty;

    // Full exception detail (type + message + stack + inner chain), null if none.
    public string? Exception { get; set; }

    // EventId.Id from the logger call site; 0 when none set.
    public int EventId { get; set; }

    // Structured state as JSON — the placeholders + their values from the
    // original log call. Stored as jsonb so we can query by property later.
    public string? StateJson { get; set; }

    // Ambient HTTP context, best-effort. Nullable so background-service logs
    // (which have no request) can still be recorded.
    public string? RequestPath { get; set; }
    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }
}
