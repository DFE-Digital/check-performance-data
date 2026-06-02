# Admin Rules Editor — Milestone 2 (read-only surface) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an admin-only, GET-only `/admin/rules` surface that renders the current rules and lookups config (outcomes, decision branches with human-readable predicates, country-languages, and version history) using the Milestone 1 `IRulesConfigService` — no editing or saving yet.

**Architecture:** A new `AdminRulesController` (`[Authorize(Roles = WikiConstants.AdminRole)]`, GET-only) reads via the M1 `IRulesConfigService` (`GetRulesAsync`/`GetLookupsAsync`/`ListVersionsAsync`). A pure `PredicateDescriber` turns a `Predicate` tree into a renderable `PredicateNode` tree; a pure `RulesAdminViewModelFactory` maps `RuleSet`/`Lookups`/version DTOs into view models. Razor views live under `Views/Admin/Rules/` so they inherit the admin layout via the `Views/Admin/_ViewStart` cascade. A new enabled `RulesConfigNavEntry` surfaces the tile under System administration. `RulesConfigNotFoundException` (thrown by the service on a never-seeded environment) is caught and rendered as a GOV.UK empty state, not a 500.

**Tech Stack:** .NET 10 / C# 12, ASP.NET Core MVC + Razor, GOV.UK Design System (GovUk.Frontend.AspNetCore), xUnit + NSubstitute (unit), Playwright + impersonation helpers (E2E).

---

## Conventions you MUST follow (read before starting)

- **Tests are xUnit, not NUnit** (CLAUDE.md is wrong on this). Unit test project root namespace is `DfE.CheckPerformanceData.Application.UnitTests`; there is a global `<Using Include="Xunit" />` so **do not** add `using Xunit;`. Test classes are `public sealed class`. Use `[Fact]`, `Assert.Equal/True/False/Contains/IsType`. NSubstitute is available (`using NSubstitute;`).
- **No SPA.** Server-rendered Razor only. JS is enhancement only — none is required for M2.
- **GOV.UK / DfE Design System** for all markup (`govuk-*` classes, error summary, tag, table, summary-list, notification-banner patterns). WCAG 2.2 AA: semantic headings, real `<table>`/`<ul>`, visible focus, `aria-*` only where the GOV.UK component requires it.
- **Explicit view paths** in the controller (e.g. `~/Views/Admin/Rules/Index.cshtml`) so the admin layout cascades. Mirror `AdminSettingsController`.
- **Build/test commands** (run from `C:\Repos\DfE\check-performance-data`):
  - Build: `dotnet build`
  - Unit tests: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj`
  - A single unit test: append `--filter "FullyQualifiedName~<ClassName>"`
  - E2E tests require the local stack running (`docker compose --profile all`) and are slow; run them only in Task 8.
- **Commits:** the MAIN agent commits per task (subagents cannot). Use Conventional Commits, scope `rules`.
- All new Web types for this feature live under `src/DfE.CheckPerformanceData.Web/Admin/Rules/` (cohesion — feature files live together). Views live under `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/`.

## Information architecture (the screens this milestone builds)

1. **Landing** `GET /admin/rules` — two cards: **Decision rules** (`rules.json`) and **Country languages** (`country-languages.json`). Each card shows current version + last-updated + last-saved-by + a count (outcomes / countries) and links to its detail and version history. A blue inset text explains the ~5-minute worker propagation delay. Each card independently shows a "Not yet configured" empty state if its blob is missing.
2. **Outcomes list** `GET /admin/rules/outcomes` — table of outcomes (key, label, branch count), each linking to its detail. Empty state if rules blob missing.
3. **Outcome detail** `GET /admin/rules/outcomes/{key}` — ordered branch list; each branch shows Id, a status tag, and the `When` predicate rendered as a nested readable tree. `404` if the key is unknown.
4. **Lookups** `GET /admin/rules/lookups` — table of country code → official languages. Empty state if lookups blob missing.
5. **Version history** `GET /admin/rules/history/{type}` — table of versions (number, created at, created by) for `Rules` or `Lookups`, each linking to detail. `404` if `{type}` is not a valid `RulesConfigType`.
6. **Version detail** `GET /admin/rules/history/{type}/{id}` — version metadata plus the raw stored JSON in a `<pre>`. `404` if the version id is unknown for that type.

## File structure (created/modified this milestone)

- Create `src/DfE.CheckPerformanceData.Web/Admin/Nav/RulesConfigNavEntry.cs` — enabled tile → `/admin/rules`.
- Modify `src/DfE.CheckPerformanceData.Web/Admin/Nav/AdminNavKeys.cs` — add `RulesConfig` key.
- Modify `src/DfE.CheckPerformanceData.Web/Extensions/AdminNavServiceCollectionExtensions.cs` — register the new entry.
- Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateNode.cs` — renderable predicate tree node.
- Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateDescriber.cs` — `Predicate` → `PredicateNode`.
- Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModels.cs` — all read-only view-model records.
- Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModelFactory.cs` — pure mapping `RuleSet`/`Lookups`/DTOs → view models.
- Create `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs` — GET-only controller.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Index.cshtml` — landing.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcomes.cshtml` — outcomes list.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcome.cshtml` — outcome detail.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_PredicateNode.cshtml` — recursive predicate partial.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Lookups.cshtml` — lookups table.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/History.cshtml` — version history list.
- Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Version.cshtml` — version detail.
- Create tests under `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/` and `.../Web/Controllers/`, and `tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminRulesAuthTests.cs`.

---

## Task 1: Add the `Rules configuration` admin nav entry

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Nav/RulesConfigNavEntry.cs`
- Modify: `src/DfE.CheckPerformanceData.Web/Admin/Nav/AdminNavKeys.cs`
- Modify: `src/DfE.CheckPerformanceData.Web/Extensions/AdminNavServiceCollectionExtensions.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/AdminNavRegistryTests.cs` (existing — update assertions)

Context: There is already a **disabled** `RulesEngineNavEntry` (Title "Rules engine", `Order = 20`, purpose: future queue-depth observability). Leave it untouched — this is a different, enabled tile for the config editor. Both sit under `system-admin`. The existing registry tests assert exactly 9 entries and `systemOrders == [10, 20]`; adding this entry makes them 10 and `[10, 20, 30]`, so those two assertions must be updated in the same task.

- [ ] **Step 1: Update the failing registry assertions**

In `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/AdminNavRegistryTests.cs`, change the count assertion in `AddAdminNavEntries_Registers_Nine_Hierarchical_Entries`:

```csharp
		Assert.Equal(10, entries.Count);
```

Add a title assertion in the same test, after the `"Rules engine"` line:

```csharp
		Assert.Contains("Rules configuration", titles);
```

In `Tiles_Within_Each_Group_Have_Distinct_Orders_Per_UI_Spec`, change the system-admin expectation:

```csharp
		Assert.Equal(new[] { 10, 20, 30 }, systemOrders);
```

Add a new test at the end of the class:

```csharp
	// --- RulesConfig_Tile_Is_Enabled_SystemAdmin_Child_LinkingToAdminRules ---

	[Fact]
	public void RulesConfig_Tile_Is_Enabled_SystemAdmin_Child_LinkingToAdminRules()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "rules-config");

		Assert.Equal("system-admin", entry.ParentKey);
		Assert.Equal("/admin/rules", entry.Url);
		Assert.Equal("GET", entry.HttpMethod);
		Assert.Equal(30, entry.Order);
		Assert.True(entry.Enabled);
	}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminNavRegistryTests"`
