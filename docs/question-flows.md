# Question flow format

The reference for the JSON files that drive the change request journeys. [request-journey.md](request-journey.md) describes the journey from start to end. [question-flow-deployment.md](question-flow-deployment.md) records why the files ship in the image.

## Overview

The journey feature (pupil change requests) is driven by JSON config files that define branching question flows.

- **Config files** live in `Web/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json` (e.g. `Remove_KS4June.json`).
- **They ship in the release image and are served from it in every environment.** `IQuestionFlowConfigSource` (Application) is implemented by `FileSystemQuestionFlowClient` (Infrastructure), reading `{ContentRootPath}/Data/QuestionFlows/`. `IQuestionFlowService` caches the result in `IMemoryCache` with `NeverRemove` priority. The interface is **read-only** — nothing writes a config at runtime.
- **Two flows may share a page id.** `Remove_KS2.json` reuses the KS4 June page ids on purpose. Each flow file holds its own rules for those pages. A test that looks a page up by id must check every page with that id, or name the file; never call `Single` on the id alone.
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
| `pupilFilter` | yes | `"Included"` / `"All"` | `Included` limits results to pupils stamped `INCLUDED`; `All` returns every pupil for the school |
| `pupilKey` | yes | `"primary"` / `"match"` | `primary` saves to `RequestState.SelectedPupil*` and generates the reference number; `match` saves to `RequestState.MatchedPupil*` |
| `requireResults` | no | `true` / `false` (default) | Limits the search to students the school holds a 16-19 result for. Independent of `pupilFilter`, which selects by inclusion status. Set on the results-enquiry student search — there is no grade to correct for a student with no result. The page must say so in its `subheading`, because the restriction silently hides students |
| `nextPageId` | no | page id string | Absent means redirect to Summary after selection |
| `validationFailure` | no | string (supports `{pupilName}`) | Error shown when no pupil is submitted. Falls back to `"Enter the name of the pupil"` |

**Suggestions endpoint:** `GET /pupils/suggestions?windowId={id}&query={q}&filter=Included|All&excludePupilId={guid}&requireResults=true` (served by `PupilSuggestionsController`). The `match` pupil page automatically passes the primary pupil's ID as `excludePupilId` so it cannot be selected twice. `requireResults` is sent only when the page config sets it; it is a search restriction, never a permission — it can reach no pupil outside the signed-in school's own file. When it is on, the autocomplete's no-match text changes to "No students found with results" so a school can tell a typo from a student who holds nothing.

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

### Conditions (`visibleWhen`, `optionalWhen`, `requireAtLeastOneWhen`)

For a plain-English guide with examples of common changes, see [journey-rules.md](journey-rules.md).

Three fields take a condition list:

| Field | On | When every entry holds | When an entry is undecided |
|---|---|---|---|
| `visibleWhen` | an option or a question | it is shown | it is hidden |
| `optionalWhen` | a question | it is optional | it stays mandatory |
| `requireAtLeastOneWhen` | a page with `requireAtLeastOne` | the rule applies | the rule applies |

The value is one entry or an array of entries. Every entry must hold (AND). An entry has one of these forms:

- **A fact test**, for example `{ "fact": "school.isIndependent", "is": false }`. It holds when the registered `IJourneyCondition` with that `Name` gives the `is` value. An unregistered fact is undecided. A fact is something that the answers do not hold. Always write `"is"`, also for `true`. A string is not a condition: the flow file fails to load, and the error shows the object to write.
- **An answer test**, for example `{ "answer": "first-language", "in": ["other", "not-known"] }`. It holds when the answer to that question is one of the values. A question with no answer, or a blank answer, is undecided. Do not write a class to test an answer.
- **An any-of test**, for example `{ "anyOf": [ <entry>, <entry> ] }`. It holds when one or more of its entries hold. It needs two or more entries. When no entry holds and one entry is undecided, it is undecided.

These are the facts:

| Fact | True when |
|---|---|
| `pupil.isAddBack` | The selected pupil's inclusion flag (Pincl) is 403, "added back". No pupil, or no Pincl, gives false. |
| `school.isIndependent` | The school's GIAS establishment type id is `"11"` (Other Independent School). Type 10 is not included. |
| `school.isOnNotOnRollList` | The school's LAESTAB is in the not-on-roll college list (see below). |
| `qualification.hasSyllabusCodes` | The selected qualification has one or more syllabus codes. |

