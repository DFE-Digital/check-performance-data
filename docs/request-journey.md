# Request Journey

This document describes the full user flow from the Check Your Pupil Data page through to submission or draft-save of a change request. It covers URLs, session state, blob storage, and the database at each stage.

---

## Overview

A change request is initiated when a school user decides they want to amend a pupil's record. The journey proceeds through five distinct phases:

```
Check Your Pupil Data
        â†“
What Would You Like to Change?
        â†“
Question Flow  (starts with one or more PupilSearch pages, then config-driven questions)
        â†“
Summary  â†’  Submit  â†’  Confirmation
              or
           Save Draft  â†’  Check Your Pupil Data
```

Throughout the journey, state is accumulated in an ASP.NET session keyed by `windowId`. No data is written to permanent storage until either submission or a draft save.

---

## Stage 1 â€” Check Your Pupil Data

**URL:** `GET /CheckYourPupilData/{windowId}`  
**Controller:** `CheckYourPupilDataController.Index`

This page lists the pupils in a checking window. Navigating here **clears any existing request state** for the window:

```csharp
HttpContext.Session.ClearRequestState(windowId);
```

This means navigating back to this page from mid-journey abandons any in-progress request. The `windowId` is a `Guid` matching the `CheckingWindow.Id` in the database.

From here, the user clicks a "Request change" or equivalent link, which takes them to:

---

## Stage 2 â€” What Would You Like to Change?

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

