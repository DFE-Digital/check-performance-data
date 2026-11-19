using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

// #800: the read-only page of a window's rule dates. Each window-relative literal in the
// serving rules set resolves against this window's PupilData exercise start date, so an
// admin can verify the dates in effect before schools see an outcome. GET only: no form
// controls, so no anti-forgery surface. No personal data is shown, so no no-store marker.
// The page shows the whole shared rules config, so it also requires the rules-config grant,
// not just the window grant, and a refusal is logged by the attribute.
[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class WindowRulesController(
    IWindowService windowService,
    IRulesConfigService rules) : Controller
{
    private const string RulesView = "~/Views/WindowAdmin/Rules.cshtml";

    [HttpGet("admin/windows/{id:guid}/rules")]
    [RequireAdminSection(AdminNavKeys.RulesConfig)]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? w = await windowService.GetByIdAsync(id, cancellationToken);
        if (w is null)
        {
            return NotFound();
        }

        // Anchor the rule dates on the PupilData exercise's start date, not the window's: a
        // data-share exercise that starts earlier would otherwise move the year every rule
        // uses (#800). Null when the window has no PupilData exercise; relative dates then
        // show as unresolvable.
        var startDate = w.FindExercise(CheckingExerciseType.PupilData) is { } pupilData
            ? DateOnly.FromDateTime(pupilData.StartDate)
            : (DateOnly?)null;

        // The rules blob is shared by all windows; only the window's anchor and type differ.
        // A missing or empty blob shows the "no rules configured" state, never a failure.
        var (rulesSet, _) = await RulesConfigLookup.TryGetRulesAsync(rules, cancellationToken);
        return View(RulesView, RulesAdminViewModelFactory.WindowRules(
            w.Title, w.Id, startDate, w.CheckingWindowType, rulesSet));
    }
}
