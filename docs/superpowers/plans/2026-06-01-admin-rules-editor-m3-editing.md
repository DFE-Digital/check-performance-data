# Admin Rules Editor — Milestone 3 (editing) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `/admin/rules` editable — an admin can change a decision branch's status and `When` predicate through a structured no-JS GOV.UK form, manage the branch list (add/remove/reorder), and edit the country-languages lookups, all saved via the existing M1 `IRulesConfigService`.

**Architecture:** Pure Web layer on top of M1. No Domain/Application/Persistence/Infrastructure changes. The recursive predicate tree round-trips through a **flat indexed list** (`PredicateNodeForm`) the default model binder handles; pure `Flatten`/`RebuildPredicate` fold between the list and the `Predicate` object graph; structural edits are pure list transforms; every mutation is a full-page POST (zero JavaScript). Saves re-read + splice into the current `RuleSet` (no-clobber concurrency) then call `SaveRulesAsync`/`SaveLookupsAsync`.

**Tech Stack:** ASP.NET Core MVC (.NET 10), Razor + GOV.UK Design System, xUnit + NSubstitute (unit), Playwright (E2E). PostgreSQL/Azurite via M1 (untouched here).

---

## Two deliberate refinements vs the approved spec (read first)

The spec's `PredicateNodeForm` is the design intent; two small, behaviour-preserving encoding choices make it bind cleanly with no JS. Both are documented here so they are not mistaken for drift:

1. **Added `Operator` token field.** A leaf's UI operator dropdown (`equals`, `less than`, `is one of`, `is known & certain`, `official language is`, …) crosses `PredicateKind`/`CompareOp` boundaries, so it cannot bind to `Kind` alone. The form binds a single string `Operator` token; a pure `Normalize` step recomputes `Kind` + `Op` from `Operator` + the field's catalogue type on every postback. `Kind`/`Op` stay authoritative for `RebuildPredicate` (faithful to the spec).
2. **`OfficialLanguageIs` reuses `Field` + `Value`** instead of separate `CountryField`/`Language` props (the country field IS the leaf's selected field; the language IS its value editor). Fewer bound fields, identical behaviour.

`PredicateKind` keeps all ten spec values. Everything else follows the spec exactly.

---

## File structure

**New (`src/DfE.CheckPerformanceData.Web/Admin/Rules/`):**
- `PredicateKind.cs` — enum (10 values).
- `PredicateNodeForm.cs` — flat bindable node.
- `PredicateForm.cs` — pure `Flatten` + `RebuildPredicate` + `OperatorToken` reverse map.
- `LeafNormalizer.cs` — pure `Normalize(node, fieldType)` token → `Kind`/`Op`.
- `BranchEditTransforms.cs` — pure list transforms (add/remove/group/ungroup/setCombinator/setField/addValue/removeValue) + `NextId`.
- `PredicateFormValidator.cs` — pure pre-save structural checks (resolves the empty-composite open item).
- `LeafEditorOptions.cs` — pure dropdown data (fields, operators per type, statuses, value-editor kind).
- `RuleSetSplicer.cs` — pure `ReplaceBranch`/`InsertBranch`/`RemoveBranch`/`MoveBranch`.
- `BranchEditForm.cs` — posted form model.
- `BranchEditViewModel.cs` — render model (form + dropdown data + errors + flags).
- `LookupsEditViewModels.cs` — `LookupRowEditForm` + `LookupRowEditViewModel`.

**Modified:**
- `Web/Controllers/AdminRulesController.cs` — new GET (`branches/{id}/edit`, `branches/add`, `branches/{id}/remove`, `lookups/{code}/edit`, `lookups/add`) + POST (`branch/transform`, `branch/save`, `branches/{id}/remove`, `branches/{id}/move`, `lookups/{code}/save`, `lookups/{code}/remove`, `lookups/row/transform`) actions.

**New views (`Web/Views/Admin/Rules/`):**
- `_PredicateEditorNode.cshtml` — recursive editor partial.
- `BranchEdit.cshtml` — branch editor.
- `RemoveBranch.cshtml` — delete confirmation interstitial.
- `LookupRowEdit.cshtml` — lookups row editor.

**Modified views:**
- `Outcome.cshtml` — edit/add/remove/move affordances + success banner.
- `Lookups.cshtml` — edit/remove/add affordances.

**New tests (`tests/DfE.CheckPerformanceData.UnitTests/Web/...`):**
- `Admin/Rules/PredicateFormTests.cs`, `BranchEditTransformsTests.cs`, `LeafNormalizerTests.cs`, `PredicateFormValidatorTests.cs`, `RuleSetSplicerTests.cs`, `LeafEditorOptionsTests.cs`.
- `Controllers/AdminRulesControllerEditTests.cs`.

**New E2E (`tests/DfE.CheckPerformanceData.E2ETests/Admin/`):**
- `AdminRulesEditTests.cs`.

> **Conventions (verified against M2 code):** Test project root namespace is `DfE.CheckPerformanceData.Application.UnitTests`; `Xunit` is a global using (no `using Xunit;`); test classes are `public sealed class`; use `[Fact]`, `Assert.Equal/True/False/Single/IsType/Contains`. NSubstitute is available (`using NSubstitute;`). The UnitTests csproj is `tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.Application.UnitTests.csproj`. The PowerShell tool's working dir is `C:\Repos\DfE` — use full paths in `dotnet` commands. Run unit tests with `dotnet test C:\Repos\DfE\check-performance-data\tests\DfE.CheckPerformanceData.UnitTests\DfE.CheckPerformanceData.Application.UnitTests.csproj`.

---

### Task 1: Flat node types + Flatten/RebuildPredicate round-trip

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateKind.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateNodeForm.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateForm.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateFormTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

```csharp
using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateFormTests
{
    private static string Json(Predicate p) => JsonSerializer.Serialize(p, RulesJson.Options);

    // A representative 3-level tree exercising every leaf kind.
    private static Predicate Sample() => new Predicate.AllOf(new Predicate[]
    {
        new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")),
        new Predicate.AnyOf(new Predicate[]
        {
            new Predicate.FieldCompare("pupilAge", CompareOp.Gte, new FieldValue.Num(16)),
            new Predicate.Not(new Predicate.FieldEq("isAddBack", new FieldValue.Bool(true))),
        }),
        new Predicate.FieldIn("inclusionFlag", new FieldValue[] { new FieldValue.Str("A"), new FieldValue.Str("B") }),
        new Predicate.FieldCompare("schoolAdmissionDate", CompareOp.Lt, new FieldValue.Date(new DateOnly(2025, 9, 1))),
        new Predicate.IsKnownAndCertain("firstLanguage"),
        new Predicate.OfficialLanguageIs("countryOfOrigin", "English"),
    });

    [Fact]
    public void Flatten_Then_Rebuild_Round_Trips()
    {
        var original = Sample();

        var flat = PredicateForm.Flatten(original);
        var rebuilt = PredicateForm.RebuildPredicate(flat);

        Assert.Equal(Json(original), Json(rebuilt));
    }

    [Fact]
    public void Flatten_Produces_DepthFirst_Parent_Before_Children()
    {
        var flat = PredicateForm.Flatten(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))
        }));

        Assert.Equal(2, flat.Count);
        Assert.Null(flat[0].ParentId);                 // root first
        Assert.Equal(PredicateKind.AllOf, flat[0].Kind);
        Assert.Equal(flat[0].Id, flat[1].ParentId);    // child references root
    }

    [Fact]
    public void Otherwise_Round_Trips()
    {
        var flat = PredicateForm.Flatten(Predicate.Otherwise.Instance);
        Assert.Equal(Json(Predicate.Otherwise.Instance), Json(PredicateForm.RebuildPredicate(flat)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\Repos\DfE\check-performance-data\tests\DfE.CheckPerformanceData.UnitTests\DfE.CheckPerformanceData.Application.UnitTests.csproj --filter FullyQualifiedName~PredicateFormTests`
Expected: FAIL — `PredicateKind`/`PredicateNodeForm`/`PredicateForm` do not exist (compile error).

- [ ] **Step 3: Create the enum**

`PredicateKind.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>Discriminator for a <see cref="PredicateNodeForm"/> in the flat editor list.</summary>
public enum PredicateKind
{
    AllOf, AnyOf, Not,
    FieldEq, FieldNeq, FieldIn, FieldCompare,
    IsKnownAndCertain, OfficialLanguageIs, Otherwise
}
```

- [ ] **Step 4: Create the bindable node**

`PredicateNodeForm.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// One node of the predicate tree, flattened so the default model binder round-trips it.
/// Order among siblings = order in the list. See <see cref="PredicateForm"/>.
/// </summary>
public sealed class PredicateNodeForm
{
    public int Id { get; set; }            // stable form-local id, unique within the form
    public int? ParentId { get; set; }     // null for the root node
    public PredicateKind Kind { get; set; }

    public string? Field { get; set; }     // leaf field (also the country field for OfficialLanguageIs)
    public string? Operator { get; set; }  // UI operator token: eq|neq|in|lt|lte|gt|gte|known|lang
    public string? Op { get; set; }        // CompareOp name, set by Normalize for FieldCompare
    public string? Value { get; set; }     // scalar literal (also the language for OfficialLanguageIs)
    public List<string> Values { get; set; } = new(); // for FieldIn
    public bool Selected { get; set; }     // transient: ticked for select-then-group
}
```

- [ ] **Step 5: Create Flatten + RebuildPredicate + the operator-token map**

`PredicateForm.cs`:

```csharp
using System.Globalization;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure folds between the <see cref="Predicate"/> object graph and the flat
/// <see cref="PredicateNodeForm"/> list the form binds. No I/O, no state.
/// </summary>
public static class PredicateForm
{
    public static List<PredicateNodeForm> Flatten(Predicate predicate)
    {
        var list = new List<PredicateNodeForm>();
        var counter = 0;

        void Walk(Predicate p, int? parentId)
        {
            var id = ++counter;
            list.Add(ToNode(p, id, parentId));
            switch (p)
            {
                case Predicate.AllOf a: foreach (var c in a.Items) Walk(c, id); break;
                case Predicate.AnyOf a: foreach (var c in a.Items) Walk(c, id); break;
                case Predicate.Not n:   Walk(n.Inner, id); break;
            }
        }

        Walk(predicate, null);
        return list;
    }

    public static Predicate RebuildPredicate(IReadOnlyList<PredicateNodeForm> nodes)
    {
        var childrenByParent = nodes
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var root = nodes.First(n => n.ParentId is null);
        return Build(root, childrenByParent);
    }

    /// <summary>UI operator token for an existing predicate node (reverse of Normalize).</summary>
    public static string OperatorToken(PredicateKind kind, string? op) => kind switch
    {
        PredicateKind.FieldEq => "eq",
        PredicateKind.FieldNeq => "neq",
        PredicateKind.FieldIn => "in",
        PredicateKind.IsKnownAndCertain => "known",
        PredicateKind.OfficialLanguageIs => "lang",
        PredicateKind.FieldCompare => op?.ToLowerInvariant() ?? "lt",
        _ => string.Empty
    };

    private static Predicate Build(PredicateNodeForm node, Dictionary<int, List<PredicateNodeForm>> kids)
    {
        List<PredicateNodeForm> Children() =>
            kids.TryGetValue(node.Id, out var cs) ? cs : new List<PredicateNodeForm>();

        return node.Kind switch
        {
            PredicateKind.AllOf => new Predicate.AllOf(Children().Select(c => Build(c, kids)).ToList()),
            PredicateKind.AnyOf => new Predicate.AnyOf(Children().Select(c => Build(c, kids)).ToList()),
            PredicateKind.Not => new Predicate.Not(
                Children().Select(c => Build(c, kids)).FirstOrDefault() ?? Predicate.Otherwise.Instance),
            PredicateKind.FieldEq => new Predicate.FieldEq(node.Field ?? "", ParseValue(node.Field, node.Value)),
            PredicateKind.FieldNeq => new Predicate.FieldNeq(node.Field ?? "", ParseValue(node.Field, node.Value)),
            PredicateKind.FieldIn => new Predicate.FieldIn(node.Field ?? "",
                node.Values.Select(v => ParseValue(node.Field, v)).ToList()),
            PredicateKind.FieldCompare => new Predicate.FieldCompare(node.Field ?? "",
                Enum.TryParse<CompareOp>(node.Op, out var cmp) ? cmp : CompareOp.Lt,
                ParseValue(node.Field, node.Value)),
            PredicateKind.IsKnownAndCertain => new Predicate.IsKnownAndCertain(node.Field ?? ""),
            PredicateKind.OfficialLanguageIs => new Predicate.OfficialLanguageIs(node.Field ?? "", node.Value ?? ""),
            _ => Predicate.Otherwise.Instance
        };
    }

    private static PredicateNodeForm ToNode(Predicate p, int id, int? parentId)
    {
        var node = new PredicateNodeForm { Id = id, ParentId = parentId };
        switch (p)
        {
            case Predicate.AllOf: node.Kind = PredicateKind.AllOf; break;
            case Predicate.AnyOf: node.Kind = PredicateKind.AnyOf; break;
            case Predicate.Not: node.Kind = PredicateKind.Not; break;
            case Predicate.FieldEq eq:
                node.Kind = PredicateKind.FieldEq; node.Field = eq.Field; node.Value = Scalar(eq.Value); break;
            case Predicate.FieldNeq neq:
                node.Kind = PredicateKind.FieldNeq; node.Field = neq.Field; node.Value = Scalar(neq.Value); break;
            case Predicate.FieldIn fin:
                node.Kind = PredicateKind.FieldIn; node.Field = fin.Field;
                node.Values = fin.Values.Select(Scalar).ToList(); break;
            case Predicate.FieldCompare cmp:
                node.Kind = PredicateKind.FieldCompare; node.Field = cmp.Field;
                node.Op = cmp.Op.ToString(); node.Value = Scalar(cmp.Value); break;
            case Predicate.IsKnownAndCertain k:
                node.Kind = PredicateKind.IsKnownAndCertain; node.Field = k.Field; break;
            case Predicate.OfficialLanguageIs l:
                node.Kind = PredicateKind.OfficialLanguageIs; node.Field = l.CountryField; node.Value = l.Language; break;
            case Predicate.Otherwise:
                node.Kind = PredicateKind.Otherwise; break;
        }
        node.Operator = OperatorToken(node.Kind, node.Op);
        return node;
    }

    private static string Scalar(FieldValue v) => v switch
    {
        FieldValue.Str s => s.Value,
        FieldValue.Bool b => b.Value ? "true" : "false",
        FieldValue.Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        FieldValue.Date d => d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldValue.Uncertain u => Scalar(u.Inner),
        _ => string.Empty
    };

    // Lenient: parse to the field's catalogue type; fall back to Str so RuleSetValidator
    // reports a friendly error on save rather than throwing mid-edit.
    private static FieldValue ParseValue(string? field, string? raw)
    {
        raw ??= string.Empty;
        if (field is not null && FieldCatalogue.TryGetType(field, out var type))
        {
            switch (type)
            {
                case FieldType.Bool when bool.TryParse(raw, out var b): return new FieldValue.Bool(b);
                case FieldType.Number when decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d):
                    return new FieldValue.Num(d);
                case FieldType.Date when DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt):
                    return new FieldValue.Date(dt);
                case FieldType.String: return new FieldValue.Str(raw);
            }
        }
        return new FieldValue.Str(raw);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ...UnitTests... --filter FullyQualifiedName~PredicateFormTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateKind.cs \
        src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateNodeForm.cs \
        src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateForm.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateFormTests.cs
git commit -m "feat(admin-rules): flat predicate form model with Flatten/Rebuild round-trip"
```

---

### Task 2: LeafNormalizer (operator token → Kind/Op + value reset)

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/LeafNormalizer.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/LeafNormalizerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class LeafNormalizerTests
{
    [Fact]
    public void Compare_Token_Sets_Kind_And_Op()
    {
        var node = new PredicateNodeForm { Kind = PredicateKind.FieldEq, Field = "pupilAge", Operator = "gte" };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.FieldCompare, node.Kind);
        Assert.Equal("Gte", node.Op);
    }

    [Fact]
    public void Eq_Token_Sets_FieldEq_And_Clears_Op()
    {
        var node = new PredicateNodeForm { Kind = PredicateKind.FieldCompare, Op = "Lt", Operator = "eq" };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.FieldEq, node.Kind);
        Assert.Null(node.Op);
    }

    [Fact]
    public void Known_Token_Sets_IsKnownAndCertain()
    {
        var node = new PredicateNodeForm { Operator = "known" };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.IsKnownAndCertain, node.Kind);
    }

    [Fact]
    public void Composite_Nodes_Are_Untouched()
    {
        var node = new PredicateNodeForm { Kind = PredicateKind.AnyOf };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.AnyOf, node.Kind);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ...UnitTests... --filter FullyQualifiedName~LeafNormalizerTests`
Expected: FAIL — `LeafNormalizer` does not exist.

- [ ] **Step 3: Implement**

`LeafNormalizer.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Maps a leaf node's UI <see cref="PredicateNodeForm.Operator"/> token onto its
/// authoritative <see cref="PredicateNodeForm.Kind"/> and <see cref="PredicateNodeForm.Op"/>.
/// Applied to every leaf on each postback before transforms / save / re-render.
/// Composite and Otherwise nodes are left untouched.
/// </summary>
public static class LeafNormalizer
{
    public static void Normalize(PredicateNodeForm node)
    {
        if (node.Kind is PredicateKind.AllOf or PredicateKind.AnyOf or PredicateKind.Not or PredicateKind.Otherwise
            && string.IsNullOrEmpty(node.Operator))
        {
            return; // structural node, no operator
        }

        switch (node.Operator)
        {
            case "eq": node.Kind = PredicateKind.FieldEq; node.Op = null; break;
            case "neq": node.Kind = PredicateKind.FieldNeq; node.Op = null; break;
            case "in": node.Kind = PredicateKind.FieldIn; node.Op = null; break;
            case "known": node.Kind = PredicateKind.IsKnownAndCertain; node.Op = null; break;
            case "lang": node.Kind = PredicateKind.OfficialLanguageIs; node.Op = null; break;
            case "lt": node.Kind = PredicateKind.FieldCompare; node.Op = "Lt"; break;
            case "lte": node.Kind = PredicateKind.FieldCompare; node.Op = "Lte"; break;
            case "gt": node.Kind = PredicateKind.FieldCompare; node.Op = "Gt"; break;
            case "gte": node.Kind = PredicateKind.FieldCompare; node.Op = "Gte"; break;
        }
    }