Expected: FAIL — count is 9 not 10; no entry with Key `rules-config`.

- [ ] **Step 3: Add the nav key**

In `src/DfE.CheckPerformanceData.Web/Admin/Nav/AdminNavKeys.cs`, add inside the class:

```csharp
    public const string RulesConfig = "rules-config";
```

- [ ] **Step 4: Create the nav entry**

Create `src/DfE.CheckPerformanceData.Web/Admin/Nav/RulesConfigNavEntry.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Enabled tile linking to the read-only rules configuration surface (Milestone 2).
// Distinct from the disabled RulesEngineNavEntry, which is a placeholder for future
// queue-depth observability. Both live under the System administration group.
public sealed record RulesConfigNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.RulesConfig;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Rules configuration";
    public string Description => "View the decision rules and country-language lookups, and their version history.";
    public string Url => "/admin/rules";
    public bool Enabled => true;
    public int Order => 30;
}
```

Note: `IAdminNavEntry.HttpMethod` has a default of `=> "GET"` on the interface (confirmed), so this record does not override it. The registry test's `Assert.Equal("GET", entry.HttpMethod)` passes via the default.

- [ ] **Step 5: Register the entry**

In `src/DfE.CheckPerformanceData.Web/Extensions/AdminNavServiceCollectionExtensions.cs`, add after the `RulesEngineNavEntry` registration:

```csharp
        services.AddSingleton<IAdminNavEntry, RulesConfigNavEntry>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminNavRegistryTests"`
Expected: PASS (all registry tests green).

- [ ] **Step 7: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Nav/RulesConfigNavEntry.cs src/DfE.CheckPerformanceData.Web/Admin/Nav/AdminNavKeys.cs src/DfE.CheckPerformanceData.Web/Extensions/AdminNavServiceCollectionExtensions.cs tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/AdminNavRegistryTests.cs
git commit -m "feat(rules): add enabled Rules configuration admin nav entry"
```

---

## Task 2: `PredicateDescriber` — render a predicate tree as readable text

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateNode.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateDescriber.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateDescriberTests.cs`

Context: `Predicate` (`Application/RulesEngine/Predicate.cs`) is a discriminated union: composites `AllOf`/`AnyOf`/`Not` and leaves `FieldEq`/`FieldNeq`/`FieldIn`/`FieldCompare`/`IsKnownAndCertain`/`OfficialLanguageIs`/`Otherwise`. Literals are `FieldValue` (`Str`/`Bool`/`Num`/`Date`/`Unknown`/`Uncertain(inner)`). `CompareOp` is `Lt|Lte|Gt|Gte`. The describer is a pure function reused by the outcome-detail view (and later by M3). It returns a tree so the Razor partial can render nested `<ul>`s for accessibility rather than a flat string.

- [ ] **Step 1: Write the failing tests**

Create `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateDescriberTests.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateDescriberTests
{
    private static PredicateNode Describe(Predicate p) => PredicateDescriber.Describe(p);

    [Fact]
    public void FieldEq_string_renders_field_is_value()
    {
        var node = Describe(new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")));
        Assert.True(node.IsLeaf);
        Assert.Equal("keyStage is \"KS4\"", node.Text);
    }

    [Fact]
    public void FieldNeq_bool_renders_is_not()
    {
        var node = Describe(new Predicate.FieldNeq("isAddBack", new FieldValue.Bool(true)));
        Assert.Equal("isAddBack is not true", node.Text);
    }

    [Fact]
    public void FieldIn_lists_values()
    {
        var node = Describe(new Predicate.FieldIn("inclusionFlag",
            new FieldValue[] { new FieldValue.Str("A"), new FieldValue.Str("B") }));
        Assert.Equal("inclusionFlag is one of: \"A\", \"B\"", node.Text);
    }

    [Fact]
    public void FieldCompare_renders_operator_phrase()
    {
        var node = Describe(new Predicate.FieldCompare("pupilAge", CompareOp.Gte, new FieldValue.Num(16m)));
        Assert.Equal("pupilAge is greater than or equal to 16", node.Text);
    }

    [Fact]
    public void IsKnownAndCertain_renders()
    {
        var node = Describe(new Predicate.IsKnownAndCertain("countryOfOrigin"));
        Assert.Equal("countryOfOrigin is known and certain", node.Text);
    }

    [Fact]
    public void OfficialLanguageIs_renders()
    {
        var node = Describe(new Predicate.OfficialLanguageIs("countryOfOrigin", "English"));
        Assert.Equal("\"English\" is an official language of the country in countryOfOrigin", node.Text);
    }

    [Fact]
    public void Otherwise_renders_catch_all()
    {
        var node = Describe(Predicate.Otherwise.Instance);
        Assert.Equal("Otherwise (always matches)", node.Text);
    }

    [Fact]
    public void Date_value_renders_iso()
    {
        var node = Describe(new Predicate.FieldEq("schoolAdmissionDate",
            new FieldValue.Date(new DateOnly(2025, 9, 1))));
        Assert.Equal("schoolAdmissionDate is 2025-09-01", node.Text);
    }

    [Fact]
    public void Uncertain_value_is_annotated()
    {
        var node = Describe(new Predicate.FieldEq("firstLanguage",
            new FieldValue.Uncertain(new FieldValue.Str("French"))));
        Assert.Equal("firstLanguage is \"French\" (uncertain)", node.Text);
    }

    [Fact]
    public void Unknown_value_renders_unknown()
    {
        var node = Describe(new Predicate.FieldEq("firstLanguage", FieldValue.Unknown.Instance));
        Assert.Equal("firstLanguage is unknown", node.Text);
    }

    [Fact]
    public void AllOf_has_header_and_children()
    {
        var node = Describe(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")),
            new Predicate.IsKnownAndCertain("pupilAge")
        }));

        Assert.False(node.IsLeaf);
        Assert.Equal("All of the following are true:", node.Text);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("keyStage is \"KS4\"", node.Children[0].Text);
        Assert.Equal("pupilAge is known and certain", node.Children[1].Text);
    }

    [Fact]
    public void AnyOf_has_header()
    {
        var node = Describe(new Predicate.AnyOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2"))
        }));
        Assert.Equal("Any of the following are true:", node.Text);
        Assert.Single(node.Children);
    }

    [Fact]
    public void Not_wraps_single_child()
    {
        var node = Describe(new Predicate.Not(new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))));
        Assert.Equal("Not true:", node.Text);
        Assert.Single(node.Children);
        Assert.Equal("keyStage is \"KS4\"", node.Children[0].Text);
    }

    [Fact]
    public void Nested_composites_describe_recursively()
    {
        var node = Describe(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.AnyOf(new Predicate[]
            {
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2")),
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))
            }),
            new Predicate.Not(new Predicate.IsKnownAndCertain("pupilAge"))
        }));

        Assert.Equal("All of the following are true:", node.Text);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("Any of the following are true:", node.Children[0].Text);
        Assert.Equal(2, node.Children[0].Children.Count);
        Assert.Equal("Not true:", node.Children[1].Text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~PredicateDescriberTests"`
