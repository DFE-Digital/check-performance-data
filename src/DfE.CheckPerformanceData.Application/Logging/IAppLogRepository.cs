namespace DfE.CheckPerformanceData.Application.Logging;

// Read + write surface for the AppLogs table. Writes come from the background log-sink
// (batch inserts); reads come from the admin logs page (filtered + paged); TruncateAll
// backs the "Clear all logs" button.
public interface IAppLogRepository
{
    // Batched write path from the background log writer. Records are handed over already
    // populated; the repository just does the bulk insert and returns the number written.
    Task<int> BulkInsertAsync(IReadOnlyList<AppLogDto> logs, CancellationToken cancellationToken);

    // Paged query with optional Level / Category / free-text (Message + Exception, ILIKE) /
    // date-range filters. Always ordered by Timestamp DESC. Returns rows + total count for
    // pagination + the distinct Levels and Categories present in the table (for filter dropdowns).
    Task<AppLogPage> SearchAsync(AppLogQuery query, CancellationToken cancellationToken);

    // Stream every row matching the query (no paging) for the CSV download. Enumerated by the
    // caller and never fully materialised into memory — even a large log table stays under budget.
    IAsyncEnumerable<AppLogDto> StreamAsync(AppLogQuery query, CancellationToken cancellationToken);

    // TRUNCATE the AppLogs table. Powers the "Clear all logs" admin button.
    Task TruncateAsync(CancellationToken cancellationToken);
}
