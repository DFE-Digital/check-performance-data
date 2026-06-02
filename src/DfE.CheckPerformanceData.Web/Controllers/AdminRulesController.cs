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

    [HttpPost("admin/rules/branch/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBranch(BranchEditForm form, CancellationToken ct)
    {
        LeafNormalizer.NormalizeAll(form.Nodes);

        var structural = PredicateFormValidator.Validate(form.Nodes);
        if (structural.Count > 0)
        {
            return View(BranchEditView, BranchEditViewModel.For(form, structural));
        }

        var (current, currentETag) = await TryGetRulesAsync(ct);
        if (current is null)
        {
            return View(BranchEditView, BranchEditViewModel.For(form,
                new[] { "The rules could not be loaded. Reload and try again." }));
        }
        // App-level check catches the common case (two admins editing at once) cheaply.
        // The service's blob If-Match below is the authoritative guard against the residual race.
        if (currentETag != form.LoadETag)
        {
            return View(BranchEditView, BranchEditViewModel.For(form, concurrencyConflict: true));
        }

        if (!current.Outcomes.Any(o => o.Key == form.OutcomeKey))
        {
            return View(BranchEditView, BranchEditViewModel.For(form,
                new[] { $"Outcome '{form.OutcomeKey}' no longer exists. Reload and try again." }));
        }

        var predicate = PredicateForm.RebuildPredicate(form.Nodes);
        var branch = new RuleBranch(form.BranchId, form.Status, predicate);
        var spliced = form.IsNew
            ? RuleSetSplicer.InsertBranch(current, form.OutcomeKey, branch)
            : RuleSetSplicer.ReplaceBranch(current, form.OutcomeKey, form.BranchId, branch);

        RulesConfigSaveResult result;
        try
        {
            result = await rules.SaveRulesAsync(spliced, form.LoadETag, ct);
        }
        catch (RulesConfigConflictException)
        {
            return View(BranchEditView, BranchEditViewModel.For(form, concurrencyConflict: true));
        }

        if (!result.Saved)
        {
            return View(BranchEditView, BranchEditViewModel.For(form, result.Errors));
        }

        TempData["SuccessMessage"] =
            $"Branch '{form.BranchId}' saved (version {result.VersionNumber}). The rules service refreshes within about 5 minutes.";
        return RedirectToAction(nameof(Outcome), new { key = form.OutcomeKey });
    }

    [HttpGet("admin/rules/outcomes/{key}/branches/{id}/remove")]
    public async Task<IActionResult> ConfirmRemoveBranch(string key, string id, CancellationToken ct)
    {
        var (ruleSet, _) = await TryGetRulesAsync(ct);
        var branch = ruleSet?.Outcomes.FirstOrDefault(o => o.Key == key)?.Rules.FirstOrDefault(b => b.Id == id);
        if (branch is null || branch.When is Predicate.Otherwise)
        {
            return NotFound();
        }

        ViewData["OutcomeKey"] = key;
        return View(RemoveBranchView,
            new BranchViewModel(branch.Id, branch.Status, PredicateDescriber.Describe(branch.When)));
    }

    [HttpPost("admin/rules/outcomes/{key}/branches/{id}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBranch(string key, string id, CancellationToken ct)
    {
        var (current, etag) = await TryGetRulesAsync(ct);
        if (current is null) return NotFound();

        RuleSet spliced;
        try { spliced = RuleSetSplicer.RemoveBranch(current, key, id); }
        catch (InvalidOperationException) { return NotFound(); }

        try
        {
            var result = await rules.SaveRulesAsync(spliced, etag, ct);
            TempData["SuccessMessage"] = result.Saved
                ? $"Branch '{id}' removed (version {result.VersionNumber})."
                : "Could not remove the branch: " + string.Join("; ", result.Errors);
        }
        catch (RulesConfigConflictException)
        {
            TempData["SuccessMessage"] = "The rules were changed by someone else. Nothing was removed — reload and try again.";
        }
        return RedirectToAction(nameof(Outcome), new { key });
    }

    [HttpPost("admin/rules/outcomes/{key}/branches/{id}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveBranch(string key, string id, string direction, CancellationToken ct)
    {
        var (current, etag) = await TryGetRulesAsync(ct);
        if (current is null) return NotFound();

        var spliced = RuleSetSplicer.MoveBranch(current, key, id, up: direction == "up");
        try
        {
            var result = await rules.SaveRulesAsync(spliced, etag, ct);
            if (!result.Saved)
            {
                TempData["SuccessMessage"] = "Could not reorder: " + string.Join("; ", result.Errors);
            }
        }
        catch (RulesConfigConflictException)
        {
            TempData["SuccessMessage"] = "The rules were changed by someone else. Nothing was reordered — reload and try again.";
        }
        return RedirectToAction(nameof(Outcome), new { key });
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
