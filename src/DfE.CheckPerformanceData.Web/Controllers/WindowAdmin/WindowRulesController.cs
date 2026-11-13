using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

// #800: the read-only page of a window's rule dates. Each window-relative literal in the
// serving rules set resolves against this window's start date, so an admin can verify the
// dates in effect before schools see an outcome. GET only: no form controls, so no
// anti-forgery surface. No personal data is shown, so no no-store marker.
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class WindowRulesController(
    IWindowService windowService,
    IRulesConfigService rules) : Controller
{
    private const string RulesView = "~/Views/WindowAdmin/Rules.cshtml";

    [HttpGet("admin/windows/{id:guid}/rules")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? w = await windowService.GetByIdAsync(id, cancellationToken);
        if (w is null)
        {
            return NotFound();
        }

        // The rules blob is shared by all windows; only the window's start date differs.
        // A missing or empty blob shows the "no rules configured" state, never a failure.
        return View(RulesView, RulesAdminViewModelFactory.WindowRules(
            w.Title, w.Id, DateOnly.FromDateTime(w.StartDate),
            await TryGetRulesAsync(cancellationToken)));
    }

    private async Task<DfE.CheckPerformanceData.Application.RulesEngine.RuleSet?> TryGetRulesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var (ruleSet, _) = await rules.GetRulesAsync(cancellationToken);
            return ruleSet;
        }
        catch (RulesConfigNotFoundException)
        {
            return null;
        }
    }
}