Do not write a class for "not X" or for "X or Y". Use `"is": false` or `anyOf` in the flow file.

```json
"optionalWhen": [
  { "answer": "reason", "in": ["english-not-first-language"] },
  { "answer": "first-language", "in": ["other", "believed-other", "chose-not-to-say", "not-known"] }
]
```

```json
"visibleWhen": [
  { "fact": "pupil.isAddBack", "is": false },
  { "anyOf": [
    { "fact": "school.isIndependent", "is": true },
    { "fact": "school.isOnNotOnRollList", "is": true }
  ] }
]
```

`VisibleWhenJsonConverter` reads the three fields into `JourneyConditionRef` lists. It throws on a malformed object, so a bad flow file fails when it loads. `JourneyConditionEvaluator` evaluates a list, and its caller says what an undecided entry gives. `QuestionFlowValidatorAlignmentTests` checks that each fact has a class, and that each answer test names a question in the same flow with one chosen answer (not a `Checkbox`), and only values from its options.

The rest of this section describes `visibleWhen` on options. A `Radio` option with `visibleWhen` is only rendered when its conditions hold. This lets a single config show different options to different users (e.g. the "Not on roll" removal reason only appears for independent schools).

- `IJourneyCondition` (Application) — `Name` + `bool Evaluate(JourneyConditionContext ctx)`. Implementations are pure logic and live in `Application/Journey/Conditions/` (no external dependencies), registered as `IJourneyCondition` in the Application `DependencyManager`. The facts are in the table above. `SchoolIsOnNotOnRollListCondition` (AB#304119) reads the FE colleges in `Web/Data/NotOnRollColleges/not-on-roll-colleges.json`, matched on LAESTAB. On startup the web app copies that file to the `rules-config` container (replacing the blob when it differs) and `NotOnRollCollegeListStore` holds the list in memory, refreshed every five minutes, because conditions are synchronous. A condition may take constructor dependencies; `QuestionFlowValidatorAlignmentTests` builds each one with substitutes.
- `JourneyConditionContext` carries the `RequestState` (`Journey`) and a `JourneyUserContext` (`User`) — a neutral snapshot of the signed-in user assembled in the Web layer from `ICurrentUserService`, so Application never touches `HttpContext`. `OrganisationTypeId` flows from the DfE Sign-in get-organisations `$.type.id` field → `organisation_type_id` claim (added in `ClaimsEnrichmentService`).
- `IOptionVisibilityService.GetVisibleOptions(question, ctx)` filters a question's options preserving order. Options with no `visibleWhen` are always shown; an undecided entry hides the option (fail closed). The controller computes `VisibleOptions` per `Radio` question for `BuildPageVm`, and `_Radio.cshtml` iterates those rather than the raw config options.

### Page-level validation (`requireAtLeastOne`)

A page may set `"requireAtLeastOne": true` (e.g. an `EvidenceUpload` page where either a file or a text explanation suffices). When set, the page validates that at least one of its questions is answered even if the individual questions are `optional`. `IJourneyValidationService.ValidateRequireAtLeastOne` returns a `RequireAtLeastOneResult` (summary lead-in message + per-question field errors) when the rule is violated; the controller maps these into `ModelState` and the error summary. Note: when a question is `optional` but *has* been filled in, its format rules (char limit, real date) are still enforced.

### Named format validators (`validator`)

A text question may set `"validator": "<Name>"` to enforce a reusable format rule on its answer once it is non-empty (e.g. a DfE number box uses `"validator": "DfeNumber"`). This mirrors the `visibleWhen`/`IJourneyCondition` pattern:

- `IFormatValidator` (Application, `Application/Journey/Validators/`) — `Name` + `bool IsValid(string value)` + `FailureMessage`. Pure logic, no external dependencies, registered as `IFormatValidator` in the Application `DependencyManager`. Current validator: `DfeNumberFormatValidator` (`Name = "DfeNumber"`, matches `nnn/nnnn` or seven-digit `nnnnnnn`; validation only — it does not normalise between the two forms).
- `JourneyValidationService` takes `IEnumerable<IFormatValidator>` and runs the named rule inside `ValidateAnswer`, *after* the required/type-specific checks pass and *only* when the value is non-empty (emptiness is the required rule's concern). An **unregistered** validator name fails **open** (the check is skipped) so a config typo cannot block every submission — `QuestionFlowValidatorAlignmentTests` guards that every referenced name has an implementation.
- **Adding a new validator:** implement `IFormatValidator` in `Application/Journey/Validators/`, register it in the Application `DependencyManager`, and reference its `Name` from a question's `validator` field.

### Date rules (`dateRules`)

A `Date` question can have a `dateRules` list. Each rule compares the answer with a date. `DateRuleEvaluator` (Application, `Application/Journey/DateRules/`) runs the rules, and `JourneyValidationService.ValidatePageDates` calls it.

```json
{ "id": "date-pupil-started", "type": "Date",
  "dateRules": [
    { "check": "notInFuture", "message": "The date {pupilName} started at your school must be in the past" },
    { "check": "onOrAfter", "fact": "cohort.start", "message": "The date {pupilName} started at your school cannot be more than 4 years ago when the cohort began" },
    { "check": "onOrAfter", "question": "date-pupil-started-school-in-england", "message": "The date {pupilName} started at your school must be the same as or after {date} when they first started at a school in England" }
  ] }
```

`Remove_KS4June.json` has a full example on the `english-not-first-language-details` page.

Each rule has a `check` and a `message`. There are three checks:

| `check` | Fails when | Target |
|---|---|---|
| `notInFuture` | The answer is later than UK today. Today passes. | None |
| `onOrBefore` | The answer is later than the target. Equal dates pass. | One |
| `onOrAfter` | The answer is earlier than the target. Equal dates pass. | One |

A comparison (`onOrBefore` or `onOrAfter`) names exactly one target:

- `question`: the id of another `Date` question in the same flow. The target is the answer to that question.
- `fact`: the name of a date fact (see the table below).
- `date`: a fixed date, written `yyyy-MM-dd`, for example `"2023-09-01"`.

**Order.** The rules run in list order. The first rule that fails gives the message. Each question gets one message at most. Put `notInFuture` first, because a comparison with a wrong date can mislead the user.

**Message.** `{pupilName}` becomes the pupil name. `{date}` becomes the target date, written `d MMMM yyyy` (for example "1 September 2022").

**When a rule is skipped.**

- A blank or invalid answer takes part in no rule. The format rules report it.
- A comparison is skipped when its target has no value, for example when the user has not answered the target question.
- A date fact that is not registered, or that gives null, skips its rule. The evaluator logs a warning with the fact name only.

**Rules between two questions.** Declare a comparison between two questions on both questions. Each question has its own message. For example, `date-of-birth` has `onOrBefore` `admission-date`, and `admission-date` has `onOrAfter` `date-of-birth`. The evaluator checks only the questions on the current page. It compares them with answers from the whole journey. So when the two questions are on different pages, the error shows on the page that the user is on.

These are the date facts:

| Fact | Value |
|---|---|
| `cohort.start` | 1 September of the school year four years back (for a 2026 window, 1 September 2022). The rules engine checks the same boundary on `schoolAdmissionDate` in `RulesEngineWorker/seed/rules.json`. If one changes, change the other. |

- `IJourneyDateFact` (Application, `Application/Journey/DateRules/`) has a `Name` and `DateOnly? Value(JourneyConditionContext ctx, DateOnly today)`. Use a date fact only for a date that the answers do not hold. The code gives the date, and the flow file holds the rule.
- **Adding a new date fact:** implement `IJourneyDateFact` in `Application/Journey/DateRules/`, register it in the Application `DependencyManager`, add it to the table above, and reference its `Name` from a rule's `fact` field.

A bad rule does not throw at run time. `DateRuleConfigTests` checks every flow file instead:

- Every `Date` question has `notInFuture` as its first rule.
- Only `Date` questions have rules.
- Each rule has a message.
- `notInFuture` names no target, and a comparison names exactly one target.
- Each `question` target is a `Date` question in the same flow.
- Each comparison between two questions has its mirror rule on the other question (`onOrBefore` against `onOrAfter`).
- Each fact has an implementation, and each implementation is registered in the container.

