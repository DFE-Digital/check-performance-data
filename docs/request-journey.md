# Request Journey

This document describes the full user flow from the Check Your Pupil Data page through to submission or draft-save of a change request. It covers URLs, session state, blob storage, and the database at each stage.

---

## Overview

A change request is initiated when a school user decides they want to amend a pupil's record. The journey proceeds through five distinct phases:

```
Check Your Pupil Data
        ↓
What Would You Like to Change?
        ↓
Question Flow  (starts with one or more PupilSearch pages, then config-driven questions)
        ↓
Summary  →  Submit  →  Confirmation
              or
           Save Draft  →  Check Your Pupil Data
```

Throughout the journey, state is accumulated in an ASP.NET session keyed by `windowId`. No data is written to permanent storage until either submission or a draft save.

---

## Stage 1 — Check Your Pupil Data

**URL:** `GET /CheckYourPupilData/{windowId}`  
**Controller:** `CheckYourPupilDataController.Index`

This page lists the pupils in a checking window. Navigating here **clears any existing request state** for the window:

```csharp
HttpContext.Session.ClearRequestState(windowId);
```

This means navigating back to this page from mid-journey abandons any in-progress request. The `windowId` is a `Guid` matching the `CheckingWindow.Id` in the database.

From here, the user clicks a "Request change" or equivalent link, which takes them to:

---

## Stage 2 — What Would You Like to Change?

**URL:** `GET /WhatToChange/{windowId}`  
**Controller:** `WhatToChangeController.Index` / `Confirm`

The user selects what they want to change (e.g. `Remove`). On POST:

```csharp
HttpContext.Session.SaveRequestState(windowId, s =>
{
    s.SelectedWhatToChange = vm.SelectedWhatToChange;
    s.CheckingWindow = window;          // CheckingWindowDto loaded from DB
});
```

The controller then fetches the question flow config for the selected `WhatToChange` + window type combination and redirects to the config's first page:

**Redirects to:** `GET /Journey/{windowId}/page/{config.FirstPageId}`

