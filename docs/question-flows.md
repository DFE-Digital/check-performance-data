# Question flow format

The reference for the JSON files that drive the change request journeys. [request-journey.md](request-journey.md) describes the journey from start to end. [question-flow-deployment.md](question-flow-deployment.md) records why the files ship in the image.

## Overview

The journey feature (pupil change requests) is driven by JSON config files that define branching question flows.

- **Config files** live in `Web/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json` (e.g. `Remove_KS4June.json`).
- **They ship in the release image and are served from it in every environment.** `IQuestionFlowConfigSource` (Application) is implemented by `FileSystemQuestionFlowClient` (Infrastructure), reading `{ContentRootPath}/Data/QuestionFlows/`. `IQuestionFlowService` caches the result in `IMemoryCache` with `NeverRemove` priority. The interface is **read-only** — nothing writes a config at runtime.
- **Two flows may share a page id.** `Remove_KS2.json` reuses the KS4 June page ids on purpose, so the rules keyed by page id (`RemovalJourneyDateRules`, `PageDateRules`) apply to both. A test that looks a page up by id must check every page with that id, or name the file; never call `Single` on the id alone.
- **There is no blob container and no seeding step.** Configs used to be read from the `question-flows` blob container, which Terraform provisions empty and which only a Development-gated seeder ever filled, so a config change did not reach QA/preproduction/production by deploying. `docs/question-flow-deployment.md` records the decision and what a storage-backed source would have to do if one is ever wanted again. The container is still declared in Terraform, unused, on purpose.

### Page types

| Type | JSON `"type"` | Notes |
|---|---|---|
| `Question` | `"Question"` (default) | One or more questions rendered in sequence. Handled by `JourneyController.Page` / `PagePost`. |
| `Content` | `"Content"` | Informational page with no questions. Skipped in summary and `RequestDocument` answers. |
| `EvidenceUpload` | `"EvidenceUpload"` | Specialised question page for file uploads. Rendered by `EvidenceUpload.cshtml`. |
| `PupilSearch` | `"PupilSearch"` | Full-page accessible-autocomplete pupil selector. Handled by `JourneyController.PupilSearchPage` / `PupilSearchPost`. Skipped in summary rows and `RequestDocument` answers. |

#### `PupilSearch` page fields

```json
{
  "id": "select-pupil",
  "type": "PupilSearch",
  "title": "Which pupil do you want to remove?",
  "pupilFilter": "Included",
  "pupilKey": "primary",
  "nextPageId": "reason",
  "validationFailure": "Select the name of the pupil you want to remove"
}
```

