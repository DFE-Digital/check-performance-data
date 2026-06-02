# Admin rules editor — design spec

**Date:** 2026-05-29
**Status:** Approved for planning
**Author:** brainstormed with Claude (Opus 4.8)

## Summary

A new admin-only section at `/admin/rules` that lets an authenticated administrator
add, remove and update the rules engine configuration — both the decision rules
(`rules.json`) and the country-languages lookup (`country-languages.json`) — through a
structured GOV.UK UI, with no raw-JSON editing. Every save is validated before it lands,
versioned for rollback, and audited. The rules engine worker picks up a saved change from
blob storage within its normal ~5-minute refresh, with no restart.

## Goals

- Let admins manage decision rules and the country-languages lookup without a developer
  or a redeploy.
- Make every saved ruleset **structurally valid before it goes live** — invalid edits are
  rejected, never written.
- Keep a **full version history with one-click rollback**, because these rules auto-decide
  real pupil cases.
- Stay within DfE/GOV.UK standards: server-rendered Razor, progressive enhancement, no SPA,
  WCAG 2.2 AA, GOV.UK Design System components.

## Non-goals (v1)

- Adding brand-new **fields** to the rule vocabulary. Fields live in `FieldCatalogue` (code)
  and are wired to message answers via `AnswerFieldMap`; adding one stays a reviewed code
  change. The UI only ever references existing catalogue fields, via dropdowns.
- Drag-and-drop predicate editing. Restructuring is done with "select-then-group" instead.
- Editing the worker's evaluation logic, the queue, or Zendesk integration.

## Decisions (settled during brainstorming)

1. **Structured UI, not raw text** — forms over the rule model, field/operator/status as
   dropdowns.
2. **Both files in scope** — `rules.json` and `country-languages.json`.
3. **Admin only** (`cypmd_admin`) — on the controller; nav visibility is already satisfied
   because the whole `/admin` landing is admin-gated.
4. **Editor shape: drill-down GOV.UK pages (approach "B") built on a nested-group predicate
   widget (approach "A").** B's page hierarchy mirrors the data model 1:1; the widget on the
   branch page handles arbitrary predicate nesting.
5. **Regrouping: select-then-group** — tick sibling rows, "Group selected as ALL/ANY", one
   server postback rewrites the subtree. No drag-drop; works without JS.
6. **Save model: versioned direct save** — validate → write blob atomically → snapshot to a
   history table → audit. No separate draft/publish workflow.
7. **Full add/remove of outcomes is in scope for v1** (not just editing branches within
   existing outcomes).

## Background: how the rules config works today

- Rules live in the Azure Storage **blob container `rules-config`**: blob **`rules.json`**
  (the `RuleSet`) and blob **`country-languages.json`** (the `Lookups` map). Locally this is
  Azurite, seeded from `src/DfE.CheckPerformanceData.RulesEngineWorker/seed/`.
- **`BlobRulesProvider`** (Infrastructure) loads and **polls the blobs every ~5 min**
  (ETag-cached), validates via `RuleSetValidator`, and atomically swaps the in-memory
  snapshot. On invalid JSON it keeps the last-known-good copy. So a newly written, valid
  `rules.json` is picked up within ~5 min, no restart.
- JSON ↔ object mapping uses the shared options in
  `Application/RulesEngine/Json/RulesJson.cs` (`PredicateJsonConverter`,
  `FieldValueJsonConverter`, camelCase, enum-as-string).

### Data model (what the UI edits)

```
RuleSet(Version, UpdatedAt, Outcomes[])
  OutcomeRules(Key, Label, Rules[])
    RuleBranch(Id, Status: DecisionStatus, When: Predicate)

Predicate =
  AllOf(items[]) | AnyOf(items[]) | Not(inner)            // composites
  | FieldEq(field, value) | FieldNeq(field, value)
  | FieldIn(field, values[]) | FieldCompare(field, op, value)
  | IsKnownAndCertain(field) | OfficialLanguageIs(countryField, language)
  | Otherwise                                              // terminal, must be last branch

DecisionStatus = AutoApproved | AutoRejected | Scrutiny
CompareOp      = Lt | Lte | Gt | Gte
FieldType      = String | Number | Date | Bool   (from FieldCatalogue, ~30 fixed fields)

Lookups(CountryLanguages: Dictionary<countryCode, languages[]>)
```