    public static void NormalizeAll(IEnumerable<PredicateNodeForm> nodes)
    {
        foreach (var n in nodes) Normalize(n);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/LeafNormalizer.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/LeafNormalizerTests.cs
git commit -m "feat(admin-rules): normalize leaf operator token to Kind/Op"
```

---

### Task 3: BranchEditTransforms (the structural list operations)

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/BranchEditTransforms.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/BranchEditTransformsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class BranchEditTransformsTests
{
    // root AllOf(1) -> [ FieldEq(2), AnyOf(3) -> [ FieldEq(4) ] ]
    private static List<PredicateNodeForm> Tree() => new()
    {
        new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
        new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        new() { Id = 3, ParentId = 1, Kind = PredicateKind.AnyOf },
        new() { Id = 4, ParentId = 3, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS2" },
    };

    [Fact]
    public void NextId_Is_Max_Plus_One()
    {
        Assert.Equal(5, BranchEditTransforms.NextId(Tree()));
        Assert.Equal(1, BranchEditTransforms.NextId(new List<PredicateNodeForm>()));
    }

    [Fact]
    public void AddCondition_Appends_Leaf_Under_Parent()
    {
        var list = Tree();
        BranchEditTransforms.AddCondition(list, parentId: 1);
        var added = list.Single(n => n.Id == 5);
        Assert.Equal(1, added.ParentId);
        Assert.Equal(PredicateKind.FieldEq, added.Kind); // sensible default leaf
        Assert.Equal("eq", added.Operator);
    }

    [Fact]
    public void AddGroup_Appends_Empty_AllOf_Under_Parent()
    {
        var list = Tree();
        BranchEditTransforms.AddGroup(list, parentId: 3);
        var added = list.Single(n => n.Id == 5);
        Assert.Equal(3, added.ParentId);
        Assert.Equal(PredicateKind.AllOf, added.Kind);
    }

    [Fact]
    public void Remove_Drops_Node_And_All_Descendants()
    {
        var list = Tree();
        BranchEditTransforms.Remove(list, id: 3); // removes AnyOf(3) and child FieldEq(4)
        Assert.DoesNotContain(list, n => n.Id is 3 or 4);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void GroupSelected_Wraps_Ticked_Siblings_In_New_Composite()
    {
        var list = Tree();
        list.Single(n => n.Id == 2).Selected = true;
        list.Single(n => n.Id == 3).Selected = true;

        BranchEditTransforms.GroupSelected(list, PredicateKind.AnyOf);

        var group = list.Single(n => n.Id == 5);
        Assert.Equal(PredicateKind.AnyOf, group.Kind);
        Assert.Equal(1, group.ParentId);                       // common parent of the selection
        Assert.Equal(5, list.Single(n => n.Id == 2).ParentId); // reparented into the new group
        Assert.Equal(5, list.Single(n => n.Id == 3).ParentId);
    }

    [Fact]
    public void Ungroup_Reparents_Children_And_Deletes_Composite()
    {
        var list = Tree();
        BranchEditTransforms.Ungroup(list, compositeId: 3); // child 4 moves up to parent 1
        Assert.DoesNotContain(list, n => n.Id == 3);
        Assert.Equal(1, list.Single(n => n.Id == 4).ParentId);
    }

    [Fact]
    public void SetCombinator_Changes_Composite_Kind()
    {
        var list = Tree();
        BranchEditTransforms.SetCombinator(list, id: 1, PredicateKind.AnyOf);
        Assert.Equal(PredicateKind.AnyOf, list.Single(n => n.Id == 1).Kind);
    }

    [Fact]
    public void SetField_Resets_Operator_And_Value_When_Type_Changes()
    {
        var list = Tree(); // node 2 is a String field with operator eq, value KS4
        BranchEditTransforms.SetField(list, id: 2, newField: "pupilAge"); // Number field
        var node = list.Single(n => n.Id == 2);
        Assert.Equal("pupilAge", node.Field);
        Assert.Equal("eq", node.Operator);  // eq is valid for Number, keep it
        Assert.Equal("", node.Value);       // value reset
    }

    [Fact]
    public void SetField_To_Type_Without_Current_Operator_Picks_First_Valid()
    {
        var list = Tree();
        list.Single(n => n.Id == 2).Operator = "in"; // valid for String, not offered for Bool
        BranchEditTransforms.SetField(list, id: 2, newField: "isAddBack"); // Bool
        var node = list.Single(n => n.Id == 2);
        Assert.Equal("eq", node.Operator); // first operator offered for Bool
    }

    [Fact]
    public void AddValue_And_RemoveValue_Mutate_FieldIn_List()
    {
        var list = new List<PredicateNodeForm>
        {
            new() { Id = 1, Kind = PredicateKind.FieldIn, Field = "inclusionFlag", Operator = "in",
                    Values = new List<string> { "A" } }
        };
        BranchEditTransforms.AddValue(list, id: 1);
        Assert.Equal(2, list[0].Values.Count);
        BranchEditTransforms.RemoveValue(list, id: 1, index: 0);
        Assert.Single(list[0].Values);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `BranchEditTransforms` does not exist.

- [ ] **Step 3: Implement**

`BranchEditTransforms.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure, in-place structural edits to the flat predicate-node list. Every editor
/// postback applies exactly one of these, then re-renders (no blob write).
/// </summary>
public static class BranchEditTransforms
{
    public static int NextId(IReadOnlyList<PredicateNodeForm> nodes) =>
        nodes.Count == 0 ? 1 : nodes.Max(n => n.Id) + 1;

    public static void AddCondition(List<PredicateNodeForm> nodes, int parentId) =>
        nodes.Add(new PredicateNodeForm
        {
            Id = NextId(nodes), ParentId = parentId,
            Kind = PredicateKind.FieldEq, Field = FirstField(), Operator = "eq", Value = ""
        });

    public static void AddGroup(List<PredicateNodeForm> nodes, int parentId) =>
        nodes.Add(new PredicateNodeForm
        {
            Id = NextId(nodes), ParentId = parentId, Kind = PredicateKind.AllOf
        });

    public static void Remove(List<PredicateNodeForm> nodes, int id)
    {
        var doomed = Descendants(nodes, id);
        doomed.Add(id);
        nodes.RemoveAll(n => doomed.Contains(n.Id));
    }

    public static void GroupSelected(List<PredicateNodeForm> nodes, PredicateKind kind)
    {
        var selected = nodes.Where(n => n.Selected).ToList();
        if (selected.Count == 0) return;

        var parentId = selected[0].ParentId; // group within the first selection's parent
        var group = new PredicateNodeForm { Id = NextId(nodes), ParentId = parentId, Kind = kind };

        // Insert the group at the position of the first selected node to preserve order.
        var firstIndex = nodes.IndexOf(selected[0]);
        nodes.Insert(firstIndex, group);

        foreach (var n in selected)
        {
            n.ParentId = group.Id;
            n.Selected = false;
        }
    }

    public static void Ungroup(List<PredicateNodeForm> nodes, int compositeId)
    {
        var composite = nodes.FirstOrDefault(n => n.Id == compositeId);
        if (composite is null) return;

        foreach (var child in nodes.Where(n => n.ParentId == compositeId))
        {
            child.ParentId = composite.ParentId;
        }
        nodes.Remove(composite);
    }

    public static void SetCombinator(List<PredicateNodeForm> nodes, int id, PredicateKind kind)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is not null) node.Kind = kind;
    }

