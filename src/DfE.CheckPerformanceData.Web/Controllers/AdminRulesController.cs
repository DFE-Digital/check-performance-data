using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Admin surface for the rules engine config. Reads the config (M2), edits branches/
// predicates and lookups (M3), and manages the outcome lifecycle + rollback (M4). Admin-only.
// Views live under Views/Admin/Rules so they inherit the admin layout via the
// Views/Admin/_ViewStart cascade, hence the explicit view paths.
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
    private const string LookupRowEditView = "~/Views/Admin/Rules/LookupRowEdit.cshtml";
    private const string OutcomeAddView = "~/Views/Admin/Rules/OutcomeAdd.cshtml";
    private const string OutcomeDeleteView = "~/Views/Admin/Rules/OutcomeDelete.cshtml";
    private const string RollbackConfirmView = "~/Views/Admin/Rules/RollbackConfirm.cshtml";

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

    [HttpGet("admin/rules/outcomes/add")]
    public IActionResult AddOutcomeForm() =>
        View(OutcomeAddView, AddOutcomeViewModel.For(new AddOutcomeForm()));

    [HttpPost("admin/rules/outcomes/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOutcome(AddOutcomeForm form, CancellationToken ct)
    {
        var (current, etag) = await TryGetRulesAsync(ct);
        if (current is null)
        {
            return View(OutcomeAddView, AddOutcomeViewModel.For(form,
                new[] { "The rules could not be loaded. Reload and try again." }));
        }

        var key = form.Key?.Trim() ?? string.Empty;
        var errors = OutcomeKeyValidator.Validate(key, current.Outcomes.Select(o => o.Key));
        if (errors.Count > 0)
        {
            return View(OutcomeAddView, AddOutcomeViewModel.For(form, errors));
        }

        var label = string.IsNullOrWhiteSpace(form.Label) ? key : form.Label.Trim();
        var outcome = new OutcomeRules(key, label, new[]
        {
            new RuleBranch($"{key}-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance)
        });
        var spliced = RuleSetSplicer.AddOutcome(current, outcome);

        try
        {
            var result = await rules.SaveRulesAsync(spliced, etag, ct);
            if (!result.Saved)
            {
                return View(OutcomeAddView, AddOutcomeViewModel.For(form, result.Errors));
            }
            TempData["SuccessMessage"] =
                $"Outcome '{key}' created (version {result.VersionNumber}). Add its decision branches below. The rules service refreshes within about 5 minutes.";
            return RedirectToAction(nameof(Outcome), new { key });
        }
        catch (RulesConfigConflictException)
        {
            return View(OutcomeAddView, AddOutcomeViewModel.For(form,
                new[] { "The rules were changed by someone else. Nothing was saved — reload and try again." }));
        }
    }

    [HttpGet("admin/rules/outcomes/{key}/delete")]
    public async Task<IActionResult> ConfirmDeleteOutcome(string key, CancellationToken ct)
    {
        if (OutcomeDeletionPolicy.IsFormBound(key))
        {
            return NotFound();
        }

        var (ruleSet, _) = await TryGetRulesAsync(ct);
        var outcome = ruleSet?.Outcomes.FirstOrDefault(o => o.Key == key);
        if (outcome is null)
        {
            return NotFound();
        }

        return View(OutcomeDeleteView, new DeleteOutcomeViewModel
        {
            Key = outcome.Key, Label = outcome.Label, BranchCount = outcome.Rules.Count
        });
    }

    [HttpPost("admin/rules/outcomes/{key}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOutcome(string key, string confirmKey, CancellationToken ct)
    {
        if (OutcomeDeletionPolicy.IsFormBound(key))
        {
            return NotFound();
        }

        var (current, etag) = await TryGetRulesAsync(ct);
        var outcome = current?.Outcomes.FirstOrDefault(o => o.Key == key);
        if (current is null || outcome is null)
        {
            return NotFound();
        }

        DeleteOutcomeViewModel Vm(IReadOnlyList<string> errors) => new()
        {
            Key = outcome.Key, Label = outcome.Label, BranchCount = outcome.Rules.Count, Errors = errors
        };

        if (!string.Equals(confirmKey?.Trim(), key, StringComparison.Ordinal))
        {
            return View(OutcomeDeleteView, Vm(new[] { "Enter the exact outcome key to confirm deletion." }));
        }

        var spliced = RuleSetSplicer.RemoveOutcome(current, key);
        try
        {
            var result = await rules.SaveRulesAsync(spliced, etag, ct);
            if (!result.Saved)
            {
                return View(OutcomeDeleteView, Vm(result.Errors));
            }
            TempData["SuccessMessage"] = $"Outcome '{key}' deleted (version {result.VersionNumber}).";
            return RedirectToAction(nameof(Outcomes));
        }
        catch (RulesConfigConflictException)
        {
            return View(OutcomeDeleteView, Vm(new[] { "The rules were changed by someone else. Nothing was deleted — reload and try again." }));
        }
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
            new BranchViewModel(branch.Id, branch.Status, PredicateDescriber.Describe(branch.When), branch.When is Predicate.Otherwise));
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

    [HttpGet("admin/rules/history/{type}/{id:int}/rollback")]
    public async Task<IActionResult> ConfirmRollback(string type, int id, CancellationToken ct)
    {
        if (!Enum.TryParse<RulesConfigType>(type, ignoreCase: true, out var configType))
        {
            return NotFound();
        }

        var versions = await rules.ListVersionsAsync(configType, ct);
        var dto = versions.FirstOrDefault(v => v.Id == id);
        if (dto is null)
        {
            return NotFound();
        }

        return View(RollbackConfirmView, new RollbackConfirmViewModel
        {
            ConfigType = configType, VersionId = dto.Id, VersionNumber = dto.VersionNumber,
            CreatedAt = dto.CreatedAt, CreatedBy = dto.CreatedBy
        });
    }

    [HttpPost("admin/rules/history/{type}/{id:int}/rollback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rollback(string type, int id, CancellationToken ct)
    {
        if (!Enum.TryParse<RulesConfigType>(type, ignoreCase: true, out var configType))
        {
            return NotFound();
        }

        var versions = await rules.ListVersionsAsync(configType, ct);
        if (versions.All(v => v.Id != id))
        {
            return NotFound();
        }

        var etag = configType == RulesConfigType.Rules
            ? (await TryGetRulesAsync(ct)).ETag
            : (await TryGetLookupsAsync(ct)).ETag;

        try
        {
            var result = await rules.RollbackAsync(id, etag, ct);
            TempData["SuccessMessage"] = result.Saved
                ? $"Rolled back (saved as version {result.VersionNumber})."
                : "Could not roll back: " + string.Join("; ", result.Errors);
        }
        catch (RulesConfigConflictException)
        {
            TempData["SuccessMessage"] = "The config was changed by someone else. Nothing was rolled back — reload and try again.";
        }

        return RedirectToAction(nameof(History), new { type });
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

    [HttpGet("admin/rules/lookups/{code}/edit")]
    public async Task<IActionResult> EditLookupRow(string code, CancellationToken ct)
    {
        var (lookups, etag) = await TryGetLookupsAsync(ct);
        if (lookups is null || !lookups.CountryLanguages.TryGetValue(code, out var langs))
        {
            return NotFound();
        }

        var form = new LookupRowEditForm
        {
            Code = code, IsNew = false, LoadETag = etag, Languages = langs.ToList()
        };
        return View(LookupRowEditView, LookupRowEditViewModel.For(form));
    }

    [HttpGet("admin/rules/lookups/add")]
    public async Task<IActionResult> AddLookupRow(CancellationToken ct)
    {
        var (_, etag) = await TryGetLookupsAsync(ct);
        var form = new LookupRowEditForm
        {
            Code = "", IsNew = true, LoadETag = etag, Languages = new List<string> { "" }
        };
        return View(LookupRowEditView, LookupRowEditViewModel.For(form));
    }

    [HttpPost("admin/rules/lookups/row/transform")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> TransformLookupRow(LookupRowEditForm form, CancellationToken ct)
    {
        var (verb, args) = SplitAction(form.Action);
        if (verb == "addLanguage") form.Languages.Add("");
        else if (verb == "removeLanguage" && int.TryParse(args.ElementAtOrDefault(0), out var idx)
                 && idx >= 0 && idx < form.Languages.Count) form.Languages.RemoveAt(idx);

        return Task.FromResult<IActionResult>(View(LookupRowEditView, LookupRowEditViewModel.For(form)));
    }

    [HttpPost("admin/rules/lookups/{code}/save")]
    [HttpPost("admin/rules/lookups/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLookupRow(LookupRowEditForm form, CancellationToken ct)
    {
        var languages = form.Languages.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var (current, currentETag) = await TryGetLookupsAsync(ct);
        if (currentETag != form.LoadETag)
        {
            return View(LookupRowEditView, LookupRowEditViewModel.For(form,
                new[] { "The lookups were changed by someone else since you opened this page. Reload and try again." }));
        }

        var map = current?.CountryLanguages.ToDictionary(kv => kv.Key, kv => kv.Value)
                  ?? new Dictionary<string, IReadOnlyList<string>>();
        map[form.Code.Trim()] = languages;
        var merged = new Lookups(map);

        var validator = new LookupsValidator();
        var validation = validator.Validate(merged);
        if (!validation.IsValid)
        {
            return View(LookupRowEditView, LookupRowEditViewModel.For(form, validation.Errors));
        }

        try
        {
            var result = await rules.SaveLookupsAsync(merged, form.LoadETag, ct);
            if (!result.Saved)
            {
                return View(LookupRowEditView, LookupRowEditViewModel.For(form, result.Errors));
            }
            TempData["SuccessMessage"] =
                $"Lookup '{form.Code}' saved (version {result.VersionNumber}). The rules service refreshes within about 5 minutes.";
            return RedirectToAction(nameof(Lookups));
        }
        catch (RulesConfigConflictException)
        {
            return View(LookupRowEditView, LookupRowEditViewModel.For(form,
                new[] { "The lookups were changed by someone else. Nothing was saved — reload and try again." }));
        }
    }

    [HttpPost("admin/rules/lookups/{code}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLookupRow(string code, CancellationToken ct)
    {
        var (current, etag) = await TryGetLookupsAsync(ct);
        if (current is null) return NotFound();

        var map = current.CountryLanguages.Where(kv => kv.Key != code)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        try
        {
            var result = await rules.SaveLookupsAsync(new Lookups(map), etag, ct);
            TempData["SuccessMessage"] = result.Saved
                ? $"Lookup '{code}' removed (version {result.VersionNumber})."
                : "Could not remove the lookup: " + string.Join("; ", result.Errors);
        }
        catch (RulesConfigConflictException)
        {
            TempData["SuccessMessage"] = "The lookups were changed by someone else. Nothing was removed — reload and try again.";
        }
        return RedirectToAction(nameof(Lookups));
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
            case "setField": BranchEditTransforms.SetField(form.Nodes, int.Parse(args[0])); break;
            case "addValue": BranchEditTransforms.AddValue(form.Nodes, int.Parse(args[0])); break;
            case "removeValue": BranchEditTransforms.RemoveValue(form.Nodes, int.Parse(args[0]), int.Parse(args[1])); break;
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