The real data nests up to three levels (`ALL[ keyStage, ANY[ ALL[bool,bool], flag ] ]`).

## Architecture

Clean-architecture-respecting; reuse what exists.

### New / changed components

**Web**
- `Controllers/AdminRulesController.cs` — `[Authorize(Roles = WikiConstants.AdminRole)]`,
  routes under `/admin/rules` (landing, outcomes, branch editor, lookups, history). GET
  renders; every mutating action is a `[ValidateAntiForgeryToken]` POST that goes through the
  save pipeline. Explicit `~/Views/Admin/Rules/...` view paths so the admin layout cascades
  (same convention as `AdminSettingsController`).
- `Views/Admin/Rules/*.cshtml` — landing, outcome list, branch list, branch editor, lookups,
  history. GOV.UK Design System components throughout.
- A **recursive Razor partial** for the predicate tree (`_PredicateNode.cshtml` or an
  editor/display template) that renders composites and leaves and recurses on children.
- `Admin/Nav/RulesConfigNavEntry.cs` — new tile (Title "Rules configuration",
  `Url = /admin/rules`, `ParentKey = AdminNavKeys.SystemAdmin`, `Enabled = true`), plus a key
  in `AdminNavKeys`. Registered in `Extensions/AdminNavServiceCollectionExtensions.cs`. The
  existing disabled `RulesEngineNavEntry` (queue observability) is left untouched.
- View models for each page and for the form round-trip (a flat, bindable representation of
  the predicate tree — see "Form binding" below).

**Application**
- `IRulesConfigStore` — abstraction for reading/writing the two config documents with ETag
  concurrency: `Task<RulesConfigDocument<T>> ReadAsync(...)` returning content + ETag, and
  `Task WriteAsync(..., expectedETag)` that fails on mismatch. Keeps the controller thin and
  testable.
- A `RulesConfigService` (or use-case handlers) that orchestrates: rebuild object → validate
  → concurrency-check → write blob → snapshot version → audit. Returns a result the controller
  maps to a success banner or a GOV.UK error summary.
- Extend **`RuleSetValidator`** with a **duplicate-outcome-key** check (it currently checks
  duplicate branch ids within an outcome and empty keys, but not duplicate outcome keys).

**Infrastructure**
- `BlobRulesConfigStore : IRulesConfigStore` — implemented over the already-registered
  `BlobServiceClient` against container `rules-config`. Reuses `RulesJson.Options`.
- Registered via the existing Infrastructure service-registration extension.

**Persistence**
- New entity **`RulesConfigVersion`** (mirrors `ContentBlockVersion`):
  `Id, ConfigType (Rules|Lookups), VersionNumber, Content (json text), CreatedAt, CreatedBy`.
  Unique index on `(ConfigType, VersionNumber)`. Append-only.
- `IEntityTypeConfiguration<RulesConfigVersion>`, `DbSet` on `PortalDbContext`/
  `IPortalDbContext`, and a new EF Core migration.
- Audit uses the existing `AuditEntry` mechanism. PortalDbContext auto-audits EF entity
  changes, but the rules/lookups documents are blobs, not EF entities, so the save pipeline
  writes an explicit `AuditEntry` row for each blob write (who/when/action/which config). The
  `RulesConfigVersion` insert is itself an EF change and is auto-audited.

### Information architecture

```
/admin/rules                                  Landing. Two cards: "Decision rules" and
                                              "Country languages". Shows current rules
                                              Version, UpdatedAt, and provider health.
/admin/rules/outcomes                         Outcome list (Key, Label, # branches).
                                              "+ Add outcome"; remove per row (guarded).
/admin/rules/outcomes/{key}                   Branch list for one outcome: ordered table
                                              (order matters — first match wins), Status,
                                              reorder ↑↓, add/remove. "otherwise" pinned last.
/admin/rules/outcomes/{key}/branches/{id}     Branch editor: Id, Status dropdown, and the
                                              nested-group predicate widget.
/admin/rules/lookups                          Country → languages editor (add/edit/remove).
/admin/rules/history                          Version history for both configs; view + roll back.
```

### The predicate widget

