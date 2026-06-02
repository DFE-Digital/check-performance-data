# Admin rules editor — Milestone 3 (editing) design spec

**Date:** 2026-06-01
**Status:** Approved for planning
**Author:** brainstormed with Claude (Opus 4.8)
**Builds on:** `2026-05-29-admin-rules-editor-design.md` (overall feature), M1 (foundation) and M2
(read-only surface), both shipped on branch `rules-admin`.

## Summary

M3 makes the `/admin/rules` surface **editable**: an admin can change a decision branch's status
and its `When` predicate through a structured GOV.UK form (the nested-group widget with
select-then-group regrouping), manage the branch list within an outcome (add / remove / reorder),
and edit the country-languages lookups. Every save runs through the **existing M1
`IRulesConfigService`** (validate → ETag concurrency → blob write → version snapshot → audit) —
M3 adds **no new Application/Persistence/Domain code**; it is pure Web layer on top of M1, layered
onto the M2 read-only views.

## Scope

**In scope**
- **Branch editor**: edit a `RuleBranch`'s `Status` and its `When` predicate tree.
- **Predicate widget**: nested-group editor (ALL / ANY / NOT composites + leaf conditions), with
  **select-then-group** regrouping, all driven by **server postbacks** (no JavaScript required).
- **Branch-list operations** within an outcome: add branch, remove branch, reorder ↑↓
  (order matters — first match wins). The terminal `otherwise` branch is pinned last and is not
  editable / removable / movable.
- **Lookups editor**: add / edit / remove rows in the `country-languages.json` map.

**Out of scope (→ M4)**
- Add / remove whole **outcomes**, the outcome **deletion hard-block guard**, and the **rollback**
  UI. (M3 only edits within the existing outcome set.)

**Out of scope (whole feature, per the parent spec)**
- Adding new **fields** to the rule vocabulary (fields live in `FieldCatalogue` / `AnswerFieldMap`
  in code — a reviewed code change). The UI only references existing catalogue fields via dropdowns.
- Drag-and-drop. Restructuring is select-then-group.
- A separate draft/publish workflow (saves are versioned-direct, per M1).

## Settled decisions (this brainstorm, 2026-06-01)

1. **M3 = branch+predicate editing + branch-list ops + lookups editor.** Outcomes add/remove,
   deletion guard, and rollback are M4.