This first page is always a `PupilSearch` page — see [PupilSearch pages](#pupilsearch-pages) below.

**Session after this stage:**
| Field | Value |
|---|---|
| `SelectedWhatToChange` | e.g. `WhatToChange.Remove` |
| `CheckingWindow` | `CheckingWindowDto` (Id, Title, Type, StartDate, EndDate) |

---

## Stage 3 — Question Flow

**URLs:**
- `GET  /Journey/{windowId}/page/{pageId}` — display a question page
- `POST /Journey/{windowId}/page/{pageId}` — submit answers and advance
- `GET  /Journey/{windowId}/pupil-search/{pageId}` — display a pupil search page
- `POST /Journey/{windowId}/pupil-search/{pageId}` — submit pupil selection and advance
- `POST /Journey/{windowId}/page/{pageId}/question/{questionId}/upload` — upload a file
- `POST /Journey/{windowId}/page/{pageId}/question/{questionId}/remove` — remove a file

**Controller:** `JourneyController`

### Session readiness

The journey checks `IsSessionReady` before every action. This requires only `SelectedWhatToChange` and `CheckingWindow` to be non-null — pupil selection happens within the journey via `PupilSearch` pages and does not need to be pre-populated.

### Config

The question flow is defined by a JSON file in `Web/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json` (e.g. `Remove_KS4June.json`, `Merge_KS4June.json`). This file is uploaded to Azure Blob Storage on startup in dev via `SeedQuestionFlows`, and fetched at runtime via `IQuestionFlowBlobClient`.

> For a page-by-page breakdown and branching diagram of the `Remove_KS4June.json` flow, see [Remove (KS4 June) — Question Flow](./Remove_KS4June-flow.md).

The config is cached in-process with `Priority = NeverRemove` — it is fetched from blob storage once per application lifetime per flow type.

Each page in the JSON has:
- `id` — unique string identifier
- `type` — `Question` (default), `Content`, `EvidenceUpload`, or `PupilSearch`
- `questions` — list of questions (question pages only)
- `nextPageId` — default next page (overridden per radio option for branching; absent on `PupilSearch` pages means redirect to Summary)

### PupilSearch pages

All flow configs begin with one or more `PupilSearch` pages. These are full-page accessible-autocomplete pupil selectors handled by `JourneyController.PupilSearchPage` / `PupilSearchPost`. The `JourneyController.Page` (GET) dispatcher transparently redirects to `PupilSearchPage` when the requested page has `type: "PupilSearch"`, so navigation guards, summary redirects, and `GetNextPageId` all work uniformly.

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

**`PupilSearch` page fields:**

| Field | Required | Values | Notes |
|---|---|---|---|
| `pupilFilter` | yes | `"Included"` / `"All"` | `Included` limits results to Pincl codes `[401,403,414,421,431]`; `All` returns every pupil for the school |
| `pupilKey` | yes | `"primary"` / `"match"` | Controls which session field is populated (see below) |
| `nextPageId` | no | page id string | Absent → redirect to Summary after selection |
| `validationFailure` | no | string | Error shown when no pupil is submitted. Supports `{pupilName}`. Falls back to `"Enter the name of the pupil"` |

**Suggestions endpoint:** `GET /pupils/suggestions?windowId={id}&query={q}&filter=Included|All&excludePupilId={guid}`  
Served by `PupilSuggestionsController`. Queries PostgreSQL via `ICheckYourPupilDataService.GetPupilSuggestionsAsync`. The `match` pupil page automatically passes the primary pupil's ID as `excludePupilId` so the same pupil cannot be selected twice.

**On successful pupil selection (`PupilSearchPost`):**

- **`pupilKey: "primary"`** — saves to `SelectedPupil*`, generates the reference number, resets `QuestionAnswers` and `QuestionHistory`. This is the pupil the request is about.
- **`pupilKey: "match"`** — saves to `MatchedPupil*`. Preserves existing history and answers. Used by Merge to identify the second record.

After either key, the page ID is appended to `QuestionHistory` (identical to how question pages work), and navigation proceeds to `nextPageId` (or Summary if absent).

**Example — Remove (one PupilSearch page):**
```
GET /Journey/{windowId}/page/select-pupil
  → redirects to GET /Journey/{windowId}/pupil-search/select-pupil
  → user selects primary pupil
POST /Journey/{windowId}/pupil-search/select-pupil
  → saves SelectedPupil, generates reference
  → redirects to GET /Journey/{windowId}/page/reason
```

**Example — Merge (two PupilSearch pages):**
```
GET /Journey/{windowId}/pupil-search/select-pupil   ← primary (Included filter)
POST → saves SelectedPupil, generates reference
     → redirects to GET /Journey/{windowId}/pupil-search/select-match-pupil

GET /Journey/{windowId}/pupil-search/select-match-pupil   ← match (All filter, excludes primary)
POST → saves MatchedPupil
     → no nextPageId → redirects to Summary
```

**Session after primary PupilSearch page:**
| Field | Value |
|---|---|
| `SelectedPupil` | `PupilDto` (name, DOB, UPN, Cypmd_Id, etc.) |
| `SelectedPupilId` | GUID string |
| `SelectedPupilLabel` | Display label from autocomplete |
| `ReferenceNumber` | e.g. `"CYPMD_KS4June_A3F8D12"` |
| `QuestionAnswers` | `{}` (reset) |
| `QuestionHistory` | `["select-pupil"]` |

**Session additionally after match PupilSearch page (Merge only):**
| Field | Value |
|---|---|
| `MatchedPupil` | `PupilDto` |
| `MatchedPupilId` | GUID string |
| `MatchedPupilLabel` | Display label from autocomplete |
| `QuestionHistory` | `["select-pupil", "select-match-pupil"]` |

### Question types

**Question pages** (`type: "Question"` or default) contain one or more questions:

| `type` | Renders | Answer stored as | Notes |
|---|---|---|---|
| `Radio` | Radio buttons | `TextValue` — the selected option value | Options can include `nextPageId` to branch the flow, and `visibleWhen` to gate visibility per user (see [Conditional option visibility](#conditional-option-visibility)) |
| `FreeText` | Single-line input | `TextValue` | |
| `TextArea` | Multi-line textarea | `TextValue` | Optional `charLimit` |
| `Date` | Day / month / year inputs | `DateValue` (`DateAnswer`) | |
| `FileUpload` | PDF upload widget | `FileValues` (list of `FileAnswer`) | Handled via separate upload/remove endpoints; max 6 total pages |
| `Autocomplete` | Accessible-autocomplete dropdown | `TextValue` — the selected display name | Requires `dataSource` in JSON (e.g. `"countries"`). Suggestions fetched from `GET /{dataSource}/suggestions?query=`. A `{fieldName}_code` hidden field carries the machine-readable code but is not currently persisted in `QuestionAnswer`. |

### Navigation guard

Every `Page` and `PupilSearchPage` GET runs `IQuestionFlowService.GetNavigationGuard` before rendering. This prevents users from:
- Jumping ahead to an unvisited page (redirected to the correct next page)
- Revisiting a page after completing the journey (redirected to Summary)

Pages already in `QuestionHistory` are always allowed.

### Answering questions

On each `PagePost`:
1. Each answer is validated (`IJourneyValidationService.ValidateAnswer`). Required answers are validated unconditionally; an `optional` answer is skipped unless it has actually been filled in, in which case its format rules (character limit, real date) are still enforced.
2. If the page sets `requireAtLeastOne`, the page-level rule is checked (see [Require at least one](#require-at-least-one)).
3. If valid, answers are written to `QuestionAnswers` and the page ID is appended to `QuestionHistory`
4. The next page is determined from the config (radio answers can branch)

### Conditional option visibility

A `Radio` option may carry `"visibleWhen": "<ConditionName>"`. The option is rendered only when a registered `IJourneyCondition` with that `Name` evaluates `true` for the current user/journey. This lets one config show different options to different schools — e.g. the **Not on roll** removal reason appears only for independent schools.

- `JourneyController.BuildPageVm` assembles a `JourneyConditionContext` from the session `RequestState` plus a `JourneyUserContext` snapshot taken from `ICurrentUserService` (so the Application layer never touches `HttpContext`).
- `IOptionVisibilityService.GetVisibleOptions(question, ctx)` filters the options in order. Options with no `visibleWhen` always show; an option naming an **unregistered** condition is hidden (fail closed). The result is exposed as `QuestionPartialModel.VisibleOptions`, which `_Radio.cshtml` iterates instead of the raw config options.
- Conditions are pure-logic classes in `Application/Journey/Conditions/`, registered as `IJourneyCondition` in the Application `DependencyManager`. Current condition: `SchoolIsIndependentCondition` — true when the GIAS establishment type id (`organisation_type_id` claim, sourced from the DfE Sign-in `$.type.id` field) is `"11"` (Other Independent School; type 10 is deliberately excluded).

### Conditional question optionality (optionalWhen)

A question may carry `"optionalWhen": "<ConditionName>"` (a bare string or an array of names, AND semantics — same JSON shape and converter as `visibleWhen`). The question is validated as **optional** — overriding `"optional": false` — only when every named `IJourneyCondition` evaluates `true` for the current journey; a name that isn't registered leaves the question mandatory (fail closed). This is separate from `visibleWhen`: the question still renders, only its mandatory-validation status changes, and no UI copy (e.g. an "(optional)" label) changes with it.

- `IQuestionOptionalityService.GetConditionallyOptionalQuestionIds(page, ctx)` returns the set of a page's question IDs that are currently optional. `JourneyController.PagePost` computes this once per page (right after building the `JourneyConditionContext`) and both the file-upload and text mandatory checks consult it via a local `IsMandatory(question)` helper.
- The same set is threaded into `IJourneyValidationService.ValidateEvidencePage(page, journey, pupilName, conditionallyOptionalQuestionIds)` so drafts (`JourneyController.IsEvidencePageValid` → `DetermineStatus`) and the amendment edit-advice screen (`EditAdviceService.GetEvidenceMessages`) re-validate a saved evidence page consistently with the live journey, rather than falling back to always-mandatory.
- Registered condition: **`EalWouldBeAutoRejected`** (PBI 292266) — applied to both questions on the shared KS4June `evidence` page (`Remove_KS4June.json`). It approximates the rules engine's `EAL-REJ-ENG` / `EAL-REJ-OTH-ENGCOUNTRY` auto-reject rules from journey state alone (the removal reason, the `first-language` answer, and the origin country's official languages — see below), so evidence upload/comments become optional exactly when the request is predicted to auto-reject:

  | `first-language` | Origin country's official languages include English? | Evidence |
  |---|---|---|
  | `english` | (irrelevant) | Optional |
  | `other` | yes | Optional |
  | `other` | no / country unknown | Mandatory |
  | `believed-english`, `believed-other` | (irrelevant) | Mandatory (these map to `Uncertain` in the engine, never auto-reject, and go to Scrutiny — a reviewer needs the evidence) |
  | `chose-not-to-say`, `not-known`, or a different removal reason | (irrelevant) | Mandatory (fail-safe; the evidence page is shared across removal branches) |

  The condition mirrors the engine exactly: the engine maps `believed-english` / `believed-other` to an `Uncertain(...)` value, and tri-state `LeafEq` evaluation (`RulesEngine.cs`) returns `Unknown` for uncertain inputs, so neither reject rule ever fires for them — those requests go to **Scrutiny**, not auto-reject, and evidence stays mandatory so the reviewer has something to assess. (The PBI's original AC Scenario 004 waived evidence for `believed-other` + an English-speaking country; that scenario was withdrawn by the BA on 2026-07-28.) The rules engine itself was deliberately left unchanged — no rule removed, no outcome logic touched.
- **`OriginCountryLanguageCapture`** resolves and stores the origin country's official languages on `RequestState.OriginCountryCode` / `OriginCountryLanguages` whenever a page POST answers the `country-originally-from` question — both `JourneyController.PagePost` (after validation succeeds) and `JourneyController.SaveDraft` (over the answers it re-reads from the form before persisting the draft) invoke it, so a "Save and exit" gets the same backfill as "Continue". It reads the same `country-languages.json` lookup the rules engine's `officialLanguageIs` predicate uses (via `IRulesConfigService.GetLookupsAsync`), so the journey-side approximation and the engine read one source of truth. If the autocomplete's hidden code field is empty (a re-POST of a previously answered page keeps the display name but drops the code — `_Autocomplete.cshtml`'s `_code` field is only populated by an explicit JS `onConfirm`), the code is recovered by an exact case-insensitive name lookup (`ICountryService.GetCodeByNameAsync`) and backfilled onto the answer's `CodeValue`, so the rules engine (which reads `CodeValue ?? TextValue`) also gets the code rather than the display name on a re-edited journey. A lookup failure or an unresolvable country stores `null` languages — fail-safe, evidence stays mandatory.
- **Drafts saved before this shipped** have no `OriginCountryLanguages`, so a resumed EAL draft answered `other` sees evidence as mandatory again until the country page is re-posted (the resume path does not force this). Fail-safe direction, and `english` drafts are unaffected because that branch never consults the country.
- Because the waiver depends on answers the user can still change afterwards, `Summary` re-validates the reachable evidence page before rendering and redirects back to it if a late change (e.g. `first-language` english → other) has made it mandatory again. Nothing between the Summary and submission validates otherwise.

### Require at least one

A page with `"requireAtLeastOne": true` must have at least one of its questions answered, even when each question is individually `optional`. This is used by the **Not on roll** evidence page, where either an uploaded file *or* a written explanation is acceptable. `IJourneyValidationService.ValidateRequireAtLeastOne` returns a `RequireAtLeastOneResult` (a summary lead-in message plus per-question field errors) when nothing is answered; the controller adds these to `ModelState` and surfaces the summary message in `_JourneyErrorSummary.cshtml` via `PageViewModel.AtLeastOneError`.

**Session after each question page:**
| Field | Updated to |
|---|---|
| `QuestionAnswers` | `{ "reason": { TextValue: "social-care-involvement" }, ... }` |
| `QuestionHistory` | `["select-pupil", "reason", "social-care", ...]` — ordered list of visited page IDs |

### File uploads (EvidenceUpload page type)

The evidence page has `type: "EvidenceUpload"` in the JSON and renders a distinct Razor view (`EvidenceUpload.cshtml`). File uploads are handled separately to answer submission:

- Each upload is a separate POST to `/Journey/{windowId}/page/{pageId}/question/{questionId}/upload`
- The file is read as bytes, validated (must be PDF, ≤ 10 MB, ≤ 6 total pages across all uploads)
- If valid, the bytes are stored via `IFileStorageService` (`EvidenceBlobStorageService`) to:
  ```
  Container: {windowId}
  Blob:      evidence-uploads/{newGuid}
  ```
  The GUID blob name is returned and stored in session as `FileAnswer.StoredFileName`. The original filename and page count are also stored in session but the bytes themselves live only in blob storage.
- Removing a file (`/remove`) deletes the blob and removes the `FileAnswer` entry from session

The text area on the evidence page is submitted via the normal `PagePost` route. An evidence page may set `requireAtLeastOne` so that the file upload and explanation are each `optional` individually but at least one must be provided — see [Require at least one](#require-at-least-one).

### Content pages

Pages with `type: "Content"` display CMS-managed content (via `EditableContent` view components) and have no questions. The user clicks Continue, which adds the page to history and moves to the next page.

### Autocomplete data sources

The `Autocomplete` question type is generic — the `dataSource` field in the question JSON determines which lookup is used at runtime. The partial view (`_Autocomplete.cshtml`) constructs the suggestions URL as `/{dataSource}/suggestions?query=...`.

Currently available data sources:

| `dataSource` | Endpoint | Backed by |
|---|---|---|
| `countries` | `GET /Countries/suggestions` | `Countries` table (seeded from FCDO Geographical Names Index + UK home nations + Crown Dependencies) |

The `Countries` table contains ~203 entries: ~195 FCDO sovereign states (excluding `GB`), four UK home nations (`GB-ENG`, `GB-SCT`, `GB-WLS`, `GB-NIR`), and three Crown Dependencies (`IM`, `JE`, `GG`). All entries carry a `CountryKind` discriminator (`Sovereign`, `HomeNation`, `CrownDependency`, `OverseasTerritory`). Note that `GB-ENG` is retained in the dataset — question flow configs for journeys where England should not appear as an option must filter it at the config level (e.g. by using a dedicated flow for schools in England).

---

## Stage 4 — Summary

**URL:** `GET /Journey/{windowId}/summary`  
**Controller:** `JourneyController.Summary`

The summary page renders a GOV.UK summary list of all answers. It can only be reached when `QuestionHistory` is complete (i.e. `GetNextPageId` after the last visited page returns null). Incomplete journeys are redirected back to the next unanswered page.

`PupilSearch` and `Content` pages are skipped when building summary rows — only question pages with answers appear.

**Pupil rows:**

- **Remove / Include** — a single "Pupil name" row shows `SelectedPupil` with a Change link to the primary `PupilSearch` page.
- **Merge** — two rows replace the single "Pupil name" row:
  - **"First record to merge"** — `"{Firstname} {Surname}, {d MMMM yyyy}"` (e.g. `"Jane Smith, 27 July 2010"`) with a Change link to the primary `PupilSearch` page.
  - **"Second record to merge"** — `"{Firstname} {Surname} {d MMMM yyyy} ({Cypmd_Id})"` (e.g. `"John Doe 2 February 2010 (CYPMD456)"`) with a Change link to the match `PupilSearch` page. If the DOB cannot be parsed the raw stored value is shown; if it is missing entirely the DOB segment is omitted (`"{name} ({id})"`).
  
  Change links for `PupilSearch` pages use the `PupilSearchPage` action rather than the `Page` action. The back link on the summary page also uses `PupilSearchPage` when the last page in `QuestionHistory` is a `PupilSearch` page.

Each question row shows the resolved question title and display answer. Radio answers show their label (not raw value). File uploads show filename and page count. Dates show as "5 January 2026" (day without leading zero, full month name, four-digit year).

The user can click "Change" on any question row to return to that page (`fromSummary=true`). When they submit from a summary-edit page, only pages up to and including the edited page are kept in history — any later pages that depended on a changed radio branch are trimmed.

From the Summary, the user can:
- **Submit the request** → `POST /Journey/{windowId}/summary`
- **Save and continue later** → `POST /Journey/{windowId}/draft`

---

## Stage 5a — Submission

**URL:** `POST /Journey/{windowId}/summary`  
**Controller:** `JourneyController.SummaryConfirm`

### What happens

1. **Duplicate-request check** — `HasSubmittedRequestAsync` (called from `PupilSearchPost`) / `CheckForConflictAsync` (called from `ConfirmRequestAsync`) queries `ChangeRequests` for an existing `SubmittedUnCommitted` row matching `WindowId + PupilId + OrganisationUrn` (excluding the current `ReferenceNumber` when one exists). Returns a `DuplicateCheckResult` discriminated record:

   - `NoConflict` — no conflicting request exists, proceed
   - `SelfSubmitted(ReferenceNumber, ConflictingReasonType, ConflictingRequestCategory, ConflictingUserName)` — the current user already has a submitted request
   - `OtherSubmitted(ReferenceNumber, ConflictingReasonType, ConflictingRequestCategory, ConflictingUserName)` — a colleague has a submitted request (identity revealed via `ConflictingUserName`)

   The check runs at two points:

   1. **Pupil selection** (`PupilSearchPost`) — after the user selects a pupil and clicks Continue. Only runs on non-`MatchKey` pages (skipped for the second pupil selector in Merge flows). On conflict, re-renders the page with:
      - A GDS error summary: **"A request has already been submitted for this pupil"** (top-level) + **"Choose another pupil"** (field-level)
      - A MOJ attention banner with a contextual message and a link to the existing request

   2. **Final submission** (`SummaryConfirm`) — catches conflicts that arose between pupil selection and submission (e.g. another user submitted in a different tab). On conflict, re-renders the Summary page with a contextual error message (no banner).

   Both check points produce messages from a 2×2 matrix of **`isSelf` × `reasonsMatch`** (whether the current request's reason type matches the conflicting request's):

   | Scenario | Pupil-search attention banner | Summary error message |
   |---|---|---|
   | **Self + same reason** | "You have already submitted a {topLevelRequest} for {pupilName}. Reference {refNum} [link]. To raise a new request, delete the previously submitted request. Then return to this page to continue." | "You have already submitted a {topLevelRequest} for this pupil." |
   | **Other + same reason** | "Your colleague {userName} has already submitted a {topLevelRequest} for {pupilName}. Reference {refNum} [link]. To raise a new request, delete the previously submitted request. Then return to this page to continue." | "A colleague at your school has already submitted a {topLevelRequest} for this pupil." |
   | **Self + different reason** | "You have already submitted a request of a different type ({topLevelRequest}) for {pupilName}. Reference {refNum} [link]. To raise a new request, delete the previously submitted request. Then return to this page to continue." | "You have already submitted a request of a different type ({topLevelRequest}) for this pupil." |
   | **Other + different reason** | "Your colleague {userName} has already submitted a request of a different type ({topLevelRequest}) for {pupilName}. Reference {refNum} [link]. To raise a new request check with your colleague, and if you want to proceed, delete the previously submitted request. Then return to this page to continue." | "A colleague at your school has already submitted a request of a different type ({topLevelRequest}) for this pupil." |

   `{topLevelRequest}` is mapped from the request category: `"Remove"` → `"pupil removal request"`, `"Include"` → `"pupil inclusion request"`, `"Merge"` → `"pupil merge request"`.

   The `DuplicateRequestException` carries `ConflictType` (`SelfSubmitted` / `OtherSubmitted`) plus `ConflictingReasonType`, `ConflictingRequestCategory`, `ConflictingUserName`, and `ReasonsMatch` so the error message is contextualised without re-querying.

   Idempotency is provided by the reference-number exclusion in `CheckForConflictAsync` (passing the actual `ReferenceNumber` at submit time avoids self-conflict) and the upsert's overwrite behaviour on the existing row.

2. **Build `RequestDocument`** — a structured document containing:
   - Reference number, submitted-at timestamp
   - Submitted-by (user ID and display name from `ICurrentUserService`)
   - School details (URN and name from `ICurrentUserService`)
   - `Pupil` — primary pupil details (from session `SelectedPupil`)
   - `MatchedPupil` — second pupil details (from session `MatchedPupil`, present for Merge only; null otherwise)
   - All answers in submission order (radio answers resolved to labels, dates formatted DD/MM/YYYY, files listed by filename). `PupilSearch` and `Content` pages are excluded from the answers list.

3. **Save request blob** — `IRequestBlobClient.SaveRequestAsync(windowId, document)` writes a JSON file to:
   ```
   Container: {windowId}
   Blob:      request_{referenceNumber}.json
   ```

4. **Upsert DB row** — `IRequestRepository.UpsertAsync(data)` with `Status = Submitted`. If a Draft row already exists for this reference number it is updated to Submitted; otherwise a new row is inserted into `ChangeRequests`.

5. **Partial session wipe** — Journey-specific fields are cleared so that back-button resubmit hits `IsSessionReady → false`:
   ```csharp
   s.SelectedWhatToChange = null;
   s.SelectedPupil = null;
   s.QuestionAnswers = new();
   s.QuestionHistory = new();
   // ReferenceNumber and CheckingWindow preserved for Confirmation page
   ```

6. **Redirect** → `GET /Journey/{windowId}/confirmation`

### ChangeRequests database row (submitted)

| Column | Value |
|---|---|
| `Id` | New `Guid` (or existing Draft row's Id) |
| `WindowId` | The checking window GUID |
| `ReferenceNumber` | e.g. `CYPMD_KS4June_A3F8D12` |
| `RequestType` | The chosen reason value from the `useAsRequestType` question (e.g. `not-on-roll`) |
| `Status` | `Submitted` |
| `OrganisationUrn` | School URN (long) |
| `PupilUpn` | Primary pupil's UPN |
| `PupilFirstname` / `PupilSurname` | From session `SelectedPupil` |
| `Submitted` | UTC timestamp (`timestamp without time zone`) |
| `SubmittedById` | DfE Sign-In user GUID |
| `SubmittedByName` | User's display name |

---

## Stage 5b — Save Draft

**URL:** `POST /Journey/{windowId}/draft`  
**Controller:** `JourneyController.SaveDraft`

Available from:
- The **EvidenceUpload page** — button uses `formaction` to override the form's action, so the text area value is included in the POST. The `pageId` is passed so the controller can capture the unsaved text area answer into session before saving.
- The **Summary page** — no `pageId`, answers are already in session.

### What happens

1. If `pageId` is provided: any non-file answers on that page are read from the form and saved to session. This handles the case where the text area has been filled in but not yet submitted. If the posted answers include `country-originally-from`, `OriginCountryLanguageCapture.ApplyAsync` runs first (same as `PagePost`), backfilling `CodeValue` and `OriginCountryCode`/`OriginCountryLanguages` before they're saved — otherwise a re-rendered country page's empty hidden `_code` field would overwrite the good answer with a null code.

2. **Save draft blob** — `IDraftBlobClient.SaveDraftAsync(windowId, referenceNumber, journey)` writes the full `RequestState` as JSON to:
   ```
   Container: {windowId}
   Blob:      draft_requests/{referenceNumber}.json
   ```
   This preserves everything needed to resume the journey — answers, history, pupil selection, reference number.

3. **Upsert DB row** — `IRequestRepository.UpsertAsync(data)` with `Status = Draft`. Creates the row if it doesn't exist, or updates it (e.g. if the user saves multiple times mid-journey).

4. **Redirect** → `GET /CheckYourPupilData/{windowId}`

### ChangeRequests database row (draft)

Same structure as submitted, but `Status = Draft`. The `Submitted` column holds the time the draft was last saved.

---

## Stage 6 — Confirmation

**URL:** `GET /Journey/{windowId}/confirmation`  
**Controller:** `JourneyController.Confirmation`

Reads `ReferenceNumber` and `CheckingWindow.EndDate` from the remaining session state. If either is absent, redirects to Check Your Pupil Data. Otherwise displays the reference number and the window close deadline.

---

## Session structure (`RequestState`)

`RequestState` (`Application/Journey/RequestState.cs`) is serialised to JSON and stored in the ASP.NET distributed session under the key `request_{windowId}`.

| Property | Type | Set at |
|---|---|---|
| `SelectedWhatToChange` | `WhatToChange?` | Stage 2 |
| `CheckingWindow` | `CheckingWindowDto?` | Stage 2 |
| `SelectedPupil` | `PupilDto?` | Stage 3 — primary `PupilSearch` page |
| `SelectedPupilId` | `string?` | Stage 3 — primary `PupilSearch` page |
| `SelectedPupilLabel` | `string?` | Stage 3 — primary `PupilSearch` page |
| `MatchedPupil` | `PupilDto?` | Stage 3 — match `PupilSearch` page (Merge only) |
| `MatchedPupilId` | `string?` | Stage 3 — match `PupilSearch` page (Merge only) |
| `MatchedPupilLabel` | `string?` | Stage 3 — match `PupilSearch` page (Merge only) |
| `ReferenceNumber` | `string?` | Stage 3 — primary `PupilSearch` page |
| `QuestionAnswers` | `Dictionary<string, QuestionAnswer>` | Stage 3 (question pages) |
| `QuestionHistory` | `List<string>` | Stage 3 (all pages including `PupilSearch`) |
| `OriginCountryCode` | `string?` | Stage 3 — any page POST answering `country-originally-from` (see [Conditional question optionality](#conditional-question-optionality-optionalwhen)); also recomputed on Save Draft (Stage 5b) if that page's answers are being saved |
| `OriginCountryLanguages` | `List<string>?` | Stage 3 — same as above |
| `SelectedNextStep` | `NextSteps?` | Not used in journey (future) |

`IsSessionReady` checks that `SelectedWhatToChange` and `CheckingWindow` are non-null. Pupil selection is handled in-journey via `PupilSearch` pages and is not required before the journey begins. Any action that fails this check redirects to Check Your Pupil Data.

---

## Blob storage layout

All blobs for a checking window share a container named by the window's GUID:

```
Container: f34d285b-8660-4d12-9c30-787328deaa0a   ← windowId
│
├── request_{referenceNumber}.json                  ← Submitted RequestDocument
│
├── draft_requests/
│   └── {referenceNumber}.json                      ← Serialised RequestState (draft)
│
└── evidence-uploads/
    └── {guid}                                      ← Uploaded PDF bytes (no extension)
```

The `question-flows` container is separate and global:

```
Container: question-flows
├── Remove_KS4June.json                             ← Remove flow config
└── Merge_KS4June.json                              ← Merge flow config
```

---

## Guard rails and failure modes

**Navigation guards** — `GetNavigationGuard` prevents URL manipulation to skip pages or jump branches. Attempts to access an out-of-order page redirect to the correct next page. This applies equally to `PupilSearch` pages and question pages.

**Double-submit on confirmation** — the idempotency check (`IsSubmittedAsync`) means re-firing the confirmation POST (back button, double click, network retry) is silent and safe. The blob write is also idempotent (`overwrite: true`). The session partial wipe means `IsSessionReady` returns false for subsequent attempts after the first, providing a second layer of defence.

**Draft before submission** — if a Draft DB row exists when submission occurs, `UpsertAsync` updates it to Submitted rather than inserting a duplicate. A Draft row therefore never co-exists with a Submitted row for the same reference number.

**Session expiry** — sessions have a 30-minute sliding expiry. If a session expires mid-journey, `IsSessionReady` returns false and the user is redirected to Check Your Pupil Data. Any uploaded files remain in blob storage (orphaned) and any saved draft remains accessible via the draft blob and DB row.