Expected: FAIL — `PredicateNode`/`PredicateDescriber` do not exist (compile error).

- [ ] **Step 3: Create the node type**

Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateNode.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// A renderable node in a described predicate tree. Leaves carry a single phrase
/// (e.g. "keyStage is \"KS4\""); composites carry a header (e.g. "All of the
/// following are true:") plus child nodes. The recursive Razor partial renders
/// composites as a nested list for accessibility.
/// </summary>
public sealed record PredicateNode(string Text, IReadOnlyList<PredicateNode> Children)
{
    public bool IsLeaf => Children.Count == 0;

    public static PredicateNode Leaf(string text) => new(text, Array.Empty<PredicateNode>());
}
```

- [ ] **Step 4: Create the describer**

Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateDescriber.cs`:

```csharp
using System.Globalization;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure rendering of a <see cref="Predicate"/> tree into a human-readable
/// <see cref="PredicateNode"/> tree for the read-only admin views. No I/O, no state.
/// </summary>
public static class PredicateDescriber
{
    public static PredicateNode Describe(Predicate predicate) => predicate switch
    {
        Predicate.AllOf all => new PredicateNode(
            "All of the following are true:",
            all.Items.Select(Describe).ToList()),

        Predicate.AnyOf any => new PredicateNode(
            "Any of the following are true:",
            any.Items.Select(Describe).ToList()),

        Predicate.Not not => new PredicateNode(
            "Not true:",
            new[] { Describe(not.Inner) }),

        Predicate.FieldEq eq => PredicateNode.Leaf($"{eq.Field} is {Value(eq.Value)}"),
        Predicate.FieldNeq neq => PredicateNode.Leaf($"{neq.Field} is not {Value(neq.Value)}"),
        Predicate.FieldIn fin => PredicateNode.Leaf(
            $"{fin.Field} is one of: {string.Join(", ", fin.Values.Select(Value))}"),
        Predicate.FieldCompare cmp => PredicateNode.Leaf(
            $"{cmp.Field} {Op(cmp.Op)} {Value(cmp.Value)}"),
        Predicate.IsKnownAndCertain known => PredicateNode.Leaf(
            $"{known.Field} is known and certain"),
        Predicate.OfficialLanguageIs lang => PredicateNode.Leaf(
            $"\"{lang.Language}\" is an official language of the country in {lang.CountryField}"),
        Predicate.Otherwise => PredicateNode.Leaf("Otherwise (always matches)"),

        _ => PredicateNode.Leaf("Unknown condition")
    };

    private static string Op(CompareOp op) => op switch
    {
        CompareOp.Lt => "is less than",
        CompareOp.Lte => "is less than or equal to",
        CompareOp.Gt => "is greater than",
        CompareOp.Gte => "is greater than or equal to",
        _ => op.ToString()
    };

    private static string Value(FieldValue value) => value switch
    {
        FieldValue.Str s => $"\"{s.Value}\"",
        FieldValue.Bool b => b.Value ? "true" : "false",
        FieldValue.Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldValue.Uncertain u => $"{Value(u.Inner)} (uncertain)",
        FieldValue.Unknown => "unknown",
        _ => "unknown"
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~PredicateDescriberTests"`
Expected: PASS (all describer tests green).

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateNode.cs src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateDescriber.cs tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateDescriberTests.cs
git commit -m "feat(rules): add PredicateDescriber for read-only predicate rendering"
```

---

## Task 3: Read-only view models + `RulesAdminViewModelFactory`

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModels.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModelFactory.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/RulesAdminViewModelFactoryTests.cs`

