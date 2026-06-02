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
    private const string BranchEditView = "~/Views/Admin/Rules/BranchEdit.cshtml";
    private const string RemoveBranchView = "~/Views/Admin/Rules/RemoveBranch.cshtml";

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

    [HttpGet("admin/rules/outcomes/{key}/branches/{id}/edit")]
    public async Task<IActionResult> EditBranch(string key, string id, CancellationToken ct)
    {
        var (ruleSet, etag) = await TryGetRulesAsync(ct);
        var outcome = ruleSet?.Outcomes.FirstOrDefault(o => o.Key == key);
        var branch = outcome?.Rules.FirstOrDefault(b => b.Id == id);
        if (outcome is null || branch is null || branch.When is Predicate.Otherwise)
        {
            return NotFound();
        }

        var form = new BranchEditForm
        {
            OutcomeKey = outcome.Key,
            OutcomeLabel = outcome.Label,
            BranchId = branch.Id,
            IsNew = false,
            Status = branch.Status,
            LoadETag = etag,
            Nodes = PredicateForm.Flatten(branch.When)
        };
        return View(BranchEditView, BranchEditViewModel.For(form));
    }

    [HttpGet("admin/rules/outcomes/{key}/branches/add")]
    public async Task<IActionResult> AddBranch(string key, CancellationToken ct)
    {
        var (ruleSet, etag) = await TryGetRulesAsync(ct);
        var outcome = ruleSet?.Outcomes.FirstOrDefault(o => o.Key == key);
        if (outcome is null)
        {
            return NotFound();
        }

        var form = new BranchEditForm
        {
            OutcomeKey = outcome.Key,
            OutcomeLabel = outcome.Label,
            BranchId = NewBranchId(outcome),
            IsNew = true,
            Status = DecisionStatus.Scrutiny,
            LoadETag = etag,
            Nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } }
        };
        return View(BranchEditView, BranchEditViewModel.For(form));
    }

    [HttpPost("admin/rules/branch/transform")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> TransformBranch(BranchEditForm form, CancellationToken ct)
    {
        ApplyTransform(form);
        return Task.FromResult<IActionResult>(View(BranchEditView, BranchEditViewModel.For(form)));
    }

    // Parses "<verb>:<arg>[:<arg2>]" and mutates form.Nodes. No persistence.
    private static void ApplyTransform(BranchEditForm form)
    {
        LeafNormalizer.NormalizeAll(form.Nodes); // apply pending operator/field selections first

        var (verb, args) = SplitAction(form.Action);
        switch (verb)
        {
            case "addCondition": BranchEditTransforms.AddCondition(form.Nodes, int.Parse(args[0])); break;
            case "addGroup": BranchEditTransforms.AddGroup(form.Nodes, int.Parse(args[0])); break;
            case "remove": BranchEditTransforms.Remove(form.Nodes, int.Parse(args[0])); break;
            case "ungroup": BranchEditTransforms.Ungroup(form.Nodes, int.Parse(args[0])); break;
            case "setCombinator":
                BranchEditTransforms.SetCombinator(form.Nodes, int.Parse(args[0]), Enum.Parse<PredicateKind>(args[1])); break;
            case "setField": BranchEditTransforms.SetField(form.Nodes, int.Parse(args[0]), args[1]); break;
            case "addValue": BranchEditTransforms.AddValue(form.Nodes, int.Parse(args[0])); break;
            case "removeValue": BranchEditTransforms.RemoveValue(form.Nodes, int.Parse(args[0]), int.Parse(args[1])); break;
            case "group": BranchEditTransforms.GroupSelected(form.Nodes,
                args[0] == "any" ? PredicateKind.AnyOf : PredicateKind.AllOf); break;
            case "ungroupSelected":
                foreach (var sel in form.Nodes.Where(n => n.Selected
                    && n.Kind is PredicateKind.AllOf or PredicateKind.AnyOf or PredicateKind.Not).ToList())
                {
                    BranchEditTransforms.Ungroup(form.Nodes, sel.Id);
                }
                break;
        }
    }

    private static (string Verb, string[] Args) SplitAction(string? action)
    {
        if (string.IsNullOrEmpty(action)) return ("", Array.Empty<string>());
        var parts = action.Split(':');
        return (parts[0], parts.Skip(1).ToArray());
    }

    private static string NewBranchId(OutcomeRules outcome)
    {
        for (var n = 1; ; n++)
        {
            var candidate = $"{outcome.Key}-{n}";
            if (outcome.Rules.All(b => b.Id != candidate)) return candidate;
        }
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
