using System.Globalization;
using System.Text;
using DfE.CheckPerformanceData.Application.Logging;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Viewer for the AppLogs table populated by DatabaseLoggerProvider. Provides a filter grid
// (level / category / free-text / date range), pagination, CSV download, and a "Clear all
// logs" action guarded by a confirm modal. Gated by the app-logs section grant.
[RequireAdminSection(AdminNavKeys.AppLogs)]
[Route("admin/system-administration/logs")]
public sealed class AppLogsController(
    IAppLogRepository logs,
    ISettingService settings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? level, string? category, string? search,
        string? from, string? to, int page = 1, CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.AppLogs;

        // Shared "rows per page" setting used by Help/Search, the deleted-pages list, and
        // every other paged admin surface. Bounded to a sensible minimum so a misconfigured
        // value can't produce a divide-by-zero on the paging math.
        var pageSize = Math.Max(1, await settings.GetIntAsync(SettingKeys.CmsPageLength));
        var pageNumber = Math.Max(1, page);
        var skip = (pageNumber - 1) * pageSize;

        var query = new AppLogQuery(
            Level: NullIfEmpty(level),
            Category: NullIfEmpty(category),
            Search: NullIfEmpty(search),
            FromUtc: ParseDate(from),
            ToUtc: ParseDate(to)?.AddDays(1).AddTicks(-1),   // inclusive end-of-day
            Skip: skip,
            Take: pageSize);

        var result = await logs.SearchAsync(query, ct);

        return View(new AppLogsViewModel
        {
            Rows = result.Rows,
            Total = result.Total,
            Levels = result.Levels,
            Categories = result.Categories,
            Skip = query.Skip,
            Take = query.Take,
            Level = level, Category = category, Search = search,
            From = from, To = to
        });
    }

    // CSV, streamed row-by-row so a big table doesn't blow the working set. Same filters as
    // the grid, no paging — download what you're currently looking at, plus everything past
    // the first page.
    [HttpGet("download")]
    public IActionResult Download(
        string? level, string? category, string? search,
        string? from, string? to, CancellationToken ct = default)
    {
        var query = new AppLogQuery(
            Level: NullIfEmpty(level),
            Category: NullIfEmpty(category),
            Search: NullIfEmpty(search),
            FromUtc: ParseDate(from),
            ToUtc: ParseDate(to)?.AddDays(1).AddTicks(-1),
            Skip: 0,
            Take: int.MaxValue);

        var fileName = $"app-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return new StreamedCsvResult(logs.StreamAsync(query, ct), fileName);
    }

    [HttpPost("clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken ct = default)
    {
        await logs.TruncateAsync(ct);
        TempData["AppLogsResult"] = "Application logs cleared.";
        return Redirect("/admin/system-administration/logs");
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : null;
    }

    // Streams the query results straight to the response body as CSV — no intermediate string
    // buffer, so millions of rows stay well under the working set.
    private sealed class StreamedCsvResult(IAsyncEnumerable<AppLogDto> rows, string fileName) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");

            // leaveOpen: true keeps the response body open for downstream middleware — the dev
            // diagnostic footer middleware wraps every response in a MemoryStream and reads it
            // back after the pipeline unwinds, which fails with "Cannot access a closed Stream"
            // when a StreamWriter disposes the body by default.
            await using var writer = new StreamWriter(response.Body, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
            await writer.WriteLineAsync(
                "Id,Timestamp,Level,Category,Message,Exception,EventId,RequestPath,UserId,CorrelationId,StateJson");

            await foreach (var r in rows.WithCancellation(context.HttpContext.RequestAborted))
            {
                await writer.WriteLineAsync(string.Join(",", new[]
                {
                    r.Id.ToString(CultureInfo.InvariantCulture),
                    Csv(r.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(r.Level),
                    Csv(r.Category),
                    Csv(r.Message),
                    Csv(r.Exception),
                    r.EventId.ToString(CultureInfo.InvariantCulture),
                    Csv(r.RequestPath),
                    Csv(r.UserId),
                    Csv(r.CorrelationId),
                    Csv(r.StateJson)
                }));
            }
        }

        // Excel-friendly CSV: quote when the field contains a delimiter, quote, or newline;
        // double any embedded quotes.
        private static string Csv(string? field)
        {
            if (field is null) return string.Empty;
            var needsQuoting = field.IndexOfAny([',', '"', '\n', '\r']) >= 0;
            return needsQuoting ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
        }
    }
}