Context: The controller hands the factory a `RuleSet` / `Lookups` / list of `RulesConfigVersionDto` (M1 `Application/RulesConfig/RulesConfigVersionDto.cs`) plus the latest version metadata, and gets back view models. Keeping mapping in a pure factory makes it unit-testable without spinning up MVC. `RuleSet(Version, UpdatedAt, Outcomes[])`, `OutcomeRules(Key, Label, Rules[])`, `RuleBranch(Id, Status, When)`, `Lookups(CountryLanguages: IReadOnlyDictionary<string, IReadOnlyList<string>>)`. `DecisionStatus` is `AutoApproved|AutoRejected|Scrutiny`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/RulesAdminViewModelFactoryTests.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class RulesAdminViewModelFactoryTests
{
    private static RuleBranch Otherwise(string id) =>
        new(id, DecisionStatus.Scrutiny, Predicate.Otherwise.Instance);

    private static OutcomeRules Outcome(string key, string label, params RuleBranch[] branches) =>
        new(key, label, branches);

    private static RuleSet SampleRules() => new(
        "v3",
        new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
        new[]
        {
            Outcome("Inclusion", "Inclusion",
                new RuleBranch("INC-1", DecisionStatus.AutoApproved,
                    new Predicate.FieldEq("inclusionFlag", new FieldValue.Str("Y"))),
                Otherwise("INC-DEF")),
            Outcome("EAL", "English as an additional language", Otherwise("EAL-DEF"))
        });

    [Fact]
    public void RulesCard_summarises_version_count_and_latest_save()
    {
        var latest = new RulesConfigVersionDto
        {
            Id = 7, ConfigType = RulesConfigType.Rules, VersionNumber = 4,
            CreatedAt = new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc), CreatedBy = "Ada"
        };

        var card = RulesAdminViewModelFactory.RulesCard(SampleRules(), latest);

        Assert.Equal("v3", card.Version);
        Assert.Equal(2, card.ItemCount);
        Assert.Equal(4, card.LatestVersionNumber);
        Assert.Equal("Ada", card.LastSavedBy);
        Assert.False(card.IsEmpty);
    }

    [Fact]
    public void RulesCard_empty_when_rules_null()
    {
        var card = RulesAdminViewModelFactory.RulesCard(null, null);
        Assert.True(card.IsEmpty);
        Assert.Equal(0, card.ItemCount);
    }

    [Fact]
    public void Outcomes_listing_maps_key_label_and_branch_count_in_order()
    {
        var vm = RulesAdminViewModelFactory.Outcomes(SampleRules());

        Assert.Equal(2, vm.Outcomes.Count);
        Assert.Equal("Inclusion", vm.Outcomes[0].Key);
        Assert.Equal("Inclusion", vm.Outcomes[0].Label);
        Assert.Equal(2, vm.Outcomes[0].BranchCount);
        Assert.Equal("EAL", vm.Outcomes[1].Key);
        Assert.Equal(1, vm.Outcomes[1].BranchCount);
    }

    [Fact]
    public void Outcome_detail_describes_each_branch_predicate()
    {
        var vm = RulesAdminViewModelFactory.Outcome(SampleRules(), "Inclusion");

        Assert.NotNull(vm);
        Assert.Equal("Inclusion", vm!.Key);
        Assert.Equal(2, vm.Branches.Count);
        Assert.Equal("INC-1", vm.Branches[0].Id);
        Assert.Equal(DecisionStatus.AutoApproved, vm.Branches[0].Status);
        Assert.Equal("inclusionFlag is \"Y\"", vm.Branches[0].Condition.Text);
        Assert.Equal("Otherwise (always matches)", vm.Branches[1].Condition.Text);
    }

    [Fact]
    public void Outcome_detail_returns_null_for_unknown_key()
    {
        Assert.Null(RulesAdminViewModelFactory.Outcome(SampleRules(), "DoesNotExist"));
    }

    [Fact]
    public void LookupsCard_counts_countries()
    {
        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>>
        {
            ["GB"] = new[] { "English", "Welsh" },
            ["FR"] = new[] { "French" }
        });

        var card = RulesAdminViewModelFactory.LookupsCard(lookups, null);
        Assert.Equal(2, card.ItemCount);
        Assert.False(card.IsEmpty);
    }

    [Fact]
    public void Lookups_listing_sorts_rows_by_country_code()
    {
        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>>
        {
            ["FR"] = new[] { "French" },
            ["GB"] = new[] { "English", "Welsh" }
        });

        var vm = RulesAdminViewModelFactory.Lookups(lookups);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("FR", vm.Rows[0].CountryCode);
        Assert.Equal("GB", vm.Rows[1].CountryCode);
        Assert.Equal("English, Welsh", vm.Rows[1].Languages);
    }

    [Fact]
    public void History_maps_versions_newest_first()
    {
        var versions = new List<RulesConfigVersionDto>
        {
            new() { Id = 1, ConfigType = RulesConfigType.Rules, VersionNumber = 1,
                    CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "A", Content = "{}" },
            new() { Id = 2, ConfigType = RulesConfigType.Rules, VersionNumber = 2,
                    CreatedAt = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "B", Content = "{}" }
        };

        var vm = RulesAdminViewModelFactory.History(RulesConfigType.Rules, versions);

        Assert.Equal(RulesConfigType.Rules, vm.ConfigType);
        Assert.Equal(2, vm.Versions.Count);
        Assert.Equal(2, vm.Versions[0].VersionNumber); // newest first
        Assert.Equal(1, vm.Versions[1].VersionNumber);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~RulesAdminViewModelFactoryTests"`
Expected: FAIL — factory and view-model types do not exist (compile error).

- [ ] **Step 3: Create the view-model records**

Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModels.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>Summary card on the landing page for one config blob.</summary>
public sealed record RulesConfigCardViewModel
{
    public bool IsEmpty { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public int ItemCount { get; init; }            // outcomes (rules) or countries (lookups)
    public string ItemNoun { get; init; } = string.Empty;
    public int? LatestVersionNumber { get; init; }
    public DateTime? LastSavedAt { get; init; }
    public string? LastSavedBy { get; init; }
}

public sealed record RulesLandingViewModel
{
    public required RulesConfigCardViewModel Rules { get; init; }
    public required RulesConfigCardViewModel Lookups { get; init; }
}

public sealed record OutcomeSummaryViewModel(string Key, string Label, int BranchCount);

public sealed record OutcomesViewModel
{
    public required IReadOnlyList<OutcomeSummaryViewModel> Outcomes { get; init; }
    public bool IsEmpty => Outcomes.Count == 0;
}

public sealed record BranchViewModel(string Id, DecisionStatus Status, PredicateNode Condition);

public sealed record OutcomeDetailViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<BranchViewModel> Branches { get; init; }
}

public sealed record LookupRowViewModel(string CountryCode, string Languages);

public sealed record LookupsViewModel
{
    public required IReadOnlyList<LookupRowViewModel> Rows { get; init; }
    public bool IsEmpty => Rows.Count == 0;
}

public sealed record VersionRowViewModel(int Id, int VersionNumber, DateTime CreatedAt, string? CreatedBy);

public sealed record HistoryViewModel
{
    public required RulesConfigType ConfigType { get; init; }
    public required IReadOnlyList<VersionRowViewModel> Versions { get; init; }
}

public sealed record VersionDetailViewModel
{
    public required RulesConfigType ConfigType { get; init; }
    public required int VersionNumber { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public required string Content { get; init; }
}
```

- [ ] **Step 4: Create the factory**

Create `src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModelFactory.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>Pure mapping from domain config types to read-only admin view models.</summary>
public static class RulesAdminViewModelFactory
{
    public static RulesConfigCardViewModel RulesCard(RuleSet? rules, RulesConfigVersionDto? latest)
    {
        if (rules is null)
        {
            return new RulesConfigCardViewModel { IsEmpty = true, ItemNoun = "outcomes" };
        }

        return new RulesConfigCardViewModel
        {
            IsEmpty = false,
            Version = rules.Version,
            UpdatedAt = rules.UpdatedAt,
            ItemCount = rules.Outcomes.Count,
            ItemNoun = "outcomes",
            LatestVersionNumber = latest?.VersionNumber,
            LastSavedAt = latest?.CreatedAt,
            LastSavedBy = latest?.CreatedBy
        };
    }

    public static RulesConfigCardViewModel LookupsCard(Lookups? lookups, RulesConfigVersionDto? latest)
    {
        if (lookups is null)
        {
            return new RulesConfigCardViewModel { IsEmpty = true, ItemNoun = "countries" };
        }

        return new RulesConfigCardViewModel
        {
            IsEmpty = false,
            ItemCount = lookups.CountryLanguages.Count,
            ItemNoun = "countries",
            LatestVersionNumber = latest?.VersionNumber,
            LastSavedAt = latest?.CreatedAt,
            LastSavedBy = latest?.CreatedBy
        };
    }

    public static OutcomesViewModel Outcomes(RuleSet? rules) => new()
    {
        Outcomes = rules is null
            ? Array.Empty<OutcomeSummaryViewModel>()
            : rules.Outcomes
                .Select(o => new OutcomeSummaryViewModel(o.Key, o.Label, o.Rules.Count))
                .ToList()
    };

    public static OutcomeDetailViewModel? Outcome(RuleSet? rules, string key)
    {
        var outcome = rules?.Outcomes.FirstOrDefault(o =>
            string.Equals(o.Key, key, StringComparison.Ordinal));
        if (outcome is null) return null;

        return new OutcomeDetailViewModel
        {
            Key = outcome.Key,
            Label = outcome.Label,
            Branches = outcome.Rules
                .Select(b => new BranchViewModel(b.Id, b.Status, PredicateDescriber.Describe(b.When)))
                .ToList()
        };
    }

    public static LookupsViewModel Lookups(Lookups? lookups) => new()
    {
        Rows = lookups is null
            ? Array.Empty<LookupRowViewModel>()
            : lookups.CountryLanguages
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new LookupRowViewModel(kv.Key, string.Join(", ", kv.Value)))
                .ToList()
    };

    public static HistoryViewModel History(RulesConfigType type, IReadOnlyList<RulesConfigVersionDto> versions) => new()
    {
        ConfigType = type,
        Versions = versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new VersionRowViewModel(v.Id, v.VersionNumber, v.CreatedAt, v.CreatedBy))
            .ToList()
    };

    public static VersionDetailViewModel VersionDetail(RulesConfigVersionDto dto) => new()
    {
        ConfigType = dto.ConfigType,
        VersionNumber = dto.VersionNumber,
        CreatedAt = dto.CreatedAt,
        CreatedBy = dto.CreatedBy,
        Content = dto.Content
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~RulesAdminViewModelFactoryTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModels.cs src/DfE.CheckPerformanceData.Web/Admin/Rules/RulesAdminViewModelFactory.cs tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/RulesAdminViewModelFactoryTests.cs
git commit -m "feat(rules): add read-only admin view models and mapping factory"
```

---

## Task 4: `AdminRulesController` + landing view (with empty state)

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Index.cshtml`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs`

Context: Mirror `AdminSettingsController` (explicit view paths, constructor injection). Gate the whole controller with `[Authorize(Roles = WikiConstants.AdminRole)]`. Inject `IRulesConfigService` (M1 `Application/RulesConfig/IRulesConfigService.cs`): `GetRulesAsync()→(RuleSet, ETag)`, `GetLookupsAsync()→(Lookups, ETag)`, `ListVersionsAsync(RulesConfigType)`, both Get methods throw `RulesConfigNotFoundException` (M1 `Application/RulesConfig/`) when the blob has never been seeded. Catch it **per config** so one missing blob still renders the other. Latest version = first item of `ListVersionsAsync` ordered desc (the factory's `History` already sorts; for the card just pass the max-by-VersionNumber DTO or `null`).

This task builds the landing only; later tasks add the other actions to the same controller.

- [ ] **Step 1: Write the failing tests**

Create `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs`:

```csharp
using System.Reflection;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class AdminRulesControllerTests
{
    private static RuleSet SampleRules() => new(
        "v1", DateTimeOffset.UnixEpoch,
        new[]
        {
            new OutcomeRules("Inclusion", "Inclusion",
                new[] { new RuleBranch("INC-DEF", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance) })
        });

    private static Lookups SampleLookups() => new(new Dictionary<string, IReadOnlyList<string>>
    {
        ["GB"] = new[] { "English" }
    });

    private static AdminRulesController NewController(IRulesConfigService svc) => new(svc);

    [Fact]
    public void Controller_Has_Authorize_AdminRole()
    {
        var authorize = typeof(AdminRulesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public void Index_Has_HttpGet_AdminRules_Route()
    {
        var method = typeof(AdminRulesController).GetMethod("Index");
        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("admin/rules", httpGet!.Template);
    }

    [Fact]
    public async Task Index_Returns_Landing_With_Both_Cards_Populated()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag-r"));
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), "etag-l"));
        svc.ListVersionsAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RulesConfigVersionDto>());

        var result = await NewController(svc).Index(default);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RulesLandingViewModel>(view.Model);
        Assert.False(model.Rules.IsEmpty);
        Assert.Equal(1, model.Rules.ItemCount);
        Assert.False(model.Lookups.IsEmpty);
        Assert.Equal(1, model.Lookups.ItemCount);
    }

    [Fact]
    public async Task Index_Renders_Empty_Card_When_Rules_Blob_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), "etag-l"));
        svc.ListVersionsAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RulesConfigVersionDto>());

        var result = await NewController(svc).Index(default);

        var model = Assert.IsType<RulesLandingViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Rules.IsEmpty);
        Assert.False(model.Lookups.IsEmpty); // lookups still rendered
    }
}
```

NOTE: `RulesConfigNotFoundException` takes a single `string message` (confirmed). The `catch (RulesConfigNotFoundException)` in the controller catches it by type, so no message is needed there.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: FAIL — `AdminRulesController` does not exist (compile error).

- [ ] **Step 3: Create the controller (landing only)**

Create `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`:

```csharp
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
```

- [ ] **Step 4: Create the landing view**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Index.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model RulesLandingViewModel
@{
    ViewData["Title"] = "Rules configuration";
}

<h1 class="govuk-heading-xl">Rules configuration</h1>
<p class="govuk-body-l">
    View the decision rules and country-language lookups used by the rules engine.
</p>

<div class="govuk-inset-text">
    Changes saved here are picked up by the rules engine worker within about 5 minutes.
    This screen is read-only — editing is not yet available.
</div>

<div class="govuk-grid-row">
    <div class="govuk-grid-column-one-half">
        @await Html.PartialAsync("_ConfigCard", BuildCard(
            "Decision rules", "rules.json", Model.Rules, "/admin/rules/outcomes",
            "View outcomes", "Rules"))
    </div>
    <div class="govuk-grid-column-one-half">
        @await Html.PartialAsync("_ConfigCard", BuildCard(
            "Country languages", "country-languages.json", Model.Lookups, "/admin/rules/lookups",
            "View languages", "Lookups"))
    </div>
</div>

@functions {
    // Inline card builder keeps the partial dumb; no extra view-model type needed.
    private static ConfigCardModel BuildCard(string title, string file, RulesConfigCardViewModel card,
        string detailUrl, string detailLabel, string historyType) =>
        new(title, file, card, detailUrl, detailLabel, historyType);

    public sealed record ConfigCardModel(string Title, string File, RulesConfigCardViewModel Card,
        string DetailUrl, string DetailLabel, string HistoryType);
}
```