    public static void SetField(List<PredicateNodeForm> nodes, int id, string newField)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is null) return;

        node.Field = newField;
        node.Value = "";
        node.Values = new List<string>();

        var allowed = LeafEditorOptions.OperatorTokensFor(newField).Select(o => o.Token).ToList();
        if (node.Operator is null || !allowed.Contains(node.Operator))
        {
            node.Operator = allowed.FirstOrDefault() ?? "eq";
        }
    }

    public static void AddValue(List<PredicateNodeForm> nodes, int id) =>
        nodes.FirstOrDefault(n => n.Id == id)?.Values.Add("");

    public static void RemoveValue(List<PredicateNodeForm> nodes, int id, int index)
    {
        var node = nodes.FirstOrDefault(n => n.Id == id);
        if (node is not null && index >= 0 && index < node.Values.Count) node.Values.RemoveAt(index);
    }

    private static List<int> Descendants(IReadOnlyList<PredicateNodeForm> nodes, int id)
    {
        var result = new List<int>();
        var direct = nodes.Where(n => n.ParentId == id).Select(n => n.Id).ToList();
        foreach (var childId in direct)
        {
            result.Add(childId);
            result.AddRange(Descendants(nodes, childId));
        }
        return result;
    }

    private static string FirstField() => FieldCatalogue.All.Keys.First();
}
```

> Note: `SetField` depends on `LeafEditorOptions.OperatorTokensFor` (Task 4). If you implement strictly in order, temporarily inline the allowed-token list here and replace it in Task 4, OR implement Task 4 first. Recommended: do Task 4 before Task 3's Step 3 so the dependency exists. (The two-stage reviewer should flag if the stub is left in.)

- [ ] **Step 4: Run to verify pass**

Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/BranchEditTransforms.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/BranchEditTransformsTests.cs
git commit -m "feat(admin-rules): predicate-tree structural transforms"
```

---