2. **Interaction model = server-postback baseline only.** Every mutation (add / remove / group /
   ungroup / change combinator / change a leaf's field) is a full-page form POST that re-renders the
   tree from the posted model. Works with zero JavaScript — the mandatory, accessible, testable
   foundation. JS enhancement (instant in-place updates) is explicitly a **later, separate increment**,
   not part of M3.
3. **Form binding = flat list of nodes with parent ids** (see "Form-binding model").
4. **Predicate widget rendering = nested cards** (style "A"): each ALL/ANY/NOT group is a bordered
   GOV.UK-style card; nesting renders as cards within cards.
5. **Edit/save model = whole-branch single form.** The entire predicate tree renders as one form with
   every leaf as live controls; structural buttons and field-changes postback and re-render; a single
   "Save branch" validates the whole branch and persists.
6. **M3 adds no persistence/service/domain code** — it reuses M1's `IRulesConfigService`
   (`SaveRulesAsync`/`SaveLookupsAsync`/`GetRulesAsync`/`GetLookupsAsync`) and validators.

## Information architecture

The M2 read-only pages grow editing affordances; new mutating routes are added to the **existing**
`AdminRulesController` (already `[Authorize(Roles = WikiConstants.AdminRole)]`, GET-only until now).
All mutating actions are `[HttpPost]` + `[ValidateAntiForgeryToken]`.

```
/admin/rules/outcomes/{key}                         (M2 GET) gains, as POSTs:
    …/branches/add            → seed a new editable branch, redirect to its editor
    …/branches/{id}/remove    → confirm interstitial → remove (otherwise blocked)
    …/branches/{id}/move      → reorder ↑/↓ (otherwise stays last)

/admin/rules/outcomes/{key}/branches/{id}/edit      GET branch editor (whole-branch form)
    [POST] action = add | remove | group | ungroup | setCombinator | setField | addValue | removeValue
                                                    → apply transform to posted flat list, re-render (NO blob write)
    [POST] action = save                            → rebuild → validate → persist (or error summary)

/admin/rules/lookups                                (M2 GET) gains, as POSTs:
    …/add                     → add a country row (code + ≥1 language)
    …/{code}/save             → replace a row's language list
    …/{code}/remove           → remove a row
```

The route shape exactly continues the M2 controller; view paths stay explicit
(`~/Views/Admin/Rules/…`) so the admin layout cascades.

## Form-binding model (server round-trip of a recursive tree)

The predicate tree is posted as a **flat indexed list** that the default model binder handles
reliably, then folded back into the `Predicate` object graph server-side.

```csharp
enum PredicateKind { AllOf, AnyOf, Not, FieldEq, FieldNeq, FieldIn, FieldCompare,
                     IsKnownAndCertain, OfficialLanguageIs, Otherwise }

sealed class PredicateNodeForm
{
    public int Id { get; set; }            // stable form-local id (counter), unique within the form
    public int? ParentId { get; set; }     // null for the root node
    public PredicateKind Kind { get; set; }

    // leaf fields (only the ones relevant to Kind are populated)
    public string? Field { get; set; }
    public string? Op { get; set; }        // CompareOp name, for FieldCompare
    public string? Value { get; set; }     // scalar literal, for Eq/Neq/Compare
    public List<string> Values { get; set; } = new();  // for FieldIn
    public string? CountryField { get; set; }          // for OfficialLanguageIs
    public string? Language { get; set; }              // for OfficialLanguageIs
}
```

- **Order among siblings** = order in the posted list.
- Two pure, unit-tested Web-layer functions:
  - `Flatten(Predicate) → List<PredicateNodeForm>` — for the first GET render and after each transform.
  - `RebuildPredicate(IReadOnlyList<PredicateNodeForm>) → Predicate` — links parent→children, maps
    each `Kind` + its fields back to the matching `Predicate`/`FieldValue` case (typed via
    `FieldCatalogue` so a Number field's value parses to a number, etc.).
- **Transforms are list operations** on the flat list (all server-side, all re-render only):
  - *add condition* → append a leaf node with the target group's `Id` as `ParentId`.
  - *add group* → append an empty `AllOf` (default) node under the target group.
  - *remove* → drop the node and all descendants (transitively by `ParentId`).
  - *group selected* → insert a new `AllOf`/`AnyOf` node under the common parent and set the ticked
    nodes' `ParentId` to it (preserving their order).
  - *ungroup* → reparent a composite's children to the composite's parent (in place), delete the composite.
  - *setCombinator* → change a composite node's `Kind` (AllOf↔AnyOf↔Not; Not constrained to one child).
  - *setField* → on a leaf, when the field changes type, reset `Op`/`Value` to that type's default.
  - *addValue / removeValue* → mutate a `FieldIn` leaf's `Values` list.

## Branch editor + predicate widget

- **Whole-branch single form.** The branch's `Status` (dropdown) and its predicate tree (flat node
  list) are one `<form>`. Every leaf is live controls; every composite is a **nested card** (style A)
  with its combinator label (changeable), a per-row remove ✕, per-row select checkboxes, and
  per-group **+ Add condition / + Add group**.
- A bottom **select-then-group bar**: when ≥1 row is ticked, "Group selected as ALL / ANY" and
  "Ungroup" act on the selection via one postback.
- Each mutating control is a **named submit button** (`name="action" value="add:42"` etc., encoding
  the action + target node id). The action posts the whole form, the controller applies the transform
  to the rebuilt flat list, and re-renders — **no blob write**.
- A **recursive Razor editor partial** renders a node: composite → card + recurse on children; leaf →
  the typed control row. (Mirrors M2's display `_PredicateNode.cshtml`, but in edit mode.)

### Leaf editor (operator + value constrained by field type)

Field dropdown (from `FieldCatalogue`) → operator list constrained by the field's `FieldType` →
typed value editor:

| Field type | Operators offered | Value editor |
|------------|-------------------|--------------|
| String | equals, not equals, is one of, is known & certain | text input (or repeatable list for "is one of") |
| Number | less than, ≤, greater than, ≥, equals, is known & certain | number input |
| Date   | before, on or before, after, on or after, equals | date input (ISO `yyyy-MM-dd`) |
| Bool   | equals, not equals, is known & certain | true/false select |

Plus `official language is` for a **country field** → a country-field dropdown + a language text input.
`is known & certain` takes no value. Because there is no JavaScript, **changing the field dropdown is a
postback** that re-renders the row with the operators and value editor matching the new field's type
(`setField` transform above).

## Branch-list operations (outcome page)

- **Add branch**: generate a unique branch `Id` (validated unique within the outcome by
  `RuleSetValidator`), default `Status = Scrutiny`, seed a minimal editable predicate (a single empty
  `AllOf` group — never `Otherwise`, which is reserved for the pinned terminal), then redirect to the
  branch editor.
- **Remove branch**: confirmation interstitial, then remove. The `otherwise` branch cannot be removed.
- **Reorder ↑↓**: postback that swaps adjacent branches. Order is significant (first matching branch
  wins). `otherwise` is always rendered/persisted last and cannot be moved.

## Lookups editor

`/admin/rules/lookups` (M2 table) gains row editing:
- **Add**: country code + one or more languages.
- **Edit**: replace a row's language list (repeatable inputs; add/remove language entries via postback).
- **Remove**: drop the row.
- Validated by the M1 `LookupsValidator` (non-empty code, ≥1 language, no blank language, no duplicate
  codes) before persisting. Saved via `SaveLookupsAsync` (`ConfigType = Lookups`) — same versioned,
  audited pipeline.

## Save → validate → persist (reuse M1)

On a **save** action (rules branch or lookups):

1. `RebuildPredicate`/rebuild the edited object; splice the edited branch into the current `RuleSet`
   (for lookups, rebuild the whole map).
2. Call `IRulesConfigService.SaveRulesAsync(ruleSet, expectedETag)` /
   `SaveLookupsAsync(lookups, expectedETag)`. M1 already does: serialise with `RulesJson.Options` →
   `RuleSetValidator` / `LookupsValidator` → ETag-conditional blob write → append
   `RulesConfigVersion` snapshot → write `AuditEntry`.
3. Map the `RulesConfigSaveResult`:
   - `Invalid(errors)` → render a **GOV.UK error summary** from `errors`, **keep the user's edits on
     the form**, persist nothing.
   - `Success(versionNumber)` → redirect to the relevant view with a GOV.UK **success notification**.

### Concurrency (no clobber)

- The branch editor captures the rules blob's **ETag at page GET** into a hidden form field and carries
  it through every structural postback (the intermediate postbacks never write, so the token is stable).
- On **save**, the controller re-reads the current `RuleSet` + current ETag:
  - if `currentETag != loadETag` → **block**: "These rules were changed by someone else since you opened
    this page — reload to see the latest." Persist nothing. (Belt-and-braces, the store's `If-Match` on
    `loadETag` would also reject the write.)
  - else splice the edited branch into the freshly-read `RuleSet` and call `SaveRulesAsync(spliced,
    loadETag)`. Splicing into the current document means concurrent edits to *other* branches are
    preserved; a genuine same-document conflict is blocked rather than clobbered.

## Architecture & components (Web layer only)

**No changes to Domain / Application / Persistence / Infrastructure.** M1 already provides the service,
validators, store, version entity, and DI. M3 adds, under `src/DfE.CheckPerformanceData.Web/`:

- `Admin/Rules/PredicateNodeForm.cs` + `PredicateKind` — the flat bindable node.
- `Admin/Rules/PredicateForm.cs` (or similar) — `Flatten` + `RebuildPredicate` + the transform
  functions (pure, unit-tested).
- `Admin/Rules/` edit view models — branch editor VM (status options, field catalogue for dropdowns,
  the flat node list, the carried ETag), lookups-edit VMs.
- `Controllers/AdminRulesController.cs` — new `[HttpPost] [ValidateAntiForgeryToken]` actions
  (branch add/remove/move, branch-edit transform + save, lookups add/save/remove), plus the branch-edit
  GET. Reuses the M2 helpers (`TryGetRulesAsync`, etc.).
- `Views/Admin/Rules/` — branch editor view + a **recursive editor partial**; edit controls added to the
  outcome and lookups views (the M2 read views gain "Edit" links/buttons).

This keeps the clean-architecture boundary intact: editing is presentation + the M1 application service.

## Testing

- **Unit (xUnit, NSubstitute where needed):**
  - `Flatten`/`RebuildPredicate` **round-trip** for representative trees (incl. 3-level nesting) and
    every leaf kind / `FieldValue` type.
  - Each **transform** (add condition, add group, remove + descendants, group selected, ungroup,
    setCombinator, setField resets value, addValue/removeValue).
  - **Controller POSTs**: structural action re-renders without persisting; **save** success persists via
    the service; **save** with validation failure → error summary + nothing persisted (assert
    `SaveRulesAsync` not called or returns Invalid → no redirect); **concurrency conflict** → blocked, not
    persisted; **`otherwise` protection** (cannot remove/move/edit); **lookups** add/edit/remove + invalid
    lookups → error summary.
- **E2E (Playwright, against the rebuilt `cypd_web` container):** auth still enforced on the new POSTs
  (non-admin denied); build a nested group via select-then-group → save → confirm it appears in version
  history; edit a lookups row and confirm the change.

## Risks & notes

- **Operational:** E2E runs against the live `cypd_web` Docker container — after coding, rebuild it
  (`docker compose --profile all up -d --build web`, wait for `/health` 200) so tests hit the new code.
  The running container is otherwise stale (a lesson from M2).
- **Prod blob-write permissions** remain an open infra item (parent spec / `Program.cs:126` TODO): the
  managed identity needs blob-write on `rules-config`. Works locally on Azurite.
- **Worker latency** (~5 min) is already surfaced in the M2 landing inset copy; the save success banner
  should reinforce it.
- **No JS in M3** is deliberate: every interaction must work via postback. JS enhancement is a future,
  separate increment and must not become a hidden dependency.
- Do **not** touch `SEND Framework/` (legacy) or the stray `Copy-Rules_Engine/` directory.

## Key existing files to build against

- M1 service & validators: `Application/RulesConfig/IRulesConfigService.cs`,
  `RulesConfigSaveResult.cs`, `Application/RulesEngine/RuleSetValidator.cs`,
  `RulesConfig/LookupsValidator.cs`, `Application/RulesEngine/Json/RulesJson.cs`.
- Rule model: `Application/RulesEngine/RuleSet.cs`, `Predicate.cs`, `FieldValue.cs`,
  `FieldCatalogue.cs`, `CompareOp.cs`, `DecisionStatus.cs`, `Lookups.cs`.
- M2 Web surface to extend: `Web/Controllers/AdminRulesController.cs`,
  `Web/Admin/Rules/PredicateDescriber.cs` + `PredicateNode.cs` (display analog of the editor partial),
  `Web/Admin/Rules/RulesAdminViewModelFactory.cs`, `Web/Views/Admin/Rules/*` (incl. display
  `_PredicateNode.cshtml`).
- Antiforgery for any future fetch use: header `X-XSRF-TOKEN` (configured in `Program.cs`); M3 uses plain
  form posts with `[ValidateAntiForgeryToken]`.
```