Create the small card partial `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_ConfigCard.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model DfE.CheckPerformanceData.Web.Views.Admin.Rules.Pages.IndexModel.ConfigCardModel
```

STOP — the `@functions` record approach above creates an awkward generated type name for the partial model. Use this simpler, robust structure instead: **delete the `_ConfigCard.cshtml` partial and the `@functions` block**, and render both cards inline in `Index.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model RulesLandingViewModel
@{
    ViewData["Title"] = "Rules configuration";

    void RenderCard(string title, string file, RulesConfigCardViewModel card,
        string detailUrl, string detailLabel, string historyType, string itemNoun)
    {
        <div class="govuk-summary-card">
            <div class="govuk-summary-card__title-wrapper">
                <h2 class="govuk-summary-card__title">@title</h2>
            </div>
            <div class="govuk-summary-card__content">
                <p class="govuk-body-s govuk-hint"><code>@file</code></p>
                @if (card.IsEmpty)
                {
                    <p class="govuk-body">Not yet configured. No file has been published.</p>
                }
                else
                {
                    <dl class="govuk-summary-list govuk-summary-list--no-border">
                        @if (!string.IsNullOrEmpty(card.Version))
                        {
                            <div class="govuk-summary-list__row">
                                <dt class="govuk-summary-list__key">Version</dt>
                                <dd class="govuk-summary-list__value">@card.Version</dd>
                            </div>
                        }
                        <div class="govuk-summary-list__row">
                            <dt class="govuk-summary-list__key">@itemNoun</dt>
                            <dd class="govuk-summary-list__value">@card.ItemCount</dd>
                        </div>
                        @if (card.LatestVersionNumber is int v)
                        {
                            <div class="govuk-summary-list__row">
                                <dt class="govuk-summary-list__key">Last saved</dt>
                                <dd class="govuk-summary-list__value">
                                    Version @v@(card.LastSavedBy is null ? "" : $" by {card.LastSavedBy}")
                                    @(card.LastSavedAt is null ? "" : $" on {card.LastSavedAt:d MMM yyyy HH:mm} UTC")
                                </dd>
                            </div>
                        }
                    </dl>
                    <p class="govuk-body">
                        <a class="govuk-link" href="@detailUrl">@detailLabel</a>
                    </p>
                }
                <p class="govuk-body">
                    <a class="govuk-link" href="/admin/rules/history/@historyType">Version history</a>
                </p>
            </div>
        </div>
    }
}

<h1 class="govuk-heading-xl">Rules configuration</h1>
<p class="govuk-body-l">
    View the decision rules and country-language lookups used by the rules engine.
</p>

<div class="govuk-inset-text">
    Changes saved here are picked up by the rules engine worker within about 5 minutes.
    This screen is read-only — editing is not yet available.
</div>

<div class="govuk-grid-row">
    <div class="govuk-grid-column-one-half">
        @{ RenderCard("Decision rules", "rules.json", Model.Rules, "/admin/rules/outcomes", "View outcomes", "Rules", "Outcomes"); }
    </div>
    <div class="govuk-grid-column-one-half">
        @{ RenderCard("Country languages", "country-languages.json", Model.Lookups, "/admin/rules/lookups", "View languages", "Lookups", "Countries"); }
    </div>
</div>
```