- **Composite node** (ALL / ANY / NOT) renders as a GOV.UK card containing rows; each row is a
  leaf or a nested composite. Controls: **+ Add condition**, **+ Add group**, remove ×, and a
  combinator selector for ALL/ANY.
- **Leaf row**: `field` dropdown (from `FieldCatalogue`) → `operator` constrained by the field's
  type (Date/Number expose `lt/lte/gt/gte`; all types expose `equals`/`not equals`/`in`; plus
  `is known & certain` and `official language is`) → a **typed value editor**: date input for
  Date, true/false radios for Bool, number input for Number, text for String, and a
  repeatable value list for `in`. `OfficialLanguageIs` shows a country-field dropdown + a
  language input.
- **Regrouping (select-then-group)**: checkboxes on sibling rows + a "Group selected as
  ALL/ANY" button; a single server postback rewrites that subtree. There is also "ungroup".
- **Progressive enhancement**: add/remove/group all work as server postbacks (the page
  round-trips and re-renders the tree from the posted model); JS makes them instant without a
  full reload. No SPA.

### Form binding (server round-trip of a recursive tree)

The predicate tree is posted as a **flat list of nodes** with parent references (each node has
an index/path, a kind, and kind-specific fields), which model-binds reliably and is rebuilt
into the `Predicate` object graph server-side. This avoids deep nested model-binding and makes
add/remove/group operations simple list transforms. The rebuilt graph is then serialised via
`RulesJson.Options` and validated.

## Save → validate → persist pipeline

On every mutating POST (rules or lookups):

1. Rebuild the `RuleSet` (or `Lookups`) from the posted flat model.
2. Serialise with `RulesJson.Options`.
3. **Validate**: `RuleSetValidator.Validate` for rules (now incl. duplicate-key check); a
   lighter validator for lookups (non-empty country code, ≥1 language, no duplicate codes). On
   failure → render a **GOV.UK error summary** from `Errors[]`, persist nothing, keep the
   user's edits on the form.
4. **Optimistic concurrency**: compare the blob's current ETag with the one captured when the
   edit session loaded. If changed → block with "these rules were changed by someone else —
   reload" (no clobber).
5. For rules: auto-bump `RuleSet.Version` and set `UpdatedAt` to now.
6. **Write the blob** (`IRulesConfigStore.WriteAsync` with the expected ETag).
7. **Append a `RulesConfigVersion`** snapshot (full document, next `VersionNumber`,
   `CreatedBy` from `ICurrentUserService`).
8. **Write an `AuditEntry`** (who/when/action/which config).
9. Redirect back to the view with a **GOV.UK success notification**. Worker refresh applies it
   within ~5 min.

Save returns to the relevant view (consistent with the existing save-UX convention — return to
view mode, minimal banners).

## Adding / removing outcomes

- **Add outcome**: collect `Key` (unique, validated) + `Label`. Seed the new outcome with a
  single `otherwise → Scrutiny` branch so it is structurally valid from creation and defaults
  to human review — never an accidental auto-approve/reject.
- **Unique keys**: enforced in the UI and by the new `RuleSetValidator` duplicate-key check.

### Deletion safeguards (outcomes)

Deleting an outcome is the highest-risk action: the engine resolves a request's outcome by
key (`RulesEngine.Evaluate` → `rules.Outcomes.FirstOrDefault(key)`), so a removed outcome makes
every future request in that category return `Decision.UnmatchedOutcome` — it silently loses its
automated rules. Two layers of protection:

1. **Hard block on form binding (always-correct, no DB).** `AnswerFieldMap.WhatToChangeToOutcomeKey`
   (Application layer, referenced directly from Web) maps the request form's reasons to outcome
   keys. If an outcome's key is a **target of this map**, deletion is **disabled** — the UI explains
   it is in active use by the request form and names the bound reason(s), and that removal requires
   a code change (remove it from the map and the form first). In practice this protects all the
   "real" outcomes; **only admin-added orphan outcomes** (keys not in the map) are UI-deletable.
2. **Connected-data display + typed confirm (for deletable orphans).** The confirmation interstitial
   shows connected data before allowing deletion — a count (and list/link) of `ChangeRequest` rows
   of that type, highlighting `Submitted` (active) ones — and requires the admin to type the outcome
   key to proceed.

