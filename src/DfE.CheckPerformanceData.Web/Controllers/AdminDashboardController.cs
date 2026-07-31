using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Engagement & amendment metrics for open checking windows (PBI 288143). Read-only; figures
// come from DashboardService's 15-minute cache. Gated by the dashboard section grant — like
// every admin section, non-granted users get 404 (the PBI's "Access Denied" is deliberately
// implemented as the codebase's standard hide-the-surface behaviour).
[RequireAdminSection(AdminNavKeys.Dashboard)]
public sealed class AdminDashboardController(
    IWindowService windowService,
    IDashboardService dashboardService,
    IOptions<DashboardSettings> settings) : Controller
{
    private const string IndexView = "~/Views/Admin/Dashboard/Index.cshtml";

    [HttpGet("admin/dashboard")]
    public async Task<IActionResult> Index(Guid? windowId, CancellationToken cancellationToken)
    {
        var all = await windowService.GetAllDataAsync(cancellationToken);
        var openWindows = (all?.Windows ?? [])
            .Where(w => w.IsOpen)
            .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (openWindows.Count == 0)
        {
            return View(IndexView, new AdminDashboardViewModel
            {
                OpenWindows = [],
                RefreshMinutes = settings.Value.RefreshMinutes,
            });
        }

        var selected = openWindows.FirstOrDefault(w => w.Id == windowId) ?? openWindows[0];
        var metrics = await dashboardService.GetMetricsAsync(selected, cancellationToken);

        return View(IndexView, new AdminDashboardViewModel
        {
            OpenWindows = openWindows.Select(w => new AdminDashboardViewModel.WindowOption(w.Id, w.Title)).ToList(),
            SelectedWindowId = selected.Id,
            Metrics = metrics,
            RefreshMinutes = settings.Value.RefreshMinutes,
        });
    }
}
