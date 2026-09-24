using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.Egress;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.AuditLog;

/// <summary>
/// The audit log (AB#294592): every audit row newest first, egress transfers distinguishable among
/// them, filtered by activity, checking window and outcome (cumulative), 20 a page, exportable as
/// CSV under the same filters. Read-only — nothing here writes, and the table's own trigger refuses
/// UPDATE and DELETE. Its own gate key because the sidebar filters tiles by their own key.
/// </summary>
[RequireAdminSection(AdminNavKeys.AuditLog)]
[Route("admin/audit-log")]
public sealed class AuditLogController(IAuditLogRepository audit, IWindowService windows) : Controller
{
    public const int PageSize = 20;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? activity, Guid? windowId, string? status, int page = 1, CancellationToken cancellationToken = default)
    {
        var activities = await audit.ListActivitiesAsync(cancellationToken);
        var filter = Filter(activity, windowId, status, activities);
        var result = await audit.ListAsync(filter, page, PageSize, cancellationToken);
        var all = (await windows.GetAllDataAsync(cancellationToken))?.Windows ?? [];

        return View(new AuditLogViewModel
        {
            Activities = activities.Select(a => new ActivityChoice(a, AuditActivities.Label(a))).ToList(),
            Windows = all.OrderByDescending(w => w.StartDate)
                .Select(w => new WindowChoice(w.Id, $"{w.Title} ({EgressOutputTypes.StageToken(w.CheckingWindowType)})")).ToList(),
            SelectedActivity = filter.Activity,
            SelectedWindowId = filter.WindowId,
            SelectedOutcome = filter.Outcome,
            Rows = result.Rows.Select(AuditLogRowViewModel.From).ToList(),
            Page = result.Page,
            TotalPages = result.TotalPages,
            TotalCount = result.TotalCount
        });
    }

    // The filtered log, every page: the same query string the page carries, streamed.
    [HttpGet("export")]
    public async Task<IActionResult> Export(string? activity, Guid? windowId, string? status, CancellationToken cancellationToken = default)
    {
        var activities = await audit.ListActivitiesAsync(cancellationToken);
        var filter = Filter(activity, windowId, status, activities);
        return new AuditLogCsvResult(audit.StreamAsync(filter, cancellationToken), $"audit-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    // An unknown value is no filter, never an error: the selects only post known values, and a
    // hand-typed URL should widen the list rather than empty it. "All windows" posts an empty value
    // (binds as null); Guid.Empty is treated the same.
    internal static AuditLogFilter Filter(string? activity, Guid? windowId, string? status, IReadOnlyList<string> activities)
    {
        var trimmed = activity?.Trim();
        return new AuditLogFilter(
            !string.IsNullOrEmpty(trimmed) && activities.Contains(trimmed, StringComparer.Ordinal) ? trimmed : null,
            windowId is null || windowId == Guid.Empty ? null : windowId,
            AuditActivities.TryParseOutcome(status));
    }
}
