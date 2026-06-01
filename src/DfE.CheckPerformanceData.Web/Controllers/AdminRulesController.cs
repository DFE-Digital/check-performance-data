using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Read-only admin surface for the rules engine config (Milestone 2). GET-only;
// editing/saving arrives in later milestones. Admin-only. Views live under
// Views/Admin/Rules so they inherit the admin layout via the Views/Admin/_ViewStart
// cascade, hence the explicit view paths.
[Authorize(Roles = WikiConstants.AdminRole)]
public sealed class AdminRulesController(IRulesConfigService rules) : Controller
{
    private const string IndexView = "~/Views/Admin/Rules/Index.cshtml";
    private const string OutcomesView = "~/Views/Admin/Rules/Outcomes.cshtml";
    private const string OutcomeView = "~/Views/Admin/Rules/Outcome.cshtml";
    private const string LookupsView = "~/Views/Admin/Rules/Lookups.cshtml";
    private const string HistoryView = "~/Views/Admin/Rules/History.cshtml";
    private const string VersionView = "~/Views/Admin/Rules/Version.cshtml";

    [HttpGet("admin/rules")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var (ruleSet, _) = await TryGetRulesAsync(ct);
        var (lookups, _) = await TryGetLookupsAsync(ct);

        var rulesLatest = ruleSet is null ? null : await LatestVersionAsync(RulesConfigType.Rules, ct);
        var lookupsLatest = lookups is null ? null : await LatestVersionAsync(RulesConfigType.Lookups, ct);

        var model = new RulesLandingViewModel
        {
            Rules = RulesAdminViewModelFactory.RulesCard(ruleSet, rulesLatest),
            Lookups = RulesAdminViewModelFactory.LookupsCard(lookups, lookupsLatest)
        };

        return View(IndexView, model);
    }

    [HttpGet("admin/rules/outcomes")]
    public async Task<IActionResult> Outcomes(CancellationToken ct)
    {
        var (ruleSet, _) = await TryGetRulesAsync(ct);
        return View(OutcomesView, RulesAdminViewModelFactory.Outcomes(ruleSet));
    }

    [HttpGet("admin/rules/outcomes/{key}")]
    public async Task<IActionResult> Outcome(string key, CancellationToken ct)
    {
        var (ruleSet, _) = await TryGetRulesAsync(ct);
        var model = RulesAdminViewModelFactory.Outcome(ruleSet, key);
        return model is null ? NotFound() : View(OutcomeView, model);
    }

    [HttpGet("admin/rules/lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        var (lookups, _) = await TryGetLookupsAsync(ct);
        return View(LookupsView, RulesAdminViewModelFactory.Lookups(lookups));
    }

    [HttpGet("admin/rules/history/{type}")]
    public async Task<IActionResult> History(string type, CancellationToken ct)
    {
        if (!Enum.TryParse<RulesConfigType>(type, ignoreCase: true, out var configType))
        {
            return NotFound();
        }

        var versions = await rules.ListVersionsAsync(configType, ct);
        return View(HistoryView, RulesAdminViewModelFactory.History(configType, versions));
    }

    [HttpGet("admin/rules/history/{type}/{id:int}")]
    public async Task<IActionResult> Version(string type, int id, CancellationToken ct)
    {
        if (!Enum.TryParse<RulesConfigType>(type, ignoreCase: true, out var configType))
        {
            return NotFound();
        }

        var versions = await rules.ListVersionsAsync(configType, ct);
        var dto = versions.FirstOrDefault(v => v.Id == id);
        return dto is null ? NotFound() : View(VersionView, RulesAdminViewModelFactory.VersionDetail(dto));
    }

    // --- helpers (shared by later GET actions) ---

    private async Task<(RuleSet? Rules, string? ETag)> TryGetRulesAsync(CancellationToken ct)
    {
        try
        {
            return await rules.GetRulesAsync(ct);
        }
        catch (RulesConfigNotFoundException)
        {
            return (null, null);
        }
    }

    private async Task<(Lookups? Lookups, string? ETag)> TryGetLookupsAsync(CancellationToken ct)
    {
        try
        {
            return await rules.GetLookupsAsync(ct);
        }
        catch (RulesConfigNotFoundException)
        {
            return (null, null);
        }
    }

    private async Task<RulesConfigVersionDto?> LatestVersionAsync(RulesConfigType type, CancellationToken ct)
    {
        var versions = await rules.ListVersionsAsync(type, ct);
        return versions.Count == 0 ? null : versions.MaxBy(v => v.VersionNumber);
    }
}