Implementation notes / assumptions to verify during build:
- Confirm `ChangeRequest.RequestType` stores the same vocabulary that maps (via
  `WhatToChangeToOutcomeKey`) to the outcome key, so the connected-request count is accurate. The
  producer pipeline is currently a stub, so this count may be empty/partial today — it is
  informational, **not** the gate. The form-binding check (layer 1) is the authoritative gate.
- Branch deletion is lower-risk (the outcome still decides via its remaining branches + the
  `otherwise` terminal), so the heavy guard applies at the **outcome** level, not per branch.

## Lookups editor

- `country-languages.json` is a `code → languages[]` map (e.g. `"GB": ["English","Welsh",...]`).
- A table page: add a country (code + one or more languages), edit a row's language list,
  remove a row. Validation: non-empty code, ≥1 language, no duplicate codes.
- **Versioned and audited the same way** (`ConfigType = Lookups`) — it also drives automated
  decisions via the `officialLanguageIs` predicate.

## Versioning & rollback

- `RulesConfigVersion` is append-only; the blob is the live copy the worker reads.
- **History page** lists versions per config (VersionNumber, CreatedAt, CreatedBy), with a view
  of the stored JSON.
- **Rollback**: load a snapshot → **re-validate** (guards against e.g. a field removed from the
  catalogue in code since the snapshot was taken) → write to blob as a **new** version
  (non-destructive). If a rolled-back snapshot fails validation, block and explain.

## Authorization

- `AdminRulesController` is `[Authorize(Roles = WikiConstants.AdminRole)]` (`cypmd_admin`).
- The `/admin` landing is already admin-gated, so the new nav tile is only visible to admins;
  no extra nav-visibility plumbing required.

## Testing

- **E2E auth** mirroring `AdminAuthTests` / `AdminSettingsTests`: anon → sign-in redirect,
  non-admin → denied, admin → 200; save endpoints reject non-admin.
- **Unit**: form-model → `RuleSet` rebuild; the validate/persist pipeline (valid save writes +
  versions + audits; invalid save writes nothing); the new duplicate-outcome-key rule; lookups
  validation; rollback re-validation; **deletion guard** (a form-bound outcome cannot be deleted;
  an orphan outcome can, and the connected-data count is surfaced). Reuse `RuleSetValidatorTests`.
- **Playwright E2E** of the editor flow: build a branch with a nested group via select-then-
  group, save, confirm it appears in history, roll back, add and remove an outcome.

## Risks & deployment notes

- **Prod blob-write permissions**: `Web/Program.cs:126` has a TODO that blob-storage
  permissions are not configured for deployed environments. Writing `rules-config` blobs from
  the Web app in prod requires the managed identity to have blob-write on that container — flag
  for infra. Works locally on Azurite today.
- **Concurrency** handled via ETag (above); two admins can't silently clobber each other.
- **Worker latency**: changes are not instant — ~5 min refresh. Surfaced in the UI copy so
  admins aren't surprised.
- Do **not** touch `SEND Framework/` (legacy) or the stray `Copy-Rules_Engine/` directory.

## Key existing files to build against

- `Application/RulesEngine/RuleSet.cs`, `Predicate.cs`, `FieldCatalogue.cs`, `DecisionStatus.cs`,
  `CompareOp.cs`, `Lookups.cs`, `RuleSetValidator.cs`, `Json/RulesJson.cs`,
  `Json/PredicateJsonConverter.cs`, `Json/FieldValueJsonConverter.cs`.
- `Infrastructure/RulesEngine/BlobRulesProvider.cs`, `BlobRulesProviderOptions.cs`.
- `Web/Program.cs` (BlobServiceClient :124, antiforgery header :141), `Controllers/
  AdminSettingsController.cs` (+ `Views/Admin/Settings/`) as the structural template,
  `Admin/Nav/IAdminNavEntry.cs`, `RulesEngineNavEntry.cs`, `SystemAdminGroupNavEntry.cs`,
  `AdminNavKeys.cs`, `Extensions/AdminNavServiceCollectionExtensions.cs`,
  `Controllers/WikiConstants.cs` (AdminRole).
- `Persistence/Entities/ContentBlockVersion.cs` + its configuration (version-table pattern),
  `Entities/AuditEntry.cs`, `PortalDbContext`/`IPortalDbContext`.