(Do not create `_ConfigCard.cshtml`. The inline local function above is the final form.)

- [ ] **Step 5: Build and run the tests to verify they pass**

Run: `dotnet build` then `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: build succeeds; tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Index.cshtml tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs
git commit -m "feat(rules): add read-only rules config landing page"
```

---

## Task 5: Outcomes list + outcome detail (recursive predicate rendering)

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs` (add two actions)
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcomes.cshtml`
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcome.cshtml`
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_PredicateNode.cshtml`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs` (add cases)

Context: Outcome detail must return `404` for an unknown outcome key (factory returns null). The predicate partial renders a `PredicateNode`: a leaf as a list item; a composite as its header text followed by a nested `<ul>` of its children, recursing into itself.

- [ ] **Step 1: Add failing controller tests**

Append to `AdminRulesControllerTests`:

```csharp
    [Fact]
    public async Task Outcomes_Returns_List_Model()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag"));

        var result = await NewController(svc).Outcomes(default);

        var model = Assert.IsType<OutcomesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Single(model.Outcomes);
        Assert.Equal("Inclusion", model.Outcomes[0].Key);
    }

    [Fact]
    public async Task Outcomes_Empty_When_Rules_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));

        var result = await NewController(svc).Outcomes(default);

        var model = Assert.IsType<OutcomesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task Outcome_Returns_Detail_For_Known_Key()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag"));

        var result = await NewController(svc).Outcome("Inclusion", default);

        var model = Assert.IsType<OutcomeDetailViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Inclusion", model.Key);
        Assert.Single(model.Branches);
        Assert.Equal("Otherwise (always matches)", model.Branches[0].Condition.Text);
    }

    [Fact]
    public async Task Outcome_Returns_NotFound_For_Unknown_Key()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag"));

        var result = await NewController(svc).Outcome("Nope", default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Outcome_Returns_NotFound_When_Rules_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));

        var result = await NewController(svc).Outcome("Inclusion", default);

        Assert.IsType<NotFoundResult>(result);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: FAIL — `Outcomes`/`Outcome` actions do not exist (compile error).

- [ ] **Step 3: Add the two actions to the controller**

In `AdminRulesController`, add the view-path constants near `IndexView`:

```csharp
    private const string OutcomesView = "~/Views/Admin/Rules/Outcomes.cshtml";
    private const string OutcomeView = "~/Views/Admin/Rules/Outcome.cshtml";
```

Add the actions (above the helpers region):

```csharp
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
```

- [ ] **Step 4: Create the outcomes list view**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcomes.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model OutcomesViewModel
@{
    ViewData["Title"] = "Decision outcomes";
}

<a href="/admin/rules" class="govuk-back-link">Back to rules configuration</a>

<h1 class="govuk-heading-xl">Decision outcomes</h1>

@if (Model.IsEmpty)
{
    <p class="govuk-body">No rules have been published yet.</p>
}
else
{
    <table class="govuk-table">
        <caption class="govuk-table__caption govuk-visually-hidden">Decision outcomes</caption>
        <thead class="govuk-table__head">
            <tr class="govuk-table__row">
                <th scope="col" class="govuk-table__header">Outcome</th>
                <th scope="col" class="govuk-table__header">Key</th>
                <th scope="col" class="govuk-table__header govuk-table__header--numeric">Branches</th>
            </tr>
        </thead>
        <tbody class="govuk-table__body">
            @foreach (var o in Model.Outcomes)
            {
                <tr class="govuk-table__row">
                    <td class="govuk-table__cell">
                        <a class="govuk-link" href="/admin/rules/outcomes/@Uri.EscapeDataString(o.Key)">@o.Label</a>
                    </td>
                    <td class="govuk-table__cell"><code>@o.Key</code></td>
                    <td class="govuk-table__cell govuk-table__cell--numeric">@o.BranchCount</td>
                </tr>
            }
        </tbody>
    </table>
}
```

- [ ] **Step 5: Create the recursive predicate partial**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_PredicateNode.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model PredicateNode

@if (Model.IsLeaf)
{
    <li class="govuk-body govuk-!-margin-bottom-1">@Model.Text</li>
}
else
{
    <li class="govuk-body govuk-!-margin-bottom-1">
        @Model.Text
        <ul class="govuk-list govuk-list--bullet govuk-!-margin-bottom-0">
            @foreach (var child in Model.Children)
            {
                @await Html.PartialAsync("_PredicateNode", child)
            }
        </ul>
    </li>
}
```

- [ ] **Step 6: Create the outcome detail view**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcome.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Application.RulesEngine
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model OutcomeDetailViewModel
@{
    ViewData["Title"] = Model.Label;

    string TagClass(DecisionStatus s) => s switch
    {
        DecisionStatus.AutoApproved => "govuk-tag--green",
        DecisionStatus.AutoRejected => "govuk-tag--red",
        _ => "govuk-tag--yellow"
    };

    string TagText(DecisionStatus s) => s switch
    {
        DecisionStatus.AutoApproved => "Auto approved",
        DecisionStatus.AutoRejected => "Auto rejected",
        _ => "Scrutiny"
    };
}

<a href="/admin/rules/outcomes" class="govuk-back-link">Back to outcomes</a>

<span class="govuk-caption-xl">Decision outcome</span>
<h1 class="govuk-heading-xl">@Model.Label</h1>
<p class="govuk-body-s govuk-hint">Key: <code>@Model.Key</code></p>

<p class="govuk-body">
    Branches are evaluated top to bottom. The first branch whose condition is true decides the outcome.
</p>

@foreach (var branch in Model.Branches)
{
    <div class="govuk-summary-card">
        <div class="govuk-summary-card__title-wrapper">
            <h2 class="govuk-summary-card__title">@branch.Id</h2>
            <ul class="govuk-summary-card__actions">
                <li class="govuk-summary-card__action">
                    <strong class="govuk-tag @TagClass(branch.Status)">@TagText(branch.Status)</strong>
                </li>
            </ul>
        </div>
        <div class="govuk-summary-card__content">
            <h3 class="govuk-heading-s govuk-!-margin-bottom-1">When</h3>
            <ul class="govuk-list">
                @await Html.PartialAsync("_PredicateNode", branch.Condition)
            </ul>
        </div>
    </div>
}
```

- [ ] **Step 7: Build and run the tests to verify they pass**

Run: `dotnet build` then `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: build succeeds; tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcomes.cshtml src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcome.cshtml src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_PredicateNode.cshtml tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs
git commit -m "feat(rules): add outcomes list and outcome detail with predicate rendering"
```

---

## Task 6: Lookups view

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs` (add action)
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Lookups.cshtml`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs` (add cases)

- [ ] **Step 1: Add failing controller tests**

Append to `AdminRulesControllerTests`:

