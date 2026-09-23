using System.Globalization;
using System.Text;
using DfE.CheckPerformanceData.Application.Audit;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AuditLog;

/// <summary>The audit log export's shape (AB#294592): one line per row, the same fields the page shows, never a payload column.</summary>
public static class AuditLogCsv
{
    // FLAGGED copy (AB#294592): column names.
    public const string Header = "Timestamp (UTC),User,Activity,Action,Entity type,Entity id,Checking window,Output types,Status";

    public static string Line(AuditLogRow row) => string.Join(",", new[]
    {
        Field(row.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        Field(row.UserName ?? row.UserId),
        Field(AuditActivities.Label(row.EntityType)),
        Field(row.Action),
        Field(row.EntityType),
        Field(row.EntityId),
        Field(row.WindowTitle),
        Field(string.Join("; ", row.OutputTypes)),
        Field(row.Outcome is { } outcome ? AuditActivities.OutcomeLabel(outcome) : null)
    });

    // Excel-friendly CSV: quote when the field contains a delimiter, quote, or newline; double any
    // embedded quotes. Same rule as the app-logs download.
    public static string Field(string? field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        var needsQuotes = field.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        var escaped = field.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}

/// <summary>
/// Streams the filtered log straight to the response body — no intermediate buffer, so a busy
/// table stays well under the working set. Mirrors AppLogsController's StreamedCsvResult, including
/// leaveOpen: the dev diagnostic middleware wraps the body and reads it back after the pipeline
/// unwinds, which fails if the writer disposes the stream.
/// </summary>
public sealed class AuditLogCsvResult(IAsyncEnumerable<AuditLogRow> rows, string fileName) : IActionResult
{
    public string FileName => fileName;

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");

        await using var writer = new StreamWriter(response.Body, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        await writer.WriteLineAsync(AuditLogCsv.Header);
        await foreach (var row in rows.WithCancellation(context.HttpContext.RequestAborted))
            await writer.WriteLineAsync(AuditLogCsv.Line(row));
    }
}