| Field | Required | Values | Notes |
|---|---|---|---|
| `pupilFilter` | yes | `"Included"` / `"All"` / `"Includable"` / `"NotIncludable"` | `Included` limits results to pupils stamped `INCLUDED`; `All` returns every pupil for the school; `Includable` returns pupils stamped `INCLUDABLE` — the Include journey's search, which on 16-19 adds the included file's P_INCL 502 students to the non-included file (#711); `NotIncludable` is the rest, which the Include journey's "already included" guards look a name up in |
| `pupilKey` | yes | `"primary"` / `"match"` | `primary` saves to `RequestState.SelectedPupil*` and generates the reference number; `match` saves to `RequestState.MatchedPupil*` |
| `requireResults` | no | `true` / `false` (default) | Limits the search to students the school holds a 16-19 result for. Independent of `pupilFilter`, which selects by inclusion status. Set on the results-enquiry student search — there is no grade to correct for a student with no result. The page must say so in its `subheading`, because the restriction silently hides students |
| `nextPageId` | no | page id string | Absent means redirect to Summary after selection |
| `validationFailure` | no | string (supports `{pupilName}`) | Error shown when no pupil is submitted. Falls back to `"Enter the name of the pupil"` |

**Suggestions endpoint:** `GET /pupils/suggestions?windowId={id}&query={q}&filter=Included|All|Includable|NotIncludable&excludePupilId={guid}&requireResults=true` (served by `PupilSuggestionsController`). The `match` pupil page automatically passes the primary pupil's ID as `excludePupilId` so it cannot be selected twice. `requireResults` is sent only when the page config sets it; it is a search restriction, never a permission — it can reach no pupil outside the signed-in school's own file. When it is on, the autocomplete's no-match text changes to "No students found with results" so a school can tell a typo from a student who holds nothing.

**Navigation:** `JourneyController.Page` (GET) transparently dispatches to `PupilSearchPage` when the requested page has `Type == PupilSearch`, so all navigation (summary redirects, `GetNextPageId`, navigation guard) works uniformly.

**History:** completing a `PupilSearch` page appends its `id` to `QuestionHistory` exactly like a question page — the existing navigation guard mechanism works without modification.

### Question types

| Type | JSON `"type"` | Notes |
|---|---|---|
| `Radio` | `"Radio"` | Single-choice options; options can set `nextPageId` to branch the flow, and `visibleWhen` to gate visibility (see below) |
| `FreeText` | `"FreeText"` | Single-line text input |
| `TextArea` | `"TextArea"` | Multi-line text; optional `charLimit` |
| `Date` | `"Date"` | Day/month/year fields; stored as `DateAnswer`. Every format failure (blank or part-filled, year not 4 digits, not a real date) shows the question's `validationFailure` + " in the format dd mm yyyy" (#544), so every Date question must set a `validationFailure` that does not spell out the format itself (`QuestionFlowValidatorAlignmentTests` pins this) |
| `FileUpload` | `"FileUpload"` | PDF upload; max 6 pages total across all uploads |
| `Checkbox` | `"Checkbox"` | Multi-select list. Options are shaped exactly like a `Radio`'s (and `visibleWhen` filters them the same way), but an option's `nextPageId` is **ignored** — several boxes may be ticked, so only the page's own `nextPageId` decides. The answer is `QuestionAnswer.SelectedValues` (the ticked values, `TextValue` stays null); "at least one must be ticked" is the ordinary required rule, so `optional: true` still allows an empty list. Displayed by `CheckboxAnswerDisplay.Join` (labels, config order) with `JoinValues` as the request document's raw value |
| `Autocomplete` | `"Autocomplete"` | Accessible-autocomplete dropdown backed by a server suggestions endpoint. Requires a `"dataSource"` field (e.g. `"countries"`) that maps to the URL prefix `/{dataSource}/suggestions`. The selected display name is stored in `QuestionAnswer.TextValue`; the selected code is submitted as `{fieldName}_code` but not currently stored in the answer model. |

**Adding a new `Autocomplete` data source:** create an `I{Source}Service` / `I{Source}Repository` pair in Application, a repository implementation in Persistence querying the relevant table, and a controller in Web with a `GET /{source}/suggestions?query=` endpoint returning `[{id, label}]`. Register both in the respective `DependencyManager.cs`.

### Conditional option visibility (`visibleWhen`)

A `Radio` option may carry a `"visibleWhen": "<ConditionName>"` field. The option is only rendered when a registered `IJourneyCondition` with that `Name` evaluates to `true`. This lets a single config show different options to different users (e.g. the "Not on roll" removal reason only appears for independent schools).

- `IJourneyCondition` (Application) — `Name` + `bool Evaluate(JourneyConditionContext ctx)`. Implementations are pure logic and live in `Application/Journey/Conditions/` (no external dependencies), registered as `IJourneyCondition` in the Application `DependencyManager`. Current condition: `SchoolIsIndependentCondition` (`Name = "SchoolIsIndependent"`, true when GIAS establishment type id is `"11"`, Other Independent School). `SchoolCanRecordNotOnRollCondition` (`Name = "SchoolCanRecordNotOnRoll"`, AB#304119) gates KS4 June "Not on roll": independent **or** an FE college whose LAESTAB is in `Web/Data/NotOnRollColleges/not-on-roll-colleges.json`. On startup the web app copies that file to the `rules-config` container (replacing the blob when it differs) and `NotOnRollCollegeListStore` holds the list in memory, refreshed every five minutes, because conditions are synchronous. A condition may take constructor dependencies; `QuestionFlowValidatorAlignmentTests` builds each one with substitutes.
- `JourneyConditionContext` carries the `RequestState` (`Journey`) and a `JourneyUserContext` (`User`) — a neutral snapshot of the signed-in user assembled in the Web layer from `ICurrentUserService`, so Application never touches `HttpContext`. `OrganisationTypeId` flows from the DfE Sign-in get-organisations `$.type.id` field → `organisation_type_id` claim (added in `ClaimsEnrichmentService`).
- `IOptionVisibilityService.GetVisibleOptions(question, ctx)` filters a question's options preserving order. Options with no `visibleWhen` are always shown; a `visibleWhen` naming an **unregistered** condition is hidden (fail closed). The controller computes `VisibleOptions` per `Radio` question for `BuildPageVm`, and `_Radio.cshtml` iterates those rather than the raw config options.

### Page-level validation (`requireAtLeastOne`)

A page may set `"requireAtLeastOne": true` (e.g. an `EvidenceUpload` page where either a file or a text explanation suffices). When set, the page validates that at least one of its questions is answered even if the individual questions are `optional`. `IJourneyValidationService.ValidateRequireAtLeastOne` returns a `RequireAtLeastOneResult` (summary lead-in message + per-question field errors) when the rule is violated; the controller maps these into `ModelState` and the error summary. Note: when a question is `optional` but *has* been filled in, its format rules (char limit, real date) are still enforced.

### Named format validators (`validator`)

A text question may set `"validator": "<Name>"` to enforce a reusable format rule on its answer once it is non-empty (e.g. a DfE number box uses `"validator": "DfeNumber"`). This mirrors the `visibleWhen`/`IJourneyCondition` pattern:

- `IFormatValidator` (Application, `Application/Journey/Validators/`) — `Name` + `bool IsValid(string value)` + `FailureMessage`. Pure logic, no external dependencies, registered as `IFormatValidator` in the Application `DependencyManager`. Current validator: `DfeNumberFormatValidator` (`Name = "DfeNumber"`, matches `nnn/nnnn` or seven-digit `nnnnnnn`; validation only — it does not normalise between the two forms).
- `JourneyValidationService` takes `IEnumerable<IFormatValidator>` and runs the named rule inside `ValidateAnswer`, *after* the required/type-specific checks pass and *only* when the value is non-empty (emptiness is the required rule's concern). An **unregistered** validator name fails **open** (the check is skipped) so a config typo cannot block every submission — `QuestionFlowValidatorAlignmentTests` guards that every referenced name has an implementation.
- **Adding a new validator:** implement `IFormatValidator` in `Application/Journey/Validators/`, register it in the Application `DependencyManager`, and reference its `Name` from a question's `validator` field.