```csharp
    [Fact]
    public async Task Lookups_Returns_Rows()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), "etag"));

        var result = await NewController(svc).Lookups(default);

        var model = Assert.IsType<LookupsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Single(model.Rows);
        Assert.Equal("GB", model.Rows[0].CountryCode);
    }

    [Fact]
    public async Task Lookups_Empty_When_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>())
            .Returns<(Lookups, string?)>(_ => throw new RulesConfigNotFoundException("country-languages.json not found"));

        var result = await NewController(svc).Lookups(default);

        var model = Assert.IsType<LookupsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsEmpty);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: FAIL — `Lookups` action does not exist.

- [ ] **Step 3: Add the action**

In `AdminRulesController`, add the view-path constant:

```csharp
    private const string LookupsView = "~/Views/Admin/Rules/Lookups.cshtml";
```

Add the action:

```csharp
    [HttpGet("admin/rules/lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        var (lookups, _) = await TryGetLookupsAsync(ct);
        return View(LookupsView, RulesAdminViewModelFactory.Lookups(lookups));
    }
```

- [ ] **Step 4: Create the lookups view**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Lookups.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model LookupsViewModel
@{
    ViewData["Title"] = "Country languages";
}

<a href="/admin/rules" class="govuk-back-link">Back to rules configuration</a>

<h1 class="govuk-heading-xl">Country languages</h1>
<p class="govuk-body">
    Maps a country code to its official languages. Used by the "official language" rule condition.
</p>

@if (Model.IsEmpty)
{
    <p class="govuk-body">No country-language lookups have been published yet.</p>
}
else
{
    <table class="govuk-table">
        <caption class="govuk-table__caption govuk-visually-hidden">Country languages</caption>
        <thead class="govuk-table__head">
            <tr class="govuk-table__row">
                <th scope="col" class="govuk-table__header">Country code</th>
                <th scope="col" class="govuk-table__header">Official languages</th>
            </tr>
        </thead>
        <tbody class="govuk-table__body">
            @foreach (var row in Model.Rows)
            {
                <tr class="govuk-table__row">
                    <td class="govuk-table__cell"><code>@row.CountryCode</code></td>
                    <td class="govuk-table__cell">@row.Languages</td>
                </tr>
            }
        </tbody>
    </table>
}
```

- [ ] **Step 5: Build and run the tests to verify they pass**

Run: `dotnet build` then `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: build succeeds; tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Lookups.cshtml tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs
git commit -m "feat(rules): add read-only country-languages lookups view"
```

---

## Task 7: Version history list + version detail

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs` (add two actions)
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/History.cshtml`
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Version.cshtml`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs` (add cases)

Context: `{type}` arrives as a route string ("Rules"/"Lookups"); parse with `Enum.TryParse<RulesConfigType>(type, ignoreCase: true, out _)` and `NotFound()` on failure. Version detail looks the requested version id up within `ListVersionsAsync(type)`; `NotFound()` if absent.

- [ ] **Step 1: Add failing controller tests**

Append to `AdminRulesControllerTests`:

```csharp
    private static RulesConfigVersionDto Version(int id, int num, string content = "{}") => new()
    {
        Id = id, ConfigType = RulesConfigType.Rules, VersionNumber = num,
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "Ada", Content = content
    };

    [Fact]
    public async Task History_Returns_Versions_For_Valid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.ListVersionsAsync(RulesConfigType.Rules, Arg.Any<CancellationToken>())
            .Returns(new[] { Version(1, 1), Version(2, 2) });

        var result = await NewController(svc).History("Rules", default);

        var model = Assert.IsType<HistoryViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(RulesConfigType.Rules, model.ConfigType);
        Assert.Equal(2, model.Versions.Count);
        Assert.Equal(2, model.Versions[0].VersionNumber); // newest first
    }

    [Fact]
    public async Task History_NotFound_For_Invalid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        var result = await NewController(svc).History("Bananas", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Version_Returns_Detail_For_Known_Id()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.ListVersionsAsync(RulesConfigType.Rules, Arg.Any<CancellationToken>())
            .Returns(new[] { Version(7, 3, "{\"x\":1}") });

        var result = await NewController(svc).Version("Rules", 7, default);

        var model = Assert.IsType<VersionDetailViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(3, model.VersionNumber);
        Assert.Equal("{\"x\":1}", model.Content);
    }

    [Fact]
    public async Task Version_NotFound_For_Unknown_Id()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.ListVersionsAsync(RulesConfigType.Rules, Arg.Any<CancellationToken>())
            .Returns(new[] { Version(7, 3) });

        var result = await NewController(svc).Version("Rules", 999, default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Version_NotFound_For_Invalid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        var result = await NewController(svc).Version("Bananas", 1, default);
        Assert.IsType<NotFoundResult>(result);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: FAIL — `History`/`Version` actions do not exist.

- [ ] **Step 3: Add the two actions to the controller**

Add the view-path constants:

```csharp
    private const string HistoryView = "~/Views/Admin/Rules/History.cshtml";
    private const string VersionView = "~/Views/Admin/Rules/Version.cshtml";
```

Add the actions:

```csharp
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
```

- [ ] **Step 4: Create the history list view**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/History.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model HistoryViewModel
@{
    var typeName = Model.ConfigType.ToString();
    var heading = Model.ConfigType == DfE.CheckPerformanceData.Application.RulesConfig.RulesConfigType.Rules
        ? "Decision rules"
        : "Country languages";
    ViewData["Title"] = $"{heading} — version history";
}

<a href="/admin/rules" class="govuk-back-link">Back to rules configuration</a>

<span class="govuk-caption-xl">Version history</span>
<h1 class="govuk-heading-xl">@heading</h1>

@if (Model.Versions.Count == 0)
{
    <p class="govuk-body">No versions have been saved yet.</p>
}
else
{
    <table class="govuk-table">
        <caption class="govuk-table__caption govuk-visually-hidden">Version history</caption>
        <thead class="govuk-table__head">
            <tr class="govuk-table__row">
                <th scope="col" class="govuk-table__header">Version</th>
                <th scope="col" class="govuk-table__header">Saved</th>
                <th scope="col" class="govuk-table__header">By</th>
                <th scope="col" class="govuk-table__header"><span class="govuk-visually-hidden">Actions</span></th>
            </tr>
        </thead>
        <tbody class="govuk-table__body">
            @foreach (var v in Model.Versions)
            {
                <tr class="govuk-table__row">
                    <td class="govuk-table__cell">@v.VersionNumber</td>
                    <td class="govuk-table__cell">@v.CreatedAt.ToString("d MMM yyyy HH:mm") UTC</td>
                    <td class="govuk-table__cell">@(v.CreatedBy ?? "Unknown")</td>
                    <td class="govuk-table__cell">
                        <a class="govuk-link" href="/admin/rules/history/@typeName/@v.Id">View JSON</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}
```

- [ ] **Step 5: Create the version detail view**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Version.cshtml`:

```cshtml
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model VersionDetailViewModel
@{
    var typeName = Model.ConfigType.ToString();
    ViewData["Title"] = $"Version {Model.VersionNumber}";
}

<a href="/admin/rules/history/@typeName" class="govuk-back-link">Back to version history</a>

<span class="govuk-caption-xl">@typeName configuration</span>
<h1 class="govuk-heading-xl">Version @Model.VersionNumber</h1>

<dl class="govuk-summary-list">
    <div class="govuk-summary-list__row">
        <dt class="govuk-summary-list__key">Saved</dt>
        <dd class="govuk-summary-list__value">@Model.CreatedAt.ToString("d MMM yyyy HH:mm") UTC</dd>
    </div>
    <div class="govuk-summary-list__row">
        <dt class="govuk-summary-list__key">By</dt>
        <dd class="govuk-summary-list__value">@(Model.CreatedBy ?? "Unknown")</dd>
    </div>
</dl>

<h2 class="govuk-heading-m">Stored JSON</h2>
<pre class="govuk-!-padding-3" style="overflow:auto; background:#f3f2f1;"><code>@Model.Content</code></pre>
```

- [ ] **Step 6: Build and run the tests to verify they pass**

Run: `dotnet build` then `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj --filter "FullyQualifiedName~AdminRulesControllerTests"`
Expected: build succeeds; tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/History.cshtml src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Version.cshtml tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerTests.cs
git commit -m "feat(rules): add config version history and version detail views"
```

---

## Task 8: E2E auth + content tests, and full verification

**Files:**
- Create: `tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminRulesAuthTests.cs`
- Modify: `tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminAuthTests.cs` (assert the new tile)

Context: Mirror `AdminAuthTests` exactly — same `[Collection("E2E")]`, `[Trait("Category", "W4")]`, `PlaywrightFixture` ctor, `AuthHelpers` impersonation, `TestHttpClients.SendAsync`, and the `try/finally` that restores editor impersonation. The local stack must be running (`docker compose --profile all`) and Azurite seeded (the `azurite_init` one-shot seeds `rules.json` + `country-languages.json`), so `/admin/rules` should render populated cards.

- [ ] **Step 1: Add the landing-tile assertion to the existing admin landing test**

In `AdminAuthTests.Admin_AsAdmin_Returns_200`, after the existing `Assert.Contains("Rules engine", body);` line, add:

```csharp
            Assert.Contains("Rules configuration", body);
```

- [ ] **Step 2: Create the `/admin/rules` auth + content E2E test**

Create `tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminRulesAuthTests.cs`:

```csharp
using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminRulesAuthTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task Rules_AsAnon_Redirects_To_Signin()
    {
        try
        {
            await AuthHelpers.ClearImpersonationAsync(_fixture);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            Assert.False(string.IsNullOrEmpty(location));
            Assert.DoesNotContain("AccessDenied", location);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    [Fact]
    public async Task Rules_AsNonAdmin_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    [Fact]
    public async Task Rules_AsEditorOnly_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    [Fact]
    public async Task Rules_AsAdmin_Returns_200_With_Both_Cards()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Rules configuration", body);
            Assert.Contains("Decision rules", body);
            Assert.Contains("Country languages", body);
            Assert.Contains("View outcomes", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
```

NOTE: confirm the exact helper names in `tests/DfE.CheckPerformanceData.E2ETests/Helpers/AuthHelpers.cs` (`ImpersonateAsUnprivilegedUserAsync`, `ImpersonateAsAdminAsync`, `ImpersonateAsEditorAsync`, `ClearImpersonationAsync`) and `TestHttpClients.SendAsync` — they are used verbatim by `AdminAuthTests`, so copy whatever that file uses.

- [ ] **Step 3: Run the full unit-test suite**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.UnitTests.csproj`
Expected: PASS — all prior tests plus the new ones (≈1340 + the M2 additions).

- [ ] **Step 4: Run the E2E suite (stack must be up)**

Ensure `docker compose --profile all` is running, then:
Run: `dotnet test tests/DfE.CheckPerformanceData.E2ETests/DfE.CheckPerformanceData.E2ETests.csproj --filter "FullyQualifiedName~AdminRulesAuthTests|FullyQualifiedName~AdminAuthTests"`
Expected: PASS — anon redirects, non-admin/editor get AccessDenied, admin gets 200 with both cards.

- [ ] **Step 5: Manual smoke (optional but recommended)**

Sign in via dev impersonation as **Admin**, visit `/admin/rules`. Verify: both cards populated, "View outcomes" lists outcomes, an outcome page renders nested predicate text, lookups table renders, version history lists at least the seeded version, "View JSON" shows raw content. Confirm a bogus outcome key (`/admin/rules/outcomes/Nope`) and a bogus type (`/admin/rules/history/Bananas`) both return 404.

- [ ] **Step 6: Commit**

```bash
git add tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminRulesAuthTests.cs tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminAuthTests.cs
git commit -m "test(rules): add E2E auth and content coverage for /admin/rules"
```

---

## Milestone roadmap (context)

- **M1 — foundation (no UI):** DONE. Version entity + migration, blob store + ETag concurrency, repo, validators, `RulesConfigService`, DI.
- **M2 — read-only admin surface:** THIS PLAN. Nav entry, GET-only controller, GOV.UK views, predicate rendering, version history, empty states.
- **M3 — editing:** recursive predicate widget + select-then-group regrouping + lookups editor (writes via `SaveRulesAsync`/`SaveLookupsAsync`, ETag concurrency, GOV.UK error summary from validator).
- **M4 — add/remove outcomes + deletion hard-block guard + rollback UI:** add/remove outcomes with safe `otherwise→Scrutiny` seed; hard-block deletion of outcomes bound in `AnswerFieldMap.WhatToChangeToOutcomeKey`; connected-`ChangeRequest` display + typed confirm; rollback via `RollbackAsync`.

## Self-Review (completed by plan author)

- **Spec coverage:** Nav entry (T1), read-only controller + landing (T4), outcomes list + per-outcome branch list with predicate rendering (T5), lookups (T6), version history + detail (T7), `RulesConfigNotFoundException` empty-state handling (T4/T5/T6), admin-only gating + E2E auth matrix (T8), ~5-min worker-latency UI copy (T4 landing inset text). All M2 carry-forward items from the M1 review are addressed or surfaced.
- **Placeholder scan:** No TBD/"add error handling"/"similar to" placeholders; every code step shows complete code. Two NOTEs flag real signatures to confirm against existing files (`RulesConfigNotFoundException` ctor; `AuthHelpers`/`TestHttpClients` member names) rather than guessing — the implementer must verify and adjust if they differ.
- **Type consistency:** `RulesConfigCardViewModel` (`IsEmpty`/`ItemCount`/`LatestVersionNumber`/`LastSavedBy`/`LastSavedAt`/`Version`), `OutcomesViewModel.Outcomes`/`.IsEmpty`, `OutcomeDetailViewModel.Branches`, `BranchViewModel(Id,Status,Condition)`, `LookupsViewModel.Rows`, `LookupRowViewModel(CountryCode,Languages)`, `HistoryViewModel.ConfigType`/`.Versions`, `VersionRowViewModel(Id,VersionNumber,CreatedAt,CreatedBy)`, `VersionDetailViewModel.Content`, `PredicateNode(Text,Children,IsLeaf,Leaf())` — names match across factory, controller, tests, and views. Factory method names (`RulesCard`/`LookupsCard`/`Outcomes`/`Outcome`/`Lookups`/`History`/`VersionDetail`) match every call site. Controller action names (`Index`/`Outcomes`/`Outcome`/`Lookups`/`History`/`Version`) match the test call sites and route templates.
```
