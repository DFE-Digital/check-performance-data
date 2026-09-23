using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.Egress;

/// <summary>
/// Egress runs history (AB#294590): every run, newest first, filtered by window and outcome, 20 a
/// page. Read-only — a Draft's Resume link and a finished run's View link both go to
/// EgressController.Resume, which routes by status. Its own gate key because the sidebar filters
/// tiles by their own key (see AdminNavKeys.EgressRuns).
/// </summary>
[RequireAdminSection(AdminNavKeys.EgressRuns)]
[Route("admin/egress/runs")]
public sealed class EgressRunsController(IEgressRunService runs, IWindowService windows) : Controller
{
    public const int PageSize = 20;

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? windowId, string? status, int page = 1, CancellationToken cancellationToken = default)
    {
        // "All windows" posts an empty value, which binds as null; Guid.Empty is treated the same
        // so a hand-typed ?windowId=00000000-... cannot filter down to nothing.
        var filter = new EgressRunHistoryFilter(
            windowId is null || windowId == Guid.Empty ? null : windowId,
            EgressRunOutcomes.TryParse(status));

        var history = await runs.ListHistoryAsync(filter, page, PageSize, cancellationToken);
        var all = (await windows.GetAllDataAsync(cancellationToken))?.Windows ?? [];

        return View(new EgressRunsViewModel
        {
            Windows = all.OrderByDescending(w => w.StartDate)
                .Select(w => new WindowChoice(w.Id, $"{w.Title} ({EgressOutputTypes.StageToken(w.CheckingWindowType)})")).ToList(),
            SelectedWindowId = filter.WindowId,
            SelectedOutcome = filter.Outcome,
            Rows = history.Rows.Select(Row).ToList(),
            Page = history.Page,
            TotalPages = history.TotalPages,
            TotalCount = history.TotalCount
        });
    }

    private static EgressRunsRow Row(EgressRunHistoryRow r)
    {
        var outcome = EgressRunOutcomes.Of(r.Status);
        return new EgressRunsRow(r.Id, r.WindowTitle, outcome, EgressRunOutcomes.Label(outcome), EgressRunsRow.TagClassFor(outcome),
            r.OutputTypes.Select(EgressOutputTypes.Label).ToList(), r.RecordsTransferred, r.StartedByName, r.StartedAtUtc);
    }
}