### Task 4: LeafEditorOptions (dropdown data) — implement before Task 3 Step 3

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/LeafEditorOptions.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/LeafEditorOptionsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class LeafEditorOptionsTests
{
    [Fact]
    public void Number_Field_Offers_Comparisons_Eq_And_Known()
    {
        var tokens = LeafEditorOptions.OperatorTokensFor("pupilAge").Select(o => o.Token).ToList();
        Assert.Equal(new[] { "lt", "lte", "gt", "gte", "eq", "known" }, tokens);
    }

    [Fact]
    public void String_Field_Offers_Eq_Neq_In_Known_Lang()
    {
        var tokens = LeafEditorOptions.OperatorTokensFor("keyStage").Select(o => o.Token).ToList();
        Assert.Equal(new[] { "eq", "neq", "in", "known", "lang" }, tokens);
    }

    [Fact]
    public void Bool_Field_Offers_Eq_Neq_Known()
    {
        var tokens = LeafEditorOptions.OperatorTokensFor("isAddBack").Select(o => o.Token).ToList();
        Assert.Equal(new[] { "eq", "neq", "known" }, tokens);
    }

    [Fact]
    public void Unknown_Field_Returns_Empty()
    {
        Assert.Empty(LeafEditorOptions.OperatorTokensFor("doesNotExist"));
    }

    [Fact]
    public void ValueEditor_Reflects_Operator_And_Type()
    {
        Assert.Equal(ValueEditorKind.None, LeafEditorOptions.ValueEditor("keyStage", "known"));
        Assert.Equal(ValueEditorKind.List, LeafEditorOptions.ValueEditor("keyStage", "in"));
        Assert.Equal(ValueEditorKind.Language, LeafEditorOptions.ValueEditor("keyStage", "lang"));
        Assert.Equal(ValueEditorKind.BoolSelect, LeafEditorOptions.ValueEditor("isAddBack", "eq"));
        Assert.Equal(ValueEditorKind.Number, LeafEditorOptions.ValueEditor("pupilAge", "gte"));
        Assert.Equal(ValueEditorKind.Date, LeafEditorOptions.ValueEditor("schoolAdmissionDate", "lt"));
        Assert.Equal(ValueEditorKind.Text, LeafEditorOptions.ValueEditor("keyStage", "eq"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `LeafEditorOptions` / `ValueEditorKind` do not exist.

- [ ] **Step 3: Implement**

`LeafEditorOptions.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public enum ValueEditorKind { Text, Number, Date, BoolSelect, List, Language, None }

public sealed record OperatorOption(string Token, string Label);

/// <summary>
/// Pure dropdown data for the leaf editor: which operators a field's type allows,
/// the value-editor shape per operator, the field list and status list.
/// </summary>
public static class LeafEditorOptions
{
    public static IReadOnlyList<string> Fields() => FieldCatalogue.All.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<OperatorOption> OperatorTokensFor(string field)
    {
        if (!FieldCatalogue.TryGetType(field, out var type)) return Array.Empty<OperatorOption>();

        return type switch
        {
            FieldType.String => new[]
            {
                new OperatorOption("eq", "equals"),
                new OperatorOption("neq", "does not equal"),
                new OperatorOption("in", "is one of"),
                new OperatorOption("known", "is known and certain"),
                new OperatorOption("lang", "official language is"),
            },
            FieldType.Number => new[]
            {
                new OperatorOption("lt", "is less than"),
                new OperatorOption("lte", "is less than or equal to"),
                new OperatorOption("gt", "is greater than"),
                new OperatorOption("gte", "is greater than or equal to"),
                new OperatorOption("eq", "equals"),
                new OperatorOption("known", "is known and certain"),
            },
            FieldType.Date => new[]
            {
                new OperatorOption("lt", "is before"),
                new OperatorOption("lte", "is on or before"),
                new OperatorOption("gt", "is after"),
                new OperatorOption("gte", "is on or after"),
                new OperatorOption("eq", "equals"),
            },
            FieldType.Bool => new[]
            {
                new OperatorOption("eq", "equals"),
                new OperatorOption("neq", "does not equal"),
                new OperatorOption("known", "is known and certain"),
            },
            _ => Array.Empty<OperatorOption>()
        };
    }

    public static ValueEditorKind ValueEditor(string? field, string? op) => op switch
    {
        "known" => ValueEditorKind.None,
        "in" => ValueEditorKind.List,
        "lang" => ValueEditorKind.Language,
        _ when field is not null && FieldCatalogue.TryGetType(field, out var t) => t switch
        {
            FieldType.Bool => ValueEditorKind.BoolSelect,
            FieldType.Number => ValueEditorKind.Number,
            FieldType.Date => ValueEditorKind.Date,
            _ => ValueEditorKind.Text
        },
        _ => ValueEditorKind.Text
    };

    public static IReadOnlyList<DecisionStatus> Statuses() =>
        new[] { DecisionStatus.AutoApproved, DecisionStatus.AutoRejected, DecisionStatus.Scrutiny };
}
```

- [ ] **Step 4: Run to verify pass**

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/LeafEditorOptions.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/LeafEditorOptionsTests.cs
git commit -m "feat(admin-rules): leaf editor dropdown options by field type"
```

---

### Task 5: PredicateFormValidator (pre-save structural guard — resolves the empty-composite open item)

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateFormValidator.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateFormValidatorTests.cs`

This catches half-built trees **before** calling the service: an empty `AllOf`/`AnyOf` (vacuous-true → matches everything) must not be saveable; `Not` needs exactly one child; `FieldIn` needs ≥1 value; leaves need a field; non-`known` leaves need a value. (Type/field-existence errors are left to `RuleSetValidator` on save.)

- [ ] **Step 1: Write the failing tests**

```csharp
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateFormValidatorTests
{
    [Fact]
    public void Empty_Composite_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } };
        var errors = PredicateFormValidator.Validate(nodes);
        Assert.Contains(errors, e => e.Contains("must contain at least one"));
    }

    [Fact]
    public void Not_With_Two_Children_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.Not },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.IsKnownAndCertain, Field = "keyStage", Operator = "known" },
            new() { Id = 3, ParentId = 1, Kind = PredicateKind.IsKnownAndCertain, Field = "pupilAge", Operator = "known" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("exactly one"));
    }

    [Fact]
    public void Leaf_Without_Field_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "", Operator = "eq", Value = "x" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("needs a field"));
    }

    [Fact]
    public void Eq_Without_Value_Is_Rejected_But_Known_Is_Allowed()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "" },
            new() { Id = 3, ParentId = 1, Kind = PredicateKind.IsKnownAndCertain, Field = "pupilAge", Operator = "known" },
        };
        var errors = PredicateFormValidator.Validate(nodes);
        Assert.Contains(errors, e => e.Contains("needs a value"));
        Assert.Single(errors); // the 'known' leaf is fine
    }

    [Fact]
    public void FieldIn_With_No_Values_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldIn, Field = "inclusionFlag", Operator = "in" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("at least one value"));
    }

    [Fact]
    public void Well_Formed_Tree_Has_No_Errors()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        };
        Assert.Empty(PredicateFormValidator.Validate(nodes));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `PredicateFormValidator` does not exist.

- [ ] **Step 3: Implement**

`PredicateFormValidator.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Web-layer structural pre-check run before <c>SaveRulesAsync</c>. Stops half-built
/// trees (notably an empty AllOf/AnyOf, which is vacuously true and would match every
/// request) from reaching the service. Field-existence and literal-type errors are left
/// to the Application <c>RuleSetValidator</c> on save.
/// </summary>
public static class PredicateFormValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<PredicateNodeForm> nodes)
    {
        var errors = new List<string>();
        foreach (var node in nodes)
        {
            var childCount = nodes.Count(n => n.ParentId == node.Id);
            switch (node.Kind)
            {
                case PredicateKind.AllOf:
                case PredicateKind.AnyOf:
                    if (childCount == 0)
                        errors.Add("A group must contain at least one condition.");
                    break;
                case PredicateKind.Not:
                    if (childCount != 1)
                        errors.Add("A 'not' group must contain exactly one condition.");
                    break;
                case PredicateKind.FieldIn:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    if (node.Values.Count == 0 || node.Values.All(string.IsNullOrWhiteSpace))
                        errors.Add($"'{node.Field}' must list at least one value.");
                    break;
                case PredicateKind.IsKnownAndCertain:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    break;
                case PredicateKind.OfficialLanguageIs:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    if (string.IsNullOrWhiteSpace(node.Value))
                        errors.Add("An 'official language is' condition needs a language.");
                    break;
                case PredicateKind.FieldEq:
                case PredicateKind.FieldNeq:
                case PredicateKind.FieldCompare:
                    if (string.IsNullOrWhiteSpace(node.Field))
                        errors.Add("A condition needs a field.");
                    if (string.IsNullOrWhiteSpace(node.Value))
                        errors.Add($"'{node.Field}' needs a value.");
                    break;
            }
        }
        return errors;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/PredicateFormValidator.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/PredicateFormValidatorTests.cs
git commit -m "feat(admin-rules): pre-save structural validation guards empty composites"
```

---

### Task 6: RuleSetSplicer (replace/insert/remove/move a branch in a RuleSet)

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/RuleSetSplicer.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/RuleSetSplicerTests.cs`

Pure structural edits to a `RuleSet`, used by save/remove/move. `otherwise` is always pinned last; inserts go before it; it cannot be removed or moved.

- [ ] **Step 1: Write the failing tests**

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class RuleSetSplicerTests
{
    private static RuleSet Sample() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("EAL", "EAL", new[]
        {
            new RuleBranch("EAL-1", DecisionStatus.AutoApproved,
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))),
            new RuleBranch("EAL-2", DecisionStatus.Scrutiny,
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2"))),
            new RuleBranch("EAL-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
        })
    });

    [Fact]
    public void ReplaceBranch_Swaps_Matching_Id_Keeps_Position()
    {
        var updated = new RuleBranch("EAL-1", DecisionStatus.AutoRejected,
            new Predicate.IsKnownAndCertain("keyStage"));

        var result = RuleSetSplicer.ReplaceBranch(Sample(), "EAL", "EAL-1", updated);

        var branch = result.Outcomes[0].Rules[0];
        Assert.Equal(DecisionStatus.AutoRejected, branch.Status);
        Assert.IsType<Predicate.IsKnownAndCertain>(branch.When);
    }

    [Fact]
    public void InsertBranch_Adds_Before_Otherwise()
    {
        var added = new RuleBranch("EAL-3", DecisionStatus.Scrutiny,
            new Predicate.IsKnownAndCertain("keyStage"));

        var result = RuleSetSplicer.InsertBranch(Sample(), "EAL", added);

        var rules = result.Outcomes[0].Rules;
        Assert.Equal("EAL-3", rules[^2].Id);                 // second-to-last
        Assert.IsType<Predicate.Otherwise>(rules[^1].When);  // otherwise stays last
    }

    [Fact]
    public void RemoveBranch_Drops_It()
    {
        var result = RuleSetSplicer.RemoveBranch(Sample(), "EAL", "EAL-1");
        Assert.DoesNotContain(result.Outcomes[0].Rules, b => b.Id == "EAL-1");
        Assert.Equal(2, result.Outcomes[0].Rules.Count);
    }

    [Fact]
    public void RemoveBranch_Refuses_Otherwise()
    {
        Assert.Throws<InvalidOperationException>(() => RuleSetSplicer.RemoveBranch(Sample(), "EAL", "EAL-OTHER"));
    }

    [Fact]
    public void MoveBranch_Up_Swaps_With_Previous()
    {
        var result = RuleSetSplicer.MoveBranch(Sample(), "EAL", "EAL-2", up: true);
        Assert.Equal("EAL-2", result.Outcomes[0].Rules[0].Id);
        Assert.Equal("EAL-1", result.Outcomes[0].Rules[1].Id);
    }

    [Fact]
    public void MoveBranch_Down_Cannot_Pass_Otherwise()
    {
        // EAL-2 is already last before otherwise; moving down is a no-op (cannot cross otherwise).
        var result = RuleSetSplicer.MoveBranch(Sample(), "EAL", "EAL-2", up: false);
        Assert.Equal("EAL-2", result.Outcomes[0].Rules[1].Id);
        Assert.IsType<Predicate.Otherwise>(result.Outcomes[0].Rules[2].When);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `RuleSetSplicer` does not exist.

- [ ] **Step 3: Implement**

`RuleSetSplicer.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure edits to a <see cref="RuleSet"/> for the editor. The terminal
/// <see cref="Predicate.Otherwise"/> branch is always pinned last: inserts land
/// before it; it cannot be removed or moved past.
/// </summary>
public static class RuleSetSplicer
{
    public static RuleSet ReplaceBranch(RuleSet rules, string outcomeKey, string branchId, RuleBranch updated) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var i = list.FindIndex(b => b.Id == branchId);
            if (i >= 0) list[i] = updated;
            return list;
        });

    public static RuleSet InsertBranch(RuleSet rules, string outcomeKey, RuleBranch newBranch) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var otherwiseIndex = list.FindIndex(b => b.When is Predicate.Otherwise);
            if (otherwiseIndex < 0) list.Add(newBranch);
            else list.Insert(otherwiseIndex, newBranch);
            return list;
        });

    public static RuleSet RemoveBranch(RuleSet rules, string outcomeKey, string branchId) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var branch = list.FirstOrDefault(b => b.Id == branchId)
                ?? throw new InvalidOperationException($"Branch '{branchId}' not found.");
            if (branch.When is Predicate.Otherwise)
                throw new InvalidOperationException("The 'otherwise' branch cannot be removed.");
            list.Remove(branch);
            return list;
        });

    public static RuleSet MoveBranch(RuleSet rules, string outcomeKey, string branchId, bool up) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var i = list.FindIndex(b => b.Id == branchId);
            if (i < 0 || list[i].When is Predicate.Otherwise) return list;

            var j = up ? i - 1 : i + 1;
            if (j < 0 || j >= list.Count) return list;
            if (list[j].When is Predicate.Otherwise) return list; // never cross otherwise

            (list[i], list[j]) = (list[j], list[i]);
            return list;
        });

    private static RuleSet MapOutcome(RuleSet rules, string key, Func<IReadOnlyList<RuleBranch>, List<RuleBranch>> edit)
    {
        var outcomes = rules.Outcomes.Select(o =>
            o.Key == key ? o with { Rules = edit(o.Rules) } : o).ToList();
        return rules with { Outcomes = outcomes };
    }
}
```

- [ ] **Step 4: Run to verify pass**

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/RuleSetSplicer.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/RuleSetSplicerTests.cs
git commit -m "feat(admin-rules): pure RuleSet splicer (replace/insert/remove/move)"
```

---

### Task 7: BranchEditForm + BranchEditViewModel

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/BranchEditForm.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/BranchEditViewModel.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/BranchEditViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class BranchEditViewModelTests
{
    [Fact]
    public void For_Builds_From_Form_With_Dropdown_Data()
    {
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", OutcomeLabel = "EAL", BranchId = "EAL-1",
            Status = DecisionStatus.Scrutiny, LoadETag = "etag-1", IsNew = false,
            Nodes = new List<PredicateNodeForm> { new() { Id = 1, Kind = PredicateKind.AllOf } }
        };

        var vm = BranchEditViewModel.For(form, errors: new[] { "boom" });

        Assert.Same(form, vm.Form);
        Assert.Contains("boom", vm.Errors);
        Assert.Contains("keyStage", vm.AllFields);
        Assert.Contains(DecisionStatus.Scrutiny, vm.Statuses);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement the form**

`BranchEditForm.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>The posted state of the whole-branch editor. Round-trips through every postback.</summary>
public sealed class BranchEditForm
{
    public string OutcomeKey { get; set; } = string.Empty;
    public string OutcomeLabel { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public DecisionStatus Status { get; set; } = DecisionStatus.Scrutiny;
    public string? LoadETag { get; set; }              // rules blob ETag captured at GET
    public List<PredicateNodeForm> Nodes { get; set; } = new();
    public string? Action { get; set; }                // "<verb>:<arg>" from the clicked button
}
```

- [ ] **Step 4: Implement the view model**

`BranchEditViewModel.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public sealed class BranchEditViewModel
{
    public required BranchEditForm Form { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> AllFields { get; init; }
    public required IReadOnlyList<DecisionStatus> Statuses { get; init; }
    public bool ConcurrencyConflict { get; init; }

    public static BranchEditViewModel For(BranchEditForm form, IReadOnlyList<string>? errors = null,
        bool concurrencyConflict = false) => new()
    {
        Form = form,
        Errors = errors ?? Array.Empty<string>(),
        AllFields = LeafEditorOptions.Fields(),
        Statuses = LeafEditorOptions.Statuses(),
        ConcurrencyConflict = concurrencyConflict
    };
}
```

- [ ] **Step 5: Run to verify pass**

Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/BranchEditForm.cs \
        src/DfE.CheckPerformanceData.Web/Admin/Rules/BranchEditViewModel.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/Rules/BranchEditViewModelTests.cs
git commit -m "feat(admin-rules): branch editor form + view model"
```

---

### Task 8: Controller — branch editor GET + add (seed) GET

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class AdminRulesControllerEditTests
{
    private static RuleSet Rules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("EAL", "EAL", new[]
        {
            new RuleBranch("EAL-1", DecisionStatus.Scrutiny, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))),
            new RuleBranch("EAL-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
        })
    });

    private static IRulesConfigService SvcWithRules(string etag = "etag-1")
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((Rules(), etag));
        return svc;
    }

    private static AdminRulesController NewController(IRulesConfigService svc) => new(svc);

    [Fact]
    public async Task EditBranch_Loads_Form_With_Captured_ETag()
    {
        var result = await NewController(SvcWithRules("etag-xyz")).EditBranch("EAL", "EAL-1", default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("EAL-1", vm.Form.BranchId);
        Assert.False(vm.Form.IsNew);
        Assert.Equal("etag-xyz", vm.Form.LoadETag);
        Assert.NotEmpty(vm.Form.Nodes); // flattened predicate
    }

    [Fact]
    public async Task EditBranch_NotFound_For_Unknown_Branch()
    {
        var result = await NewController(SvcWithRules()).EditBranch("EAL", "nope", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditBranch_Refuses_Otherwise_Branch()
    {
        var result = await NewController(SvcWithRules()).EditBranch("EAL", "EAL-OTHER", default);
        Assert.IsType<NotFoundResult>(result); // otherwise is not editable
    }

    [Fact]
    public async Task AddBranch_Seeds_New_Form_With_Empty_AllOf()
    {
        var result = await NewController(SvcWithRules()).AddBranch("EAL", default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.Form.IsNew);
        Assert.Single(vm.Form.Nodes);
        Assert.Equal(PredicateKind.AllOf, vm.Form.Nodes[0].Kind);
        Assert.StartsWith("EAL-", vm.Form.BranchId); // generated id
        Assert.Equal(DecisionStatus.Scrutiny, vm.Form.Status);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `EditBranch`/`AddBranch` do not exist.

- [ ] **Step 3: Add usings + view-path consts + the two GET actions to `AdminRulesController`**

Add to the top usings (if not present): `using DfE.CheckPerformanceData.Web.Admin.Rules;` (already present per M2).

Add a view-path const alongside the existing ones:

```csharp
    private const string BranchEditView = "~/Views/Admin/Rules/BranchEdit.cshtml";
    private const string RemoveBranchView = "~/Views/Admin/Rules/RemoveBranch.cshtml";
```

Add these actions (after the existing GET actions, before the helpers):

```csharp
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

    private static string NewBranchId(OutcomeRules outcome)
    {
        for (var n = 1; ; n++)
        {
            var candidate = $"{outcome.Key}-{n}";
            if (outcome.Rules.All(b => b.Id != candidate)) return candidate;
        }
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test ...UnitTests... --filter FullyQualifiedName~AdminRulesControllerEditTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs
git commit -m "feat(admin-rules): branch editor GET + add-branch seed"
```

---

### Task 9: Controller — transform POST (re-render, no write)

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`
- Modify: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
    [Fact]
    public async Task Transform_AddCondition_ReRenders_Without_Persisting()
    {
        var svc = SvcWithRules();
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", BranchId = "EAL-1", Status = DecisionStatus.Scrutiny, LoadETag = "etag-1",
            Action = "addCondition:1",
            Nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } }
        };

        var result = await NewController(svc).TransformBranch(form, default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, vm.Form.Nodes.Count); // condition added
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transform_GroupSelected_Builds_New_Composite()
    {
        var svc = SvcWithRules();
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", BranchId = "EAL-1", LoadETag = "etag-1", Action = "group:any",
            Nodes = new List<PredicateNodeForm>
            {
                new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
                new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "A", Selected = true },
                new() { Id = 3, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "B", Selected = true },
            }
        };

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).TransformBranch(form, default)).Model);

        Assert.Contains(vm.Form.Nodes, n => n.Kind == PredicateKind.AnyOf && n.ParentId == 1);
    }
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `TransformBranch` does not exist.

- [ ] **Step 3: Implement the transform action**

Add to `AdminRulesController`:

```csharp
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
```

> `setField`'s second arg can contain no `:` because field names are catalogue identifiers; safe to split on `:`. If a field name ever contained `:`, revisit — none do today.

- [ ] **Step 4: Run to verify pass**

Expected: PASS (2 new tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs
git commit -m "feat(admin-rules): branch transform postback (no persistence)"
```

---

### Task 10: Controller — save POST (validate → concurrency → splice → persist)

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`
- Modify: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
    private static BranchEditForm SaveableForm(string etag) => new()
    {
        OutcomeKey = "EAL", BranchId = "EAL-1", IsNew = false, Status = DecisionStatus.AutoApproved,
        LoadETag = etag, Action = "save",
        Nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        }
    };

    [Fact]
    public async Task Save_Persists_And_Redirects_On_Success()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(5));

        var result = await NewController(svc).SaveBranch(SaveableForm("etag-1"), default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Outcome", redirect.ActionName);
        await svc.Received(1).SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_Blocks_On_Empty_Composite_Without_Calling_Service()
    {
        var svc = SvcWithRules("etag-1");
        var form = SaveableForm("etag-1");
        form.Nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } }; // empty

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(form, default)).Model);

        Assert.NotEmpty(vm.Errors);
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_Shows_Error_Summary_On_Validation_Failure()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Invalid(new[] { "bad rules" }));

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(SaveableForm("etag-1"), default)).Model);

        Assert.Contains("bad rules", vm.Errors);
    }

    [Fact]
    public async Task Save_Blocks_On_Concurrency_Conflict()
    {
        var svc = SvcWithRules("etag-CURRENT"); // store moved on since the editor loaded
        var form = SaveableForm("etag-STALE");

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(form, default)).Model);

        Assert.True(vm.ConcurrencyConflict);
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_New_Branch_Inserts_Before_Otherwise()
    {
        var svc = SvcWithRules("etag-1");
        RuleSet? captured = null;
        svc.SaveRulesAsync(Arg.Do<RuleSet>(r => captured = r), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(2));

        var form = SaveableForm("etag-1");
        form.IsNew = true;
        form.BranchId = "EAL-NEW";

        await NewController(svc).SaveBranch(form, default);

        var rules = captured!.Outcomes.First(o => o.Key == "EAL").Rules;
        Assert.Equal("EAL-NEW", rules[^2].Id);
        Assert.IsType<Predicate.Otherwise>(rules[^1].When);
    }
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `SaveBranch` does not exist.

- [ ] **Step 3: Implement the save action**

Add to `AdminRulesController`:

```csharp
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
        if (currentETag != form.LoadETag)
        {
            return View(BranchEditView, BranchEditViewModel.For(form, concurrencyConflict: true));
        }

        var predicate = PredicateForm.RebuildPredicate(form.Nodes);
        var branch = new RuleBranch(form.BranchId, form.Status, predicate);
        var spliced = form.IsNew
            ? RuleSetSplicer.InsertBranch(current, form.OutcomeKey, branch)
            : RuleSetSplicer.ReplaceBranch(current, form.OutcomeKey, form.BranchId, branch);

        var result = await rules.SaveRulesAsync(spliced, form.LoadETag, ct);
        if (!result.Saved)
        {
            return View(BranchEditView, BranchEditViewModel.For(form, result.Errors));
        }

        TempData["SuccessMessage"] =
            $"Branch '{form.BranchId}' saved (version {result.VersionNumber}). The rules service refreshes within about 5 minutes.";
        return RedirectToAction(nameof(Outcome), new { key = form.OutcomeKey });
    }
```

- [ ] **Step 4: Run to verify pass**

Expected: PASS (5 new tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs
git commit -m "feat(admin-rules): branch save with validation, concurrency guard, splice"
```

---

### Task 11: Controller — remove branch (confirm + POST) + move branch

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`
- Modify: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
    [Fact]
    public async Task ConfirmRemoveBranch_Returns_View_For_Editable_Branch()
    {
        var result = await NewController(SvcWithRules()).ConfirmRemoveBranch("EAL", "EAL-1", default);
        var model = Assert.IsType<BranchViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("EAL-1", model.Id);
    }

    [Fact]
    public async Task ConfirmRemoveBranch_NotFound_For_Otherwise()
    {
        var result = await NewController(SvcWithRules()).ConfirmRemoveBranch("EAL", "EAL-OTHER", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RemoveBranch_Persists_And_Redirects()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(3));

        var result = await NewController(svc).RemoveBranch("EAL", "EAL-1", default);

        Assert.IsType<RedirectToActionResult>(result);
        await svc.Received(1).SaveRulesAsync(
            Arg.Is<RuleSet>(r => r.Outcomes.First(o => o.Key == "EAL").Rules.All(b => b.Id != "EAL-1")),
            "etag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveBranch_Persists_Reordered_Set()
    {
        var svc = SvcWithRules("etag-1");
        // add a second editable branch so there is something to swap with
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((new RuleSet("v1", DateTimeOffset.UnixEpoch, new[]
        {
            new OutcomeRules("EAL", "EAL", new[]
            {
                new RuleBranch("EAL-1", DecisionStatus.Scrutiny, new Predicate.IsKnownAndCertain("keyStage")),
                new RuleBranch("EAL-2", DecisionStatus.Scrutiny, new Predicate.IsKnownAndCertain("pupilAge")),
                new RuleBranch("EAL-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
            })
        }), "etag-1"));
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(4));

        var result = await NewController(svc).MoveBranch("EAL", "EAL-2", "up", default);

        Assert.IsType<RedirectToActionResult>(result);
        await svc.Received(1).SaveRulesAsync(
            Arg.Is<RuleSet>(r => r.Outcomes.First(o => o.Key == "EAL").Rules[0].Id == "EAL-2"),
            "etag-1", Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — actions do not exist.

- [ ] **Step 3: Implement**

Add to `AdminRulesController`:

```csharp
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

        var result = await rules.SaveRulesAsync(spliced, etag, ct);
        TempData["SuccessMessage"] = result.Saved
            ? $"Branch '{id}' removed (version {result.VersionNumber})."
            : "Could not remove the branch: " + string.Join("; ", result.Errors);
        return RedirectToAction(nameof(Outcome), new { key });
    }

    [HttpPost("admin/rules/outcomes/{key}/branches/{id}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveBranch(string key, string id, string direction, CancellationToken ct)
    {
        var (current, etag) = await TryGetRulesAsync(ct);
        if (current is null) return NotFound();

        var spliced = RuleSetSplicer.MoveBranch(current, key, id, up: direction == "up");
        var result = await rules.SaveRulesAsync(spliced, etag, ct);
        if (!result.Saved)
        {
            TempData["SuccessMessage"] = "Could not reorder: " + string.Join("; ", result.Errors);
        }
        return RedirectToAction(nameof(Outcome), new { key });
    }
```

- [ ] **Step 4: Run to verify pass**

Expected: PASS (4 new tests).

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs
git commit -m "feat(admin-rules): remove (with confirm) and reorder branches"
```

---

### Task 12: Recursive editor partial `_PredicateEditorNode.cshtml`

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_PredicateEditorNode.cshtml`

No unit test (Razor). Verified by E2E (Task 16) and the manual checklist (Task 17). The partial renders ONE node by its index into `Model.Form.Nodes`; visible controls use flat `Nodes[{index}].*` names so the binder reconstructs the list regardless of DOM nesting. Children are rendered recursively inside the card.

- [ ] **Step 1: Create the partial**

```razor
@using DfE.CheckPerformanceData.Application.RulesEngine
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model DfE.CheckPerformanceData.Web.Views.Admin.Rules.EditorNodeContext
@{
    var node = Model.Node;
    var i = Model.Index;
    var isComposite = node.Kind is PredicateKind.AllOf or PredicateKind.AnyOf or PredicateKind.Not;
    var children = Model.ChildrenOf(node.Id);
    var ops = node.Field is null ? Array.Empty<OperatorOption>() : LeafEditorOptions.OperatorTokensFor(node.Field);
    var valueEditor = LeafEditorOptions.ValueEditor(node.Field, node.Operator);
}

<input type="hidden" name="Nodes[@i].Id" value="@node.Id" />
<input type="hidden" name="Nodes[@i].ParentId" value="@(node.ParentId?.ToString() ?? "")" />
<input type="hidden" name="Nodes[@i].Kind" value="@node.Kind" />
<input type="hidden" name="Nodes[@i].Op" value="@node.Op" />

@if (isComposite)
{
    <div class="govuk-inset-text govuk-!-margin-top-2">
        <div class="govuk-form-group govuk-!-margin-bottom-2">
            <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Combinator">Match</label>
            <select class="govuk-select" id="Nodes_@(i)__Combinator" name="Nodes[@i].Kind">
                <option value="@PredicateKind.AllOf" selected="@(node.Kind == PredicateKind.AllOf)">All of these</option>
                <option value="@PredicateKind.AnyOf" selected="@(node.Kind == PredicateKind.AnyOf)">Any of these</option>
                <option value="@PredicateKind.Not" selected="@(node.Kind == PredicateKind.Not)">Not (one condition)</option>
            </select>
            <button class="govuk-button govuk-button--secondary govuk-!-margin-left-2" data-module="govuk-button"
                    name="Action" value="setCombinator:@node.Id:@node.Kind" type="submit">Change</button>
        </div>

        @foreach (var child in children)
        {
            @await Html.PartialAsync("_PredicateEditorNode", Model.Recurse(child))
        }

        <div class="govuk-button-group govuk-!-margin-top-2">
            <button class="govuk-button govuk-button--secondary" data-module="govuk-button"
                    name="Action" value="addCondition:@node.Id" type="submit">Add condition</button>
            <button class="govuk-button govuk-button--secondary" data-module="govuk-button"
                    name="Action" value="addGroup:@node.Id" type="submit">Add group</button>
            @if (node.ParentId is not null)
            {
                <button class="govuk-button govuk-button--warning" data-module="govuk-button"
                        name="Action" value="ungroup:@node.Id" type="submit">Ungroup</button>
                <button class="govuk-button govuk-button--warning" data-module="govuk-button"
                        name="Action" value="remove:@node.Id" type="submit">Remove group</button>
            }
        </div>
    </div>
}
else
{
    <div class="govuk-!-margin-bottom-2" style="display:flex; gap:10px; align-items:flex-end; flex-wrap:wrap;">
        <div class="govuk-checkboxes__item govuk-checkboxes--small">
            <input class="govuk-checkboxes__input" type="checkbox" id="Nodes_@(i)__Selected"
                   name="Nodes[@i].Selected" value="true" checked="@node.Selected" />
            <label class="govuk-label govuk-checkboxes__label" for="Nodes_@(i)__Selected">Select</label>
        </div>

        <div class="govuk-form-group govuk-!-margin-bottom-0">
            <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Field">Field</label>
            <select class="govuk-select" id="Nodes_@(i)__Field" name="Nodes[@i].Field">
                @foreach (var f in Model.AllFields)
                {
                    <option value="@f" selected="@(f == node.Field)">@f</option>
                }
            </select>
        </div>
        <button class="govuk-button govuk-button--secondary govuk-!-margin-bottom-0" data-module="govuk-button"
                name="Action" value="setField:@node.Id:@node.Field" type="submit">Update field</button>

        <div class="govuk-form-group govuk-!-margin-bottom-0">
            <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Operator">Operator</label>
            <select class="govuk-select" id="Nodes_@(i)__Operator" name="Nodes[@i].Operator">
                @foreach (var op in ops)
                {
                    <option value="@op.Token" selected="@(op.Token == node.Operator)">@op.Label</option>
                }
            </select>
        </div>

        @switch (valueEditor)
        {
            case ValueEditorKind.None:
                break;
            case ValueEditorKind.BoolSelect:
                <div class="govuk-form-group govuk-!-margin-bottom-0">
                    <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Value">Value</label>
                    <select class="govuk-select" id="Nodes_@(i)__Value" name="Nodes[@i].Value">
                        <option value="true" selected="@(node.Value == "true")">true</option>
                        <option value="false" selected="@(node.Value == "false")">false</option>
                    </select>
                </div>
                break;
            case ValueEditorKind.Number:
                <div class="govuk-form-group govuk-!-margin-bottom-0">
                    <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Value">Value</label>
                    <input class="govuk-input govuk-input--width-10" type="number" step="any"
                           id="Nodes_@(i)__Value" name="Nodes[@i].Value" value="@node.Value" />
                </div>
                break;
            case ValueEditorKind.Date:
                <div class="govuk-form-group govuk-!-margin-bottom-0">
                    <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Value">Value</label>
                    <input class="govuk-input govuk-input--width-10" type="date"
                           id="Nodes_@(i)__Value" name="Nodes[@i].Value" value="@node.Value" />
                </div>
                break;
            case ValueEditorKind.Language:
                <div class="govuk-form-group govuk-!-margin-bottom-0">
                    <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Value">Language</label>
                    <input class="govuk-input" id="Nodes_@(i)__Value" name="Nodes[@i].Value" value="@node.Value" />
                </div>
                break;
            case ValueEditorKind.List:
                <div class="govuk-form-group govuk-!-margin-bottom-0">
                    <label class="govuk-label govuk-label--s">Values</label>
                    @for (var v = 0; v < node.Values.Count; v++)
                    {
                        <div style="display:flex; gap:6px; margin-bottom:4px;">
                            <input class="govuk-input govuk-input--width-10" name="Nodes[@i].Values[@v]" value="@node.Values[v]" />
                            <button class="govuk-button govuk-button--warning govuk-!-margin-bottom-0" data-module="govuk-button"
                                    name="Action" value="removeValue:@node.Id:@v" type="submit">Remove</button>
                        </div>
                    }
                    <button class="govuk-button govuk-button--secondary govuk-!-margin-bottom-0" data-module="govuk-button"
                            name="Action" value="addValue:@node.Id" type="submit">Add value</button>
                </div>
                break;
            default:
                <div class="govuk-form-group govuk-!-margin-bottom-0">
                    <label class="govuk-label govuk-label--s" for="Nodes_@(i)__Value">Value</label>
                    <input class="govuk-input" id="Nodes_@(i)__Value" name="Nodes[@i].Value" value="@node.Value" />
                </div>
                break;
        }

        <button class="govuk-button govuk-button--warning govuk-!-margin-bottom-0" data-module="govuk-button"
                name="Action" value="remove:@node.Id" type="submit">Remove condition</button>
    </div>
}
```

- [ ] **Step 2: Create the partial's context type**

Create `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/EditorNodeContext.cs`:

```csharp
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Web.Views.Admin.Rules;

/// <summary>
/// View context for the recursive predicate editor partial. Carries the node being
/// rendered, its index in the flat list (for input names), the full node list (to find
/// children and assign child indices) and the field dropdown data.
/// </summary>
public sealed class EditorNodeContext
{
    public required IReadOnlyList<PredicateNodeForm> Nodes { get; init; }
    public required IReadOnlyList<string> AllFields { get; init; }
    public required PredicateNodeForm Node { get; init; }

    public int Index => IndexOf(Node.Id);

    private int IndexOf(int id)
    {
        for (var k = 0; k < Nodes.Count; k++)
        {
            if (Nodes[k].Id == id) return k;
        }
        return -1;
    }

    public IReadOnlyList<PredicateNodeForm> ChildrenOf(int parentId) =>
        Nodes.Where(n => n.ParentId == parentId).ToList();

    public EditorNodeContext Recurse(PredicateNodeForm child) => new()
    {
        Nodes = Nodes, AllFields = AllFields, Node = child
    };
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build C:\Repos\DfE\check-performance-data\src\DfE.CheckPerformanceData.Web\DfE.CheckPerformanceData.Web.csproj`
Expected: Build succeeded (no view compilation errors if Razor runtime-compilation is off; if views compile at build, the partial + context must resolve).

- [ ] **Step 4: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/_PredicateEditorNode.cshtml \
        src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/EditorNodeContext.cs
git commit -m "feat(admin-rules): recursive predicate editor partial"
```

---

### Task 13: BranchEdit.cshtml + RemoveBranch.cshtml views

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/BranchEdit.cshtml`
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/RemoveBranch.cshtml`

- [ ] **Step 1: Create `BranchEdit.cshtml`**

```razor
@using DfE.CheckPerformanceData.Application.RulesEngine
@using DfE.CheckPerformanceData.Web.Admin.Rules
@using DfE.CheckPerformanceData.Web.Views.Admin.Rules
@model BranchEditViewModel
@{
    ViewData["Title"] = Model.Form.IsNew ? "Add branch" : $"Edit branch {Model.Form.BranchId}";
    var root = Model.Form.Nodes.FirstOrDefault(n => n.ParentId is null);
}

<a href="/admin/rules/outcomes/@Model.Form.OutcomeKey" class="govuk-back-link">Back to @Model.Form.OutcomeLabel</a>

@if (Model.ConcurrencyConflict)
{
    <div class="govuk-error-summary" data-module="govuk-error-summary">
        <div role="alert">
            <h2 class="govuk-error-summary__title">These rules were changed by someone else</h2>
            <div class="govuk-error-summary__body">
                <p class="govuk-body">Since you opened this page the rules were saved by another admin.
                Reload to see the latest version, then re-apply your change. Nothing has been saved.</p>
                <a class="govuk-link" href="/admin/rules/outcomes/@Model.Form.OutcomeKey/branches/@Model.Form.BranchId/edit">Reload this branch</a>
            </div>
        </div>
    </div>
}
else if (Model.Errors.Count > 0)
{
    <div class="govuk-error-summary" data-module="govuk-error-summary">
        <div role="alert">
            <h2 class="govuk-error-summary__title">There is a problem</h2>
            <div class="govuk-error-summary__body">
                <ul class="govuk-list govuk-error-summary__list">
                    @foreach (var error in Model.Errors)
                    {
                        <li>@error</li>
                    }
                </ul>
            </div>
        </div>
    </div>
}

<span class="govuk-caption-xl">@Model.Form.OutcomeLabel</span>
<h1 class="govuk-heading-xl">@ViewData["Title"]</h1>

<form method="post" action="/admin/rules/branch/save">
    @Html.AntiForgeryToken()
    <input type="hidden" name="OutcomeKey" value="@Model.Form.OutcomeKey" />
    <input type="hidden" name="OutcomeLabel" value="@Model.Form.OutcomeLabel" />
    <input type="hidden" name="BranchId" value="@Model.Form.BranchId" />
    <input type="hidden" name="IsNew" value="@Model.Form.IsNew.ToString().ToLowerInvariant()" />
    <input type="hidden" name="LoadETag" value="@Model.Form.LoadETag" />

    <div class="govuk-form-group">
        <label class="govuk-label govuk-label--s" for="Status">Decision</label>
        <select class="govuk-select" id="Status" name="Status">
            @foreach (var status in Model.Statuses)
            {
                <option value="@status" selected="@(status == Model.Form.Status)">@status</option>
            }
        </select>
    </div>

    <h2 class="govuk-heading-l">Condition</h2>
    @if (root is not null)
    {
        @await Html.PartialAsync("_PredicateEditorNode", new EditorNodeContext
        {
            Nodes = Model.Form.Nodes, AllFields = Model.AllFields, Node = root
        })
    }

    <div class="govuk-button-group govuk-!-margin-top-4">
        <button class="govuk-button govuk-button--secondary" data-module="govuk-button"
                name="Action" value="group:all" formaction="/admin/rules/branch/transform" type="submit">Group selected as ALL</button>
        <button class="govuk-button govuk-button--secondary" data-module="govuk-button"
                name="Action" value="group:any" formaction="/admin/rules/branch/transform" type="submit">Group selected as ANY</button>
        <button class="govuk-button govuk-button--secondary" data-module="govuk-button"
                name="Action" value="ungroupSelected" formaction="/admin/rules/branch/transform" type="submit">Ungroup selected</button>
    </div>

    <div class="govuk-button-group govuk-!-margin-top-4">
        <button class="govuk-button" data-module="govuk-button" name="Action" value="save" type="submit">Save branch</button>
        <a class="govuk-link" href="/admin/rules/outcomes/@Model.Form.OutcomeKey">Cancel</a>
    </div>
</form>
```

> **Binding note (important):** every structural button uses `formaction="/admin/rules/branch/transform"` so it posts to the transform action and re-renders; only **Save branch** submits to the form's default `action` (`/admin/rules/branch/save`). The combinator/operator/field controls inside the partial post their values with whichever button is clicked, so the user's in-progress edits survive each postback. `Action` carries the verb.

- [ ] **Step 2: Create `RemoveBranch.cshtml`**

```razor
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model BranchViewModel
@{
    ViewData["Title"] = $"Remove branch {Model.Id}";
    var key = ViewData["OutcomeKey"] as string ?? "";
}

<a href="/admin/rules/outcomes/@key" class="govuk-back-link">Back</a>

<h1 class="govuk-heading-xl">Remove branch @Model.Id?</h1>
<p class="govuk-body">This permanently removes the branch from the outcome. A new version is recorded and the change can be rolled back from version history.</p>

<div class="govuk-inset-text">
    <ul class="govuk-list govuk-list--bullet">
        @await Html.PartialAsync("_PredicateNode", Model.Condition)
    </ul>
</div>

<form method="post" action="/admin/rules/outcomes/@key/branches/@Model.Id/remove">
    @Html.AntiForgeryToken()
    <div class="govuk-button-group">
        <button class="govuk-button govuk-button--warning" data-module="govuk-button" type="submit">Remove branch</button>
        <a class="govuk-link" href="/admin/rules/outcomes/@key">Cancel</a>
    </div>
</form>
```

- [ ] **Step 3: Build to verify the views compile**

Run: `dotnet build C:\Repos\DfE\check-performance-data\src\DfE.CheckPerformanceData.Web\DfE.CheckPerformanceData.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/BranchEdit.cshtml \
        src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/RemoveBranch.cshtml
git commit -m "feat(admin-rules): branch editor and remove-confirm views"
```

---

### Task 14: Outcome.cshtml edit affordances + success banner

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcome.cshtml`

- [ ] **Step 1: Add the success banner after the back-link**

Insert immediately after the `<a ... class="govuk-back-link">` line:

```razor
@if (TempData["SuccessMessage"] is string success)
{
    <div class="govuk-notification-banner govuk-notification-banner--success" role="alert"
         aria-labelledby="govuk-notification-banner-title" data-module="govuk-notification-banner">
        <div class="govuk-notification-banner__header">
            <h2 class="govuk-notification-banner__title" id="govuk-notification-banner-title">Success</h2>
        </div>
        <div class="govuk-notification-banner__content">
            <p class="govuk-notification-banner__heading">@success</p>
        </div>
    </div>
}
```

- [ ] **Step 2: Add an "Add branch" link after the intro paragraph**

After the `<p class="govuk-body">Branches are evaluated top to bottom...</p>` line:

```razor
<a class="govuk-button" data-module="govuk-button" href="/admin/rules/outcomes/@Model.Key/branches/add">Add branch</a>
```

- [ ] **Step 3: Add per-branch edit/remove/move controls inside the summary card**

Replace the existing `<ul class="govuk-summary-card__actions">...</ul>` block with one that also exposes edit/move/remove for non-`otherwise` branches. The `otherwise` branch is detected by its condition text (the M2 describer renders it as `"Otherwise (always matches)"`):

```razor
            <ul class="govuk-summary-card__actions">
                <li class="govuk-summary-card__action">
                    <strong class="govuk-tag @TagClass(branch.Status)">@TagText(branch.Status)</strong>
                </li>
                @if (branch.Condition.Text != "Otherwise (always matches)")
                {
                    <li class="govuk-summary-card__action">
                        <a class="govuk-link" href="/admin/rules/outcomes/@Model.Key/branches/@branch.Id/edit">Edit</a>
                    </li>
                    <li class="govuk-summary-card__action">
                        <form method="post" action="/admin/rules/outcomes/@Model.Key/branches/@branch.Id/move" style="display:inline">
                            @Html.AntiForgeryToken()
                            <button class="govuk-link govuk-button--text" name="direction" value="up" type="submit"
                                    style="background:none;border:none;padding:0;color:#1d70b8;cursor:pointer;text-decoration:underline;">Up</button>
                        </form>
                    </li>
                    <li class="govuk-summary-card__action">
                        <form method="post" action="/admin/rules/outcomes/@Model.Key/branches/@branch.Id/move" style="display:inline">
                            @Html.AntiForgeryToken()
                            <button class="govuk-link govuk-button--text" name="direction" value="down" type="submit"
                                    style="background:none;border:none;padding:0;color:#1d70b8;cursor:pointer;text-decoration:underline;">Down</button>
                        </form>
                    </li>
                    <li class="govuk-summary-card__action">
                        <a class="govuk-link" href="/admin/rules/outcomes/@Model.Key/branches/@branch.Id/remove">Remove</a>
                    </li>
                }
            </ul>
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build C:\Repos\DfE\check-performance-data\src\DfE.CheckPerformanceData.Web\DfE.CheckPerformanceData.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Outcome.cshtml
git commit -m "feat(admin-rules): outcome page edit/add/move/remove affordances"
```

---

### Task 15: Lookups editing (view models + controller + row editor view)

**Files:**
- Create: `src/DfE.CheckPerformanceData.Web/Admin/Rules/LookupsEditViewModels.cs`
- Modify: `src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs`
- Create: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/LookupRowEdit.cshtml`
- Modify: `src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Lookups.cshtml`
- Modify: `tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs`

- [ ] **Step 1: Add failing controller tests**

```csharp
    private static Lookups SampleLookups() => new(new Dictionary<string, IReadOnlyList<string>>
    {
        ["GB"] = new[] { "English" }
    });

    private static IRulesConfigService SvcWithLookups(string etag = "L1")
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), etag));
        return svc;
    }

    [Fact]
    public async Task EditLookupRow_Loads_Existing_Languages()
    {
        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(SvcWithLookups()).EditLookupRow("GB", default)).Model);
        Assert.Equal("GB", vm.Form.Code);
        Assert.Contains("English", vm.Form.Languages);
        Assert.False(vm.Form.IsNew);
    }

    [Fact]
    public async Task AddLookupRow_Seeds_New_Form()
    {
        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(SvcWithLookups()).AddLookupRow(default)).Model);
        Assert.True(vm.Form.IsNew);
        Assert.Single(vm.Form.Languages); // one empty slot
    }

    [Fact]
    public async Task SaveLookupRow_Persists_Merged_Map()
    {
        var svc = SvcWithLookups("L1");
        Lookups? captured = null;
        svc.SaveLookupsAsync(Arg.Do<Lookups>(l => captured = l), "L1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(2));

        var form = new LookupRowEditForm
        {
            Code = "FR", IsNew = true, LoadETag = "L1", Action = "save",
            Languages = new List<string> { "French" }
        };

        var result = await NewController(svc).SaveLookupRow(form, default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(captured!.CountryLanguages.ContainsKey("FR"));
        Assert.True(captured.CountryLanguages.ContainsKey("GB")); // existing rows preserved
    }

    [Fact]
    public async Task SaveLookupRow_Invalid_Shows_Errors()
    {
        var svc = SvcWithLookups("L1");
        var form = new LookupRowEditForm { Code = "", IsNew = true, LoadETag = "L1", Action = "save",
            Languages = new List<string> { "" } };

        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveLookupRow(form, default)).Model);

        Assert.NotEmpty(vm.Errors);
        await svc.DidNotReceive().SaveLookupsAsync(Arg.Any<Lookups>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransformLookupRow_AddLanguage_ReRenders()
    {
        var form = new LookupRowEditForm { Code = "GB", LoadETag = "L1", Action = "addLanguage",
            Languages = new List<string> { "English" } };

        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(SvcWithLookups()).TransformLookupRow(form, default)).Model);

        Assert.Equal(2, vm.Form.Languages.Count);
    }

    [Fact]
    public async Task RemoveLookupRow_Persists_Without_Code()
    {
        var svc = SvcWithLookups("L1");
        Lookups? captured = null;
        svc.SaveLookupsAsync(Arg.Do<Lookups>(l => captured = l), "L1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(3));

        var result = await NewController(svc).RemoveLookupRow("GB", default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(captured!.CountryLanguages.ContainsKey("GB"));
    }
```

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — lookups edit types/actions do not exist.

- [ ] **Step 3: Create the lookups edit view models**

`LookupsEditViewModels.cs`:

```csharp
namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public sealed class LookupRowEditForm
{
    public string Code { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public string? LoadETag { get; set; }
    public List<string> Languages { get; set; } = new();
    public string? Action { get; set; } // save | addLanguage | removeLanguage:<index>
}

public sealed class LookupRowEditViewModel
{
    public required LookupRowEditForm Form { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public static LookupRowEditViewModel For(LookupRowEditForm form, IReadOnlyList<string>? errors = null) =>
        new() { Form = form, Errors = errors ?? Array.Empty<string>() };
}
```

- [ ] **Step 4: Add the lookups edit actions to `AdminRulesController`**

Add a view-path const:

```csharp
    private const string LookupRowEditView = "~/Views/Admin/Rules/LookupRowEdit.cshtml";
```

Add the actions:

```csharp
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
        var (current, etag) = await TryGetLookupsAsync(ct);
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

        var result = await rules.SaveLookupsAsync(merged, etag, ct);
        if (!result.Saved)
        {
            return View(LookupRowEditView, LookupRowEditViewModel.For(form, result.Errors));
        }

        TempData["SuccessMessage"] =
            $"Lookup '{form.Code}' saved (version {result.VersionNumber}). The rules service refreshes within about 5 minutes.";
        return RedirectToAction(nameof(Lookups));
    }

    [HttpPost("admin/rules/lookups/{code}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLookupRow(string code, CancellationToken ct)
    {
        var (current, etag) = await TryGetLookupsAsync(ct);
        if (current is null) return NotFound();

        var map = current.CountryLanguages.Where(kv => kv.Key != code)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var result = await rules.SaveLookupsAsync(new Lookups(map), etag, ct);

        TempData["SuccessMessage"] = result.Saved
            ? $"Lookup '{code}' removed (version {result.VersionNumber})."
            : "Could not remove the lookup: " + string.Join("; ", result.Errors);
        return RedirectToAction(nameof(Lookups));
    }
```

> Add `using DfE.CheckPerformanceData.Application.RulesConfig;` is already present (M2). `LookupsValidator` lives in that namespace.

- [ ] **Step 5: Run to verify pass**

Expected: PASS (6 new tests).

- [ ] **Step 6: Create `LookupRowEdit.cshtml`**

```razor
@using DfE.CheckPerformanceData.Web.Admin.Rules
@model LookupRowEditViewModel
@{
    ViewData["Title"] = Model.Form.IsNew ? "Add country" : $"Edit {Model.Form.Code}";
    var saveAction = Model.Form.IsNew ? "/admin/rules/lookups/save" : $"/admin/rules/lookups/{Model.Form.Code}/save";
}

<a href="/admin/rules/lookups" class="govuk-back-link">Back to country languages</a>

@if (Model.Errors.Count > 0)
{
    <div class="govuk-error-summary" data-module="govuk-error-summary">
        <div role="alert">
            <h2 class="govuk-error-summary__title">There is a problem</h2>
            <div class="govuk-error-summary__body">
                <ul class="govuk-list govuk-error-summary__list">
                    @foreach (var error in Model.Errors) { <li>@error</li> }
                </ul>
            </div>
        </div>
    </div>
}

<h1 class="govuk-heading-xl">@ViewData["Title"]</h1>

<form method="post" action="@saveAction">
    @Html.AntiForgeryToken()
    <input type="hidden" name="IsNew" value="@Model.Form.IsNew.ToString().ToLowerInvariant()" />
    <input type="hidden" name="LoadETag" value="@Model.Form.LoadETag" />

    <div class="govuk-form-group">
        <label class="govuk-label govuk-label--s" for="Code">Country code</label>
        @if (Model.Form.IsNew)
        {
            <input class="govuk-input govuk-input--width-5" id="Code" name="Code" value="@Model.Form.Code" />
        }
        else
        {
            <input type="hidden" name="Code" value="@Model.Form.Code" />
            <p class="govuk-body"><code>@Model.Form.Code</code></p>
        }
    </div>

    <fieldset class="govuk-fieldset">
        <legend class="govuk-fieldset__legend govuk-fieldset__legend--s">Official languages</legend>
        @for (var i = 0; i < Model.Form.Languages.Count; i++)
        {
            <div style="display:flex; gap:6px; margin-bottom:4px;">
                <input class="govuk-input govuk-input--width-20" name="Languages[@i]" value="@Model.Form.Languages[i]" />
                <button class="govuk-button govuk-button--warning govuk-!-margin-bottom-0" data-module="govuk-button"
                        name="Action" value="removeLanguage:@i" formaction="/admin/rules/lookups/row/transform" type="submit">Remove</button>
            </div>
        }
        <button class="govuk-button govuk-button--secondary" data-module="govuk-button"
                name="Action" value="addLanguage" formaction="/admin/rules/lookups/row/transform" type="submit">Add language</button>
    </fieldset>

    <div class="govuk-button-group govuk-!-margin-top-4">
        <button class="govuk-button" data-module="govuk-button" name="Action" value="save" type="submit">Save</button>
        <a class="govuk-link" href="/admin/rules/lookups">Cancel</a>
    </div>
</form>
```

- [ ] **Step 7: Add edit/remove/add affordances + success banner to `Lookups.cshtml`**

After the `<a ... class="govuk-back-link">` line, add the success banner (same block as Task 14 Step 1). After the intro `<p>`, add:

```razor
<a class="govuk-button" data-module="govuk-button" href="/admin/rules/lookups/add">Add country</a>
```

Add an "Actions" column. Change the `<thead>` row to include a third header, and each body row to include edit + remove:

```razor
            <tr class="govuk-table__row">
                <th scope="col" class="govuk-table__header">Country code</th>
                <th scope="col" class="govuk-table__header">Official languages</th>
                <th scope="col" class="govuk-table__header">Actions</th>
            </tr>
```

```razor
                <tr class="govuk-table__row">
                    <td class="govuk-table__cell"><code>@row.CountryCode</code></td>
                    <td class="govuk-table__cell">@row.Languages</td>
                    <td class="govuk-table__cell">
                        <a class="govuk-link" href="/admin/rules/lookups/@row.CountryCode/edit">Edit</a>
                        <form method="post" action="/admin/rules/lookups/@row.CountryCode/remove" style="display:inline; margin-left:10px;">
                            @Html.AntiForgeryToken()
                            <button class="govuk-link govuk-button--text" type="submit"
                                    style="background:none;border:none;padding:0;color:#1d70b8;cursor:pointer;text-decoration:underline;">Remove</button>
                        </form>
                    </td>
                </tr>
```

- [ ] **Step 8: Build + full unit run**

Run: `dotnet test C:\Repos\DfE\check-performance-data\tests\DfE.CheckPerformanceData.UnitTests\DfE.CheckPerformanceData.Application.UnitTests.csproj`
Expected: PASS (all unit tests, including every M3 test added so far).

- [ ] **Step 9: Commit**

```bash
git add src/DfE.CheckPerformanceData.Web/Admin/Rules/LookupsEditViewModels.cs \
        src/DfE.CheckPerformanceData.Web/Controllers/AdminRulesController.cs \
        src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/LookupRowEdit.cshtml \
        src/DfE.CheckPerformanceData.Web/Views/Admin/Rules/Lookups.cshtml \
        tests/DfE.CheckPerformanceData.UnitTests/Web/Controllers/AdminRulesControllerEditTests.cs
git commit -m "feat(admin-rules): country-languages lookups editing"
```

---

### Task 16: E2E — auth on new POSTs + edit/save round-trips

**Files:**
- Create: `tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminRulesEditTests.cs`

> **Operational (from M2):** E2E runs against the live `cypd_web` Docker container. Before running these, rebuild it: `docker compose --profile all up -d --build web`, then poll `http://localhost:8080/health` until 200. The running container is otherwise stale and will 404 the new routes.

- [ ] **Step 1: Write the E2E tests**

```csharp
using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminRulesEditTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task EditBranch_AsNonAdmin_Denied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/outcomes/Inclusion/branches/INC-1/edit");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task AddBranch_AsAdmin_Returns_Editor()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            // Discover a real outcome key from the outcomes page first.
            using var listReq = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes");
            var listBody = await (await TestHttpClients.SendAsync(listReq)).Content.ReadAsStringAsync();
            Assert.Contains("/admin/rules/outcomes/", listBody);

            // The first outcome link target works for add-branch.
            var start = listBody.IndexOf("/admin/rules/outcomes/", StringComparison.Ordinal);
            var href = listBody.Substring(start, listBody.IndexOf('"', start) - start);
            var key = href.Replace("/admin/rules/outcomes/", "").Trim('/');

            using var addReq = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/outcomes/{key}/branches/add");
            var addResp = await TestHttpClients.SendAsync(addReq);

            Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
            var body = await addResp.Content.ReadAsStringAsync();
            Assert.Contains("Add branch", body);
            Assert.Contains("Save branch", body);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task Lookups_Edit_Page_AsAdmin_Returns_200()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/lookups/add");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Official languages", await response.Content.ReadAsStringAsync());
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }
}
```

- [ ] **Step 2: Rebuild the web container, then run the E2E suite**

```bash
docker compose --profile all up -d --build web
```

Poll until healthy (PowerShell):

```powershell
do { try { $h = (Invoke-WebRequest http://localhost:8080/health -UseBasicParsing).StatusCode } catch { $h = 0 }; Start-Sleep 2 } until ($h -eq 200)
```

Run: `dotnet test C:\Repos\DfE\check-performance-data\tests\DfE.CheckPerformanceData.E2ETests\... --filter "FullyQualifiedName~AdminRulesEditTests|FullyQualifiedName~AdminRulesAuthTests"`
Expected: PASS (new + existing rules auth tests).

- [ ] **Step 3: Commit**

```bash
git add tests/DfE.CheckPerformanceData.E2ETests/Admin/AdminRulesEditTests.cs
git commit -m "test(admin-rules): E2E auth + editor surface for M3"
```

---

### Task 17: Final verification + manual smoke test

**Files:** none (verification only).

- [ ] **Step 1: Full unit + integration run**

Run: `dotnet test C:\Repos\DfE\check-performance-data\tests\DfE.CheckPerformanceData.UnitTests\DfE.CheckPerformanceData.Application.UnitTests.csproj`
Expected: PASS, no regressions vs the M2 baseline (M2 left UnitTests at 1379; M3 adds the new tests on top).

- [ ] **Step 2: Manual smoke test (stack running, signed in as Admin via dev impersonation at `localhost:8080`)**

Walk and tick each:
- [ ] `/admin/rules/outcomes/{key}` shows Edit / Up / Down / Remove on every non-`otherwise` branch and an "Add branch" button; the `otherwise` branch shows none of these.
- [ ] Edit a branch: add a condition, change its field (postback re-renders operators + value editor for the new type), change the combinator to ANY, tick two leaves and "Group selected as ALL" (a nested card appears), then Save → redirect to the outcome page with a green success banner; the change is reflected and a new entry appears under `/admin/rules/history/Rules`.
- [ ] Try to Save a branch whose group has no conditions → error summary "A group must contain at least one condition.", nothing saved.
- [ ] Add a branch, fill one condition, Save → it is inserted directly above `otherwise`.
- [ ] Reorder two branches with Up/Down → order changes and persists; `otherwise` stays last and cannot move past.
- [ ] Remove a branch → confirmation page → Remove → gone, success banner.
- [ ] Lookups: Add country (code + languages), Edit (add/remove a language via postback then Save), Remove → each persists and shows a success banner; invalid input (empty code/blank language) shows an error summary and saves nothing.
- [ ] Concurrency: open a branch editor in two tabs; save in tab 1; save in tab 2 → tab 2 shows the "changed by someone else" block and does not clobber.

- [ ] **Step 3: Update project memory**

Update `C:\Users\ajsde\.claude\projects\C--Repos-DfE\memory\project_rules_editor.md`: mark M3 (editing) BUILT, record the commit range, note the two documented refinements (Operator token; Field/Value reuse for OfficialLanguageIs), and set the RESUME-HERE to M4 (add/remove outcomes + deletion guard + rollback UI).

- [ ] **Step 4: Final commit (if memory or docs tracked) / report**

Report the change summary (per CLAUDE.md): files added/modified per layer, test counts, and the manual smoke-test result.

---

## Self-review

**Spec coverage:**
- Branch editor (status + predicate tree) → Tasks 7–10, 12–13. ✅
- Predicate widget: ALL/ANY/NOT + leaves, nested cards, select-then-group, server postbacks, no JS → Tasks 3, 9, 12, 13. ✅
- Leaf editor: operators constrained by field type, typed value editors, field-change-as-postback → Tasks 4, 9 (`setField`), 12. ✅
- Form-binding model: flat `PredicateNodeForm` list, `Flatten`/`RebuildPredicate`, transforms as list ops → Tasks 1, 3. ✅ (Refinements documented at top.)
- Branch-list ops: add / remove (confirm) / reorder, `otherwise` pinned & protected → Tasks 6, 8, 11, 14. ✅
- Lookups editor: add / edit (repeatable languages via postback) / remove, `LookupsValidator`, `SaveLookupsAsync` → Task 15. ✅
- Save → validate → persist via M1, error summary keeps edits, success banner, ~5-min note → Tasks 10, 14, 15. ✅
- Concurrency (capture load ETag, re-read, block-not-clobber) → Task 10. ✅
- Empty-composite open item → resolved by `PredicateFormValidator` (Task 5) + add-branch never persists half-built (Task 8). ✅
- No Domain/Application/Persistence/Infrastructure changes → all new code under `Web/`. ✅
- Testing: round-trip, each transform, controller POSTs (re-render/save/invalid/concurrency/otherwise/lookups), E2E auth + flows → Tasks 1–11, 15, 16. ✅
- Rebuild container before E2E → Task 16 Step 2. ✅

**Placeholder scan:** No "TBD"/"add validation here"/"similar to". Each code step carries complete code. The one cross-task dependency (`SetField` → `LeafEditorOptions`) is called out with an ordering instruction (do Task 4 before Task 3 Step 3).

**Type consistency:** `IRulesConfigService` signatures (`GetRulesAsync`/`SaveRulesAsync`/`GetLookupsAsync`/`SaveLookupsAsync` returning `RulesConfigSaveResult` with `.Success(int)`/`.Invalid(IReadOnlyList<string>)`), `RuleSet`/`OutcomeRules`/`RuleBranch`/`Predicate`/`FieldValue`/`CompareOp`/`DecisionStatus`/`FieldType`/`FieldCatalogue`/`Lookups`/`LookupsValidator` all match the verified source. Test conventions (namespace `DfE.CheckPerformanceData.Application.UnitTests.*`, xUnit global using, NSubstitute, `RulesConfigNotFoundException(string)`) match the M2 test files. Controller primary-ctor field is `rules` (matches M2). `PredicateDescriber.Describe` returns `PredicateNode`; `BranchViewModel(string Id, DecisionStatus Status, PredicateNode Condition)` reused for the remove-confirm view (matches M2 `RulesAdminViewModels.cs`).