This first page is always a `PupilSearch` page â€” see [PupilSearch pages](#pupilsearch-pages) below.

**Session after this stage:**
| Field | Value |
|---|---|
| `SelectedWhatToChange` | e.g. `WhatToChange.Remove` |
| `CheckingWindow` | `CheckingWindowDto` (Id, Title, Type, StartDate, EndDate) |

---

## Stage 3 â€” Question Flow

**URLs:**
- `GET  /Journey/{windowId}/page/{pageId}` â€” display a question page
- `POST /Journey/{windowId}/page/{pageId}` â€” submit answers and advance
- `GET  /Journey/{windowId}/pupil-search/{pageId}` â€” display a pupil search page
- `POST /Journey/{windowId}/pupil-search/{pageId}` â€” submit pupil selection and advance
- `POST /Journey/{windowId}/page/{pageId}/question/{questionId}/upload` â€” upload a file
- `POST /Journey/{windowId}/page/{pageId}/question/{questionId}/remove` â€” remove a file

**Controller:** `JourneyController`

### Session readiness

The journey checks `IsSessionReady` before every action. This requires only `SelectedWhatToChange` and `CheckingWindow` to be non-null â€” pupil selection happens within the journey via `PupilSearch` pages and does not need to be pre-populated.

### Config

The question flow is defined by a JSON file in `Web/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json` (e.g. `Remove_KS4June.json`, `Merge_KS4June.json`). The file ships inside the release image and is read from there at runtime via `IQuestionFlowConfigSource` â€” there is no upload step and no blob container (see `docs/question-flow-deployment.md`).

> For a page-by-page breakdown and branching diagram of the `Remove_KS4June.json` flow, see [Remove (KS4 June) â€” Question Flow](./Remove_KS4June-flow.md).

The config is cached in-process with `Priority = NeverRemove` â€” it is fetched from blob storage once per application lifetime per flow type.

Each page in the JSON has:
- `id` â€” unique string identifier
- `type` â€” `Question` (default), `Content`, `EvidenceUpload`, or `PupilSearch`
- `questions` â€” list of questions (question pages only)
- `nextPageId` â€” default next page (overridden per radio option for branching; absent on `PupilSearch` pages means redirect to Summary)

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
| `requireResults` | no | `true` / `false` (default) | Limits the search to students the school holds a 16-19 result for. Independent of `pupilFilter`, which selects by inclusion status. Used by the results enquiry â€” there is no grade to correct for a student with no result. A page that sets it must say so in its `subheading`, because the restriction hides students silently |
| `pupilSearchField` | no | `"All"` / `"CypmdId"` (default `All`) | Which field(s) the query is matched against. `All` (or absent) matches forename, surname, split names, UPN/ULN and CYPMD ID, plus date of birth on 16-19. `CypmdId` matches **only** a trimmed, case-insensitive prefix of `Cypmd_Id` â€” a name, UPN or date of birth returns nothing. Set on `Merge_KS4June.json`'s `select-match-pupil`, whose `subheading` already reads "Start typing ID to search records"; had it said nothing, narrowing the search would hide records silently and read as a typo |
| `nextPageId` | no | page id string | Absent â†’ redirect to Summary after selection |
| `validationFailure` | no | string | Error shown when no pupil is submitted. Supports `{pupilName}`. Falls back to `"Enter the name of the pupil"` |

`pupilSearchField` also changes what the suggestion text says. On a `CypmdId` page a KS4 suggestion is `"Surname, Firstname, DOB (Cypmd_Id)"`, because the KS4 suggestion leads with name and date of birth â€” which are identical across the duplicate records a merge exists to reconcile, so without the ID two rows in the dropdown cannot be told apart. `All` keeps the original `"Surname, Firstname, DOB"`; 16-19 is unchanged either way, as it already prints `CYPMD ID:`. The narrowed value is a property of one page: the merge journey's `select-pupil` page and every other journey keep `All`.

**Suggestions endpoint:** `GET /pupils/suggestions?windowId={id}&query={q}&filter=Included|All&excludePupilId={guid}&requireResults=true&pupilSearchField=All|CypmdId`  
Served by `PupilSuggestionsController`. Reads the school's pupils from the cached per-school-window blob through `ICheckYourPupilDataService.GetPupilSuggestionsAsync` (`CheckYourPupilDataRepository`); it does **not** query PostgreSQL â€” the `Pupils` table was dropped when pupil data moved to blob storage. The `match` pupil page automatically passes the primary pupil's ID as `excludePupilId` so the same pupil cannot be selected twice.

`requireResults` is sent only when the page config sets it. The service resolves the school's set of CYPMD ids with results (`IResultsClient.GetCypmdIdsWithResultsAsync`, served from the same cached results file the enquiry itself reads) and passes it to `SearchPupilsAsync` as an allow-list, which applies it **before** the ten-suggestion cap â€” filtering afterwards would drop the one student who holds results whenever ten who do not sort ahead of them. It is a search restriction, never a permission: it can reach no pupil outside the signed-in school's own file. With it on, the autocomplete's no-match text becomes "No students found with results", so a school can tell a typo from a student who holds nothing.

`pupilSearchField` is sent only when the page config narrows the search; the endpoint defaults to `All`, so omitting it leaves every unconfigured page on the historical matching. It is a search restriction, not a permission â€” like `requireResults`, it cannot reach a pupil outside the signed-in school's own file, and the primary pupil's `excludePupilId` is still applied, so the first record can never be offered as its own match.

**On successful pupil selection (`PupilSearchPost`):**

- **`pupilKey: "primary"`** â€” saves to `SelectedPupil*`, generates the reference number, resets `QuestionAnswers` and `QuestionHistory`. This is the pupil the request is about.
- **`pupilKey: "match"`** â€” saves to `MatchedPupil*`. Preserves existing history and answers. Used by Merge to identify the second record.

After either key, the page ID is appended to `QuestionHistory` (identical to how question pages work), and navigation proceeds to `nextPageId` (or Summary if absent).

**Example â€” Remove (one PupilSearch page):**
```
GET /Journey/{windowId}/page/select-pupil
  â†’ redirects to GET /Journey/{windowId}/pupil-search/select-pupil
  â†’ user selects primary pupil
POST /Journey/{windowId}/pupil-search/select-pupil
  â†’ saves SelectedPupil, generates reference
  â†’ redirects to GET /Journey/{windowId}/page/reason
```

**Example â€” Merge (two PupilSearch pages):**
```
GET /Journey/{windowId}/pupil-search/select-pupil   â† primary (Included filter)
POST â†’ saves SelectedPupil, generates reference
     â†’ redirects to GET /Journey/{windowId}/pupil-search/select-match-pupil

GET /Journey/{windowId}/pupil-search/select-match-pupil   â† match (All filter, excludes primary)
POST â†’ saves MatchedPupil
     â†’ no nextPageId â†’ redirects to Summary
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
| `Radio` | Radio buttons | `TextValue` â€” the selected option value | Options can include `nextPageId` to branch the flow, and `visibleWhen` to gate visibility per user (see [Conditional option visibility](#conditional-option-visibility)) |
| `FreeText` | Single-line input | `TextValue` | |
| `TextArea` | Multi-line textarea | `TextValue` | Optional `charLimit` |
| `Date` | Day / month / year inputs | `DateValue` (`DateAnswer`) | |
| `FileUpload` | PDF upload widget | `FileValues` (list of `FileAnswer`) | Handled via separate upload/remove endpoints; at most 6 files (AB#304900) |
| `Autocomplete` | Accessible-autocomplete dropdown | `TextValue` â€” the selected display name | Requires `dataSource` in JSON (e.g. `"countries"`). Suggestions fetched from `GET /{dataSource}/suggestions?query=`. A `{fieldName}_code` hidden field carries the machine-readable code but is not currently persisted in `QuestionAnswer`. |

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

A `Radio` option may carry `"visibleWhen": "<ConditionName>"`, or `"visibleWhen": ["<ConditionName>", ...]` for several names with AND semantics. The option is rendered only when every named, registered `IJourneyCondition` evaluates `true` for the current user/journey. This lets one config show different options to different schools â€” e.g. the **Not on roll** removal reason appears only for independent schools and a listed set of FE colleges, while **Admitted following permanent exclusion** appears only for schools that are *not* independent.

- `JourneyController.BuildPageVm` assembles a `JourneyConditionContext` from the session `RequestState` plus a `JourneyUserContext` snapshot taken from `ICurrentUserService` (so the Application layer never touches `HttpContext`).
- `IOptionVisibilityService.GetVisibleOptions(question, ctx)` filters the options in order. Options with no `visibleWhen` always show; an option naming an **unregistered** condition is hidden (fail closed). The result is exposed as `QuestionPartialModel.VisibleOptions`, which `_Radio.cshtml` iterates instead of the raw config options.
- Conditions are pure-logic classes in `Application/Journey/Conditions/`, registered as `IJourneyCondition` in the Application `DependencyManager`. Registration is load-bearing, because these services treat an unregistered name as `false`; `QuestionFlowValidatorAlignmentTests` asserts both halves (every name a flow references has an implementation, and every implementation resolves from the container). **`DependencyManager` is the authoritative list of conditions â€” read it there rather than trusting a list restated in this document**, which is how the previous "Current condition:" wording here went stale.
  - `SchoolIsIndependentCondition` â€” true when the GIAS establishment type id (`organisation_type_id` claim, sourced from the DfE Sign-in `$.type.id` field) is `"11"` (Other Independent School; type 10 is deliberately excluded).
  - `SchoolIsNotIndependentCondition` â€” the exact negation of the above, gating **Admitted following permanent exclusion** on the opposite polarity. It delegates to `SchoolIsIndependentCondition` rather than restating the `"11"` test, so the type id stays defined once and the two polarities cannot disagree.
- `SchoolCanRecordNotOnRollCondition` (AB#304119) gates the KS4 June **Not on roll** reason. It is true for an independent school (the rule above) **or** for an FE college whose LAESTAB (`organisation_laestab` claim, reduced to digits) is in the college list. It is one condition because `visibleWhen` ANDs its names. The list:
  - **Source of truth:** `Web/Data/NotOnRollColleges/not-on-roll-colleges.json` in the release image. Only `laestab` is read; URN, UKPRN and name are for people. To change the list, edit this file and deploy.
  - **Storage:** on web startup `NotOnRollCollegeListService` copies it to `not-on-roll-colleges.json` in the `rules-config` container and **replaces the blob when it differs**, so a hand edit to the blob lasts only until the next restart.
  - **Cache:** `NotOnRollCollegeListStore` (a singleton, `INotOnRollCollegeListProvider`) holds the list in memory, because conditions are synchronous. It starts with the bundled list, then reads the blob on startup and every five minutes. A missing, unreadable or malformed blob keeps the list already loaded.

### Conditional question optionality (optionalWhen)

A question may carry `"optionalWhen": "<ConditionName>"` (a bare string or an array of names, AND semantics â€” same JSON shape and converter as `visibleWhen`). The question is validated as **optional** â€” overriding `"optional": false` â€” only when every named `IJourneyCondition` evaluates `true` for the current journey; a name that isn't registered leaves the question mandatory (fail closed). This is separate from `visibleWhen`: the question still renders, only its mandatory-validation status changes, and no UI copy (e.g. an "(optional)" label) changes with it.

- `IQuestionOptionalityService.GetConditionallyOptionalQuestionIds(page, ctx)` returns the set of a page's question IDs that are currently optional. `JourneyController.PagePost` computes this once per page (right after building the `JourneyConditionContext`) and both the file-upload and text mandatory checks consult it via a local `IsMandatory(question)` helper.
- The same set is threaded into `IJourneyValidationService.ValidateEvidencePage(page, journey, pupilName, conditionallyOptionalQuestionIds)` so drafts (`JourneyController.IsEvidencePageValid` â†’ `DetermineStatus`) and the amendment edit-advice screen (`EditAdviceService.GetEvidenceMessages`) re-validate a saved evidence page consistently with the live journey, rather than falling back to always-mandatory.
- Registered condition: **`EalWouldBeAutoRejected`** (PBI 292266, revised by AB#309468) â€” applied to both questions on the shared KS4June `evidence` page (`Remove_KS4June.json`). Evidence upload/comments are optional for exactly four first-language answers:

  | `first-language` | Evidence |
  |---|---|
  | `other`, `believed-other`, `chose-not-to-say`, `not-known` | Optional (regardless of the origin country's official languages) |
  | `english`, `believed-english` | Mandatory |
  | different removal reason | Mandatory (the evidence page is shared across removal branches) |

  AB#309468 supersedes the earlier auto-reject mirroring: the origin country's languages are no longer consulted by this condition, and `believed-other` / `chose-not-to-say` / `not-known` now waive evidence. The rules engine itself is unchanged â€” no rule removed, no outcome logic touched.

- **`OriginCountryLanguageCapture`** resolves and stores the origin country's official languages on `RequestState.OriginCountryCode` / `OriginCountryLanguages` whenever a page POST answers the `country-originally-from` question â€” both `JourneyController.PagePost` (after validation succeeds) and `JourneyController.SaveDraft` (over the answers it re-reads from the form before persisting the draft) invoke it, so a "Save and exit" gets the same backfill as "Continue". It reads the same `country-languages.json` lookup the rules engine's `officialLanguageIs` predicate uses (via `IRulesConfigService.GetLookupsAsync`), so the journey-side approximation and the engine read one source of truth. If the autocomplete's hidden code field is empty (a re-POST of a previously answered page keeps the display name but drops the code â€” `_Autocomplete.cshtml`'s `_code` field is only populated by an explicit JS `onConfirm`), the code is recovered by an exact case-insensitive name lookup (`ICountryService.GetCodeByNameAsync`) and backfilled onto the answer's `CodeValue`, so the rules engine (which reads `CodeValue ?? TextValue`) also gets the code rather than the display name on a re-edited journey. A lookup failure or an unresolvable country stores `null` languages â€” fail-safe, evidence stays mandatory.
- **Drafts** are unaffected by `OriginCountryLanguages` for the evidence waiver: the condition reads only the `reason` and `first-language` answers, which a resumed draft already has.
- Because the waiver depends on answers the user can still change afterwards, `Summary` re-validates the reachable evidence page before rendering and redirects back to it if a late change (e.g. `first-language` english â†’ other) has made it mandatory again. Nothing between the Summary and submission validates otherwise.

### Require at least one

A page with `"requireAtLeastOne": true` must have at least one of its questions answered, even when each question is individually `optional`. This is used by the **Not on roll** evidence page, where either an uploaded file *or* a written explanation is acceptable. `IJourneyValidationService.ValidateRequireAtLeastOne` returns a `RequireAtLeastOneResult` (a summary lead-in message plus per-question field errors) when nothing is answered; the controller adds these to `ModelState` and surfaces the summary message in `_JourneyErrorSummary.cshtml` via `PageViewModel.AtLeastOneError`.

**Session after each question page:**
| Field | Updated to |
|---|---|
| `QuestionAnswers` | `{ "reason": { TextValue: "social-care-involvement" }, ... }` |
| `QuestionHistory` | `["select-pupil", "reason", "social-care", ...]` â€” ordered list of visited page IDs |

### File uploads (EvidenceUpload page type)

The evidence page has `type: "EvidenceUpload"` in the JSON and renders a distinct Razor view (`EvidenceUpload.cshtml`). File uploads are handled separately to answer submission:

- Each upload is a separate POST to `/Journey/{windowId}/page/{pageId}/question/{questionId}/upload`
- The upload is refused straight away if it already holds 6 files (`EvidenceUploadLimits.MaxFiles`, AB#304900): "You can only upload 6 files. Remove a file before you upload another."
- Otherwise the name must not already be used in the request and the file must be â‰¤ 10 MB; it is then read as bytes and must be a readable PDF. The old limit of 6 total *pages* is switched off: `JourneyValidationService`'s `maxEvidencePages` defaults to 0 and the registration passes no value.
- If valid, the bytes are stored via `IFileStorageService` (`EvidenceBlobStorageService`) to:
  ```
  Container: {windowId}
  Blob:      evidence-uploads/{newGuid}
  ```
  The GUID blob name is returned and stored in session as `FileAnswer.StoredFileName`. The original filename and page count are also stored in session but the bytes themselves live only in blob storage.
- Removing a file (`/remove`) deletes the blob and removes the `FileAnswer` entry from session

The file control is GOV.UK Frontend's JavaScript-enhanced File upload (#380): `_FileUpload.cshtml` wraps the input in `govuk-file-upload-wrapper` with `data-module="govuk-file-upload"`, the module renders a "Choose file" button that takes the id `fileUpload`, and the native input becomes `#fileUpload-input`, hidden, still named `fileUpload`. Scripts reach it by name; see `docs/accessibility.md` â†’ "File inputs are the JavaScript-enhanced GOV.UK File upload".

The text area on the evidence page is submitted via the normal `PagePost` route. An evidence page may set `requireAtLeastOne` so that the file upload and explanation are each `optional` individually but at least one must be provided â€” see [Require at least one](#require-at-least-one).

### Content pages

Pages with `type: "Content"` display CMS-managed content (via `EditableContent` view components) and have no questions. The user clicks Continue, which adds the page to history and moves to the next page.

### Autocomplete data sources

The `Autocomplete` question type is generic â€” the `dataSource` field in the question JSON determines which lookup is used at runtime. The partial view (`_Autocomplete.cshtml`) constructs the suggestions URL as `/{dataSource}/suggestions?query=...`.

Currently available data sources:

| `dataSource` | Endpoint | Backed by |
|---|---|---|
| `countries` | `GET /Countries/suggestions` | `Countries` table (seeded from FCDO Geographical Names Index + UK home nations + Crown Dependencies) |

The `Countries` table contains ~203 entries: ~195 FCDO sovereign states (excluding `GB`), four UK home nations (`GB-ENG`, `GB-SCT`, `GB-WLS`, `GB-NIR`), and three Crown Dependencies (`IM`, `JE`, `GG`). All entries carry a `CountryKind` discriminator (`Sovereign`, `HomeNation`, `CrownDependency`, `OverseasTerritory`). Note that `GB-ENG` is retained in the dataset â€” question flow configs for journeys where England should not appear as an option must filter it at the config level (e.g. by using a dedicated flow for schools in England).

---

## Stage 4 â€” Summary

**URL:** `GET /Journey/{windowId}/summary`  
**Controller:** `JourneyController.Summary`

The summary page renders a GOV.UK summary list of all answers. It can only be reached when `QuestionHistory` is complete (i.e. `GetNextPageId` after the last visited page returns null). Incomplete journeys are redirected back to the next unanswered page.

`PupilSearch` and `Content` pages are skipped when building summary rows â€” only question pages with answers appear.

**Pupil rows:**

- **Remove / Include** â€” a single "Pupil name" row shows `SelectedPupil` with a Change link to the primary `PupilSearch` page.
- **Merge** â€” two rows replace the single "Pupil name" row:
  - **"First record to merge"** â€” `"{Firstname} {Surname}, {d MMMM yyyy}"` (e.g. `"Jane Smith, 27 July 2010"`) with a Change link to the primary `PupilSearch` page.
  - **"Second record to merge"** â€” `"{Firstname} {Surname} {d MMMM yyyy} ({Cypmd_Id})"` (e.g. `"John Doe 2 February 2010 (CYPMD456)"`) with a Change link to the match `PupilSearch` page. If the DOB cannot be parsed the raw stored value is shown; if it is missing entirely the DOB segment is omitted (`"{name} ({id})"`).
  
  Change links for `PupilSearch` pages use the `PupilSearchPage` action rather than the `Page` action. The back link on the summary page also uses `PupilSearchPage` when the last page in `QuestionHistory` is a `PupilSearch` page.

Each question row shows the resolved question title and display answer. Radio answers show their label (not raw value). File uploads show filename and page count. Dates show as "5 January 2026" (day without leading zero, full month name, four-digit year).

The user can click "Change" on any question row to return to that page (`fromSummary=true`). When they submit from a summary-edit page, only pages up to and including the edited page are kept in history â€” any later pages that depended on a changed radio branch are trimmed.

From the Summary, the user can:
- **Submit the request** â†’ `POST /Journey/{windowId}/summary`
- **Save and continue later** â†’ `POST /Journey/{windowId}/draft`

---

## Stage 5a â€” Submission

**URL:** `POST /Journey/{windowId}/summary`  
**Controller:** `JourneyController.SummaryConfirm`

### What happens

1. **Duplicate-request check** â€” `HasSubmittedRequestAsync` (called from `PupilSearchPost`) / `CheckForConflictAsync` (called from `ConfirmRequestAsync`) queries `ChangeRequests` for an existing `Submitted` row matching `WindowId + PupilId + OrganisationUrn` (excluding the current `ReferenceNumber` when one exists). Returns a `DuplicateCheckResult` discriminated record:

   - `NoConflict` â€” no conflicting request exists, proceed
   - `SelfSubmitted(ReferenceNumber, ConflictingReasonType, ConflictingRequestCategory, ConflictingUserName)` â€” the current user already has a submitted request
   - `OtherSubmitted(ReferenceNumber, ConflictingReasonType, ConflictingRequestCategory, ConflictingUserName)` â€” a colleague has a submitted request (identity revealed via `ConflictingUserName`)

   The check runs at two points:

   1. **Pupil selection** (`PupilSearchPost`) â€” after the user selects a pupil and clicks Continue. Only runs on non-`MatchKey` pages (skipped for the second pupil selector in Merge flows). On conflict, re-renders the page with:
      - A GDS error summary: **"A request has already been submitted for this pupil"** (top-level) + **"Choose another pupil"** (field-level)
      - A MOJ attention banner with a contextual message and a link to the existing request

   2. **Final submission** (`SummaryConfirm`) â€” catches conflicts that arose between pupil selection and submission (e.g. another user submitted in a different tab). On conflict, re-renders the Summary page with a contextual error message (no banner).

   Both check points produce messages from a 2Ã—2 matrix of **`isSelf` Ã— `reasonsMatch`** (whether the current request's reason type matches the conflicting request's):

   | Scenario | Pupil-search attention banner | Summary error message |
   |---|---|---|
   | **Self + same reason** | "You have already submitted a {topLevelRequest} for {pupilName}. Reference {refNum} [link]. To raise a new request, delete the previously submitted request. Then return to this page to continue." | "You have already submitted a {topLevelRequest} for this pupil." |
   | **Other + same reason** | "Your colleague {userName} has already submitted a {topLevelRequest} for {pupilName}. Reference {refNum} [link]. To raise a new request, delete the previously submitted request. Then return to this page to continue." | "A colleague at your school has already submitted a {topLevelRequest} for this pupil." |
   | **Self + different reason** | "You have already submitted a request of a different type ({topLevelRequest}) for {pupilName}. Reference {refNum} [link]. To raise a new request, delete the previously submitted request. Then return to this page to continue." | "You have already submitted a request of a different type ({topLevelRequest}) for this pupil." |
   | **Other + different reason** | "Your colleague {userName} has already submitted a request of a different type ({topLevelRequest}) for {pupilName}. Reference {refNum} [link]. To raise a new request check with your colleague, and if you want to proceed, delete the previously submitted request. Then return to this page to continue." | "A colleague at your school has already submitted a request of a different type ({topLevelRequest}) for this pupil." |

   `{topLevelRequest}` is mapped from the request category: `"Remove"` â†’ `"pupil removal request"`, `"Include"` â†’ `"pupil inclusion request"`, `"Merge"` â†’ `"pupil merge request"`.

   The `DuplicateRequestException` carries `ConflictType` (`SelfSubmitted` / `OtherSubmitted`) plus `ConflictingReasonType`, `ConflictingRequestCategory`, `ConflictingUserName`, and `ReasonsMatch` so the error message is contextualised without re-querying.

   Idempotency is provided by the reference-number exclusion in `CheckForConflictAsync` (passing the actual `ReferenceNumber` at submit time avoids self-conflict) and the upsert's overwrite behaviour on the existing row.

2. **Build `RequestDocument`** â€” a structured document containing:
   - Reference number, submitted-at timestamp
   - Submitted-by (user ID and display name from `ICurrentUserService`)
   - School details (URN and name from `ICurrentUserService`)
   - `Pupil` â€” primary pupil details (from session `SelectedPupil`)
   - `MatchedPupil` â€” second pupil details (from session `MatchedPupil`, present for Merge only; null otherwise)
   - All answers in submission order (radio answers resolved to labels, dates formatted DD/MM/YYYY, files listed by filename). `PupilSearch` and `Content` pages are excluded from the answers list.

3. **Save request blob** â€” `IRequestBlobClient.SaveRequestAsync(windowId, document)` writes a JSON file to:
   ```
   Container: {windowId}
   Blob:      request_{referenceNumber}.json
   ```

4. **Upsert DB row** â€” `IRequestRepository.UpsertAsync(data)` with `Status = Submitted`. If a Draft row already exists for this reference number it is updated to Submitted; otherwise a new row is inserted into `ChangeRequests`.

5. **Partial session wipe** â€” Journey-specific fields are cleared so that back-button resubmit hits `IsSessionReady â†’ false`:
   ```csharp
   s.SelectedWhatToChange = null;
   s.SelectedPupil = null;
   s.QuestionAnswers = new();
   s.QuestionHistory = new();
   // ReferenceNumber and CheckingWindow preserved for Confirmation page
   ```

6. **Redirect** â†’ `GET /Journey/{windowId}/confirmation`

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

## Stage 5b â€” Save Draft

**URL:** `POST /Journey/{windowId}/draft`  
**Controller:** `JourneyController.SaveDraft`

Available from:
- The **EvidenceUpload page** â€” button uses `formaction` to override the form's action, so the text area value is included in the POST. The `pageId` is passed so the controller can capture the unsaved text area answer into session before saving.
- The **Summary page** â€” no `pageId`, answers are already in session.

### What happens

1. If `pageId` is provided: any non-file answers on that page are read from the form and saved to session. This handles the case where the text area has been filled in but not yet submitted. If the posted answers include `country-originally-from`, `OriginCountryLanguageCapture.ApplyAsync` runs first (same as `PagePost`), backfilling `CodeValue` and `OriginCountryCode`/`OriginCountryLanguages` before they're saved â€” otherwise a re-rendered country page's empty hidden `_code` field would overwrite the good answer with a null code.

2. **Save draft blob** â€” `IDraftBlobClient.SaveDraftAsync(windowId, referenceNumber, journey)` writes the full `RequestState` as JSON to:
   ```
   Container: {windowId}
   Blob:      draft_requests/{referenceNumber}.json
   ```
   This preserves everything needed to resume the journey â€” answers, history, pupil selection, reference number.

3. **Upsert DB row** â€” `IRequestRepository.UpsertAsync(data)` with `Status = Draft`. Creates the row if it doesn't exist, or updates it (e.g. if the user saves multiple times mid-journey).

4. **Redirect** â†’ `GET /CheckYourPupilData/{windowId}`

### ChangeRequests database row (draft)

Same structure as submitted, but `Status = Draft`. The `Submitted` column holds the time the draft was last saved.

---

## Stage 6 â€” Confirmation

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
| `SelectedPupil` | `PupilDto?` | Stage 3 â€” primary `PupilSearch` page |
| `SelectedPupilId` | `string?` | Stage 3 â€” primary `PupilSearch` page |
| `SelectedPupilLabel` | `string?` | Stage 3 â€” primary `PupilSearch` page |
| `MatchedPupil` | `PupilDto?` | Stage 3 â€” match `PupilSearch` page (Merge only) |
| `MatchedPupilId` | `string?` | Stage 3 â€” match `PupilSearch` page (Merge only) |
| `MatchedPupilLabel` | `string?` | Stage 3 â€” match `PupilSearch` page (Merge only) |
| `ReferenceNumber` | `string?` | Stage 3 â€” primary `PupilSearch` page |
| `QuestionAnswers` | `Dictionary<string, QuestionAnswer>` | Stage 3 (question pages) |
| `QuestionHistory` | `List<string>` | Stage 3 (all pages including `PupilSearch`) |
| `OriginCountryCode` | `string?` | Stage 3 â€” any page POST answering `country-originally-from` (see [Conditional question optionality](#conditional-question-optionality-optionalwhen)); also recomputed on Save Draft (Stage 5b) if that page's answers are being saved |
| `OriginCountryLanguages` | `List<string>?` | Stage 3 â€” same as above |
| `SelectedNextStep` | `NextSteps?` | Not used in journey (future) |

`IsSessionReady` checks that `SelectedWhatToChange` and `CheckingWindow` are non-null. Pupil selection is handled in-journey via `PupilSearch` pages and is not required before the journey begins. Any action that fails this check redirects to Check Your Pupil Data.

---

## Blob storage layout

All blobs for a checking window share a container named by the window's GUID:

```
Container: f34d285b-8660-4d12-9c30-787328deaa0a   â† windowId
â”‚
â”œâ”€â”€ request_{referenceNumber}.json                  â† Submitted RequestDocument
â”‚
â”œâ”€â”€ draft_requests/
â”‚   â””â”€â”€ {referenceNumber}.json                      â† Serialised RequestState (draft)
â”‚
â””â”€â”€ evidence-uploads/
    â””â”€â”€ {guid}                                      â† Uploaded PDF bytes (no extension)
```

Question flow configs are **not** in blob storage. They ship inside the release image and are read
from `{ContentRootPath}/Data/QuestionFlows/` in every environment (see
`docs/question-flow-deployment.md`):

```
Data/QuestionFlows/
â”œâ”€â”€ Remove_KS4June.json                             â† Remove flow config
â””â”€â”€ Merge_KS4June.json                              â† Merge flow config
```

---

## Guard rails and failure modes

**Navigation guards** â€” `GetNavigationGuard` prevents URL manipulation to skip pages or jump branches. Attempts to access an out-of-order page redirect to the correct next page. This applies equally to `PupilSearch` pages and question pages.

**Double-submit on confirmation** â€” the idempotency check (`IsSubmittedAsync`) means re-firing the confirmation POST (back button, double click, network retry) is silent and safe. The blob write is also idempotent (`overwrite: true`). The session partial wipe means `IsSessionReady` returns false for subsequent attempts after the first, providing a second layer of defence.

**Draft before submission** â€” if a Draft DB row exists when submission occurs, `UpsertAsync` updates it to Submitted rather than inserting a duplicate. A Draft row therefore never co-exists with a Submitted row for the same reference number.

**Session expiry** â€” sessions have a 30-minute sliding expiry. If a session expires mid-journey, `IsSessionReady` returns false and the user is redirected to Check Your Pupil Data. Any uploaded files remain in blob storage (orphaned) and any saved draft remains accessible via the draft blob and DB row.

