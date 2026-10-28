# Domain model and features

The detailed reference for the entities, features and Application services. `AGENTS.md` has the short version and the rules that are easy to break. Blob paths are in [blob-storage.md](blob-storage.md). The question flow format is in [question-flows.md](question-flows.md).

## Key Entities

**CheckingWindow** — a time-bounded data checking period with a `KeyStage`, `Title`, `StartDate`, and `EndDate`. Entities live in `Persistence/Entities/`. Its `StartDate`/`EndDate` is **derived** as the union of its `CheckingExercises` (#319) — `CheckingWindowDto.DeriveDatesFromExercises`, called by `WindowService` on every create and update. There is no window-level date step in the admin wizard, so the two can never disagree; to move a window's dates, move an exercise's.

**CheckingExercise** — one activity inside a window, on its own dates, owning the datasets it ingests. It carries the validation stamp (`ExerciseValidated`, renamed from `WindowValidated` in #319): `CheckingWindow.Validated` is gone, because a single window-level flag could only ever describe one exercise. The stamp holds the dataset checksums the clean run was taken over, so `CheckingExerciseDto.IsValidated` goes false once a file behind it is replaced — the summary page shows that as "Not since the files changed" rather than as "No". The old window stamp was written unconditionally by both `CreateAsync` and `UpdateAsync` and therefore recorded nothing, which is why its migration backfills nothing.

Since #466 an exercise is identified by its own **`Id`**, not by its kind, and may have a **null
`ExerciseType`**: a display-only data share the admin named (`Name`/`TabName`). The window/type
unique index survives only for legacy rows — it is filtered to `"UsesExerciseStorage" = false`. A
window still holds **at most one exercise of each kind** (one pupil data checking, one results
enquiry), enforced in code (see below), plus unlimited typeless shares. A check constraint,
`"ExerciseType" IS NOT NULL OR ("DisplayOnly" AND "UsesExerciseStorage")`, stops a half-configured
row: no kind is only meaningful for a display-only share on the new storage.

Every kind lookup (`FindExercise(kind)`, `IsOpen`, next steps, close) ignores null-kind rows by
construction — lifted equality means a null kind matches no member — and `FindExercise(null)`
deliberately finds nothing, because "the typeless one" is not a question with an answer.

**`UsesExerciseStorage` decides where an exercise's blobs live.** True (every row created since
#466) means `exercises/{id}/data/` and `exercises/{id}/logs/`, with uploads at
`ingress/{exerciseId}/{datasetId}/{file}`; false keeps the old kind-based paths, which is why the
change needed **no blob migration**. `CheckingExerciseBlobPaths` holds both sets — do not delete the
kind-based ones.

**Every clean run of a new-storage exercise is a release.** The run writes under
`exercises/{id}/releases/{releaseId}/data/`, never over the live output. `PublishReleaseAsync` then
records the release (with a copy of each slot's files) and makes it current in one save. Readers
follow `CurrentReleaseId` (null = the unversioned `exercises/{id}/data/`, where seeders write). An
admin can make an earlier release live (`ExerciseReleaseController`). `ChangeRequest` records
`CheckingExerciseReleaseId`. Neither release pointer is a foreign key (EF cannot order the cycle).
Full design: `docs/checking-exercise-releases.md`.

- **The display reads schemas from `CheckingExerciseDto.PublishedDatasets`** (the current release's
  files, or the complete slots when there is no release), **never** from `DatasetsToIngest`. An
  uploaded-but-unrun schema must not reach schools. Uploads go under a checksum folder, so a
  same-named upload never overwrites a file a release names.
- **A release writes one file per dataset** (`DatasetBlobName`, what the tab reads) plus a **merged
  file from the journey slots only** (what the journeys read). The exception is an exercise with
  **one** dataset that feeds the journey (e.g. KS4 pupil data): the merged file is then that
  dataset's file, so no copy is written and the tab reads the merged file
  (`CheckingExerciseBlobPaths.MergedFileIsDatasetFile`, used by both the run and `ExerciseTabBuilder`).
- **`CheckingWindowDataset.FeedsJourney` is set at creation and never updated**
  (`WindowDatasets.DefaultsFor` / `AddedSlotFeedsJourney`). Data shares and pupil data checking start
  with **no** slots, except KS4 June pupil data, which starts with one `pupils` slot (journey, no
  inclusion stamp: each pupil carries `P_INCL`). A file added to pupil data is merged into the
  journey's pupils data, unless the admin adds it as a **data share** ("What is this file for?" on
  the add-file page, e.g. 16-19 previously published), which is display only. A results enquiry
  starts with its supplier result slots (journey), and a file added to it feeds the journey unless
  the admin adds it as a data share.
- **An added results file stamps its own name as `SourceFile`**, so schools see that name as the
  result's source and a new supplier file needs no code change. Only the default slots stamp a
  `ResultsSources` tag.
- Saving a window keeps every slot and removes only a stale supplier slot of another window type
  (`IsStaleSupplierSlot`).
- Layout is `CheckingExercise.Layout`, set on the exercise edit page. A schema's `x-display.layout`
  is ignored.

**The journeys reach storage through `ICheckingExerciseStorageResolver`.** Pupil data checking and
results enquiry still address "this window, this kind", which no longer identifies a row, so the
resolver maps the pair to one exercise. It **fails closed**: two equally good candidates resolve to
null and the journey finds no data, because serving one release's data under another's journey is
worse than an empty page an admin can see and fix. It considers only **live** exercises
(`CheckingExerciseDto.IsLiveAt`: enabled and inside `VisibleFrom`/`VisibleUntil`).

**`TabOrder` is the only exercise order (there is no `SortOrder`).** It orders the school tabs, the admin pages, the next steps and the deadlines, all through `ExerciseOrder.InTabOrder` (tab order, then id). The wizard gives a kind `WindowExercises.DefaultTabOrder` (pupil data 100, results enquiry 200).

**Every exercise has a `TabName`** (NOT NULL since `RequireExerciseTabName`, which gave each null
row its kind's default name and enabled it). There is no pre-#466 exemption any more.
`WindowExercises.DefaultTabName` names wizard-created exercises ("Pupils"/"Students", "Results"),
and they start **disabled** — schools see nothing until an admin enables the exercise.
**A window never has two exercises of one kind** (`OneExercisePerKind.FindClash`: same non-null
kind, whether or not either is enabled or visible — a disabled duplicate still makes
`FindExercise(kind)` and the kind-keyed admin routes ambiguous). The add/edit exercise pages show it
as a form error on the exercise type, and `WindowService` refuses such a save. A data share has no
kind and never clashes.
`WindowService` refuses only a save that *adds* an exercise to a kind, so a window saved before the
rule with two of a kind can still be saved and sorted out. It also refuses a kind change on an
exercise that is legacy, has a live release, or has change requests
(`IWindowService.KindChangeBlockAsync`, which the edit page asks first to show a form error).

Admin screens are `CreateCheckingExerciseController` (`admin/windows/{id}/exercises/new`),
`EditCheckingExerciseController` (`.../{exerciseId}/edit`) and `ExerciseDataController`
(`.../{exerciseId}/data/new`), all keyed by **id** because a dataset name is only unique within one
exercise and a display-only exercise has no kind to name. Dropping a configured exercise from the wizard
sets `IsEnabled = false` rather than deleting it. To remove one for good, the summary card's
**Delete** link goes to `DeleteExerciseController` (`.../{exerciseId}/delete`, GET confirm then POST,
like `DeleteWindowController`). `IDeleteExerciseService` deletes the row (datasets and releases
cascade), **deletes its change requests** (the admin must tick a box when there are any), clears any
`ReplacesCheckingExerciseId` pointing at it, sets the window's dates again from the exercises left,
then deletes the blobs under `CheckingExerciseBlobPaths.OwnedPrefixes`. An exercise with
`UsesExerciseStorage = false` keeps its blobs: they sit under kind-named prefixes that nothing
marks as its own. Validate, Close and "Send requests for processing" are per exercise
id; **a display-only exercise has no Close or Send requests button**, because a change request only
reaches an exercise through `WhatToChangeCheckingExerciseMap`, which maps kinds, so a data share
can never hold one to close.

**PupilRecord** — read model in `Application/CheckYourPupilData/PupilRecord.cs` for the per-school pupil JSON, matching the **confirmed supplier schema**. The supplier uses UPPER_SNAKE field names that don't map to our C# names by a uniform rule, so every property carries an explicit `[JsonPropertyName]` (e.g. `FORENAME`→`Firstname`, `DOB`→`DateOfBirth`, `P_INCL`→`Pincl`, `ENTRYDAT`→`EntryDate`, `NEWMOBILE`→`NewMobile`). A class-level `[JsonNumberHandling(AllowReadingFromString)]` makes numeric fields bind whether the supplier sends a JSON number or a quoted string (`401` or `"401"`); `NewMobile` uses `NumericBoolJsonConverter` (arrives as `0`/`1`); `DateOfBirth`/`EntryDate` are kept as raw strings because their date format is supplier-defined. `Urn` is a `long`. Unknown JSON fields are ignored. KS2 uses this record too: its schema reads its supplier columns under these names through `x-ingress.source`. Pupils are **not** in the database — they live in blob storage as one JSON file per school (see [Pupil Storage](blob-storage.md#pupil-storage)).

The JSON Schema marks several fields nullable, so the model is hardened for null: `Pincl` is `int?` (null / absent = inclusion flag not supplied, treated as **not included**), and every string property is guaranteed non-null via `NullToEmptyStringJsonConverter` (registered on the pupil `JsonSerializerOptions`; null → `""`) so downstream `StartsWith`/`Contains` search never NREs. `DateOfBirth`/`EntryDate` are stored raw but formatted to `dd/MM/yyyy` for display/CSV via `PupilDateFormatter.ToDisplayDate` (tolerates the schema's ISO `yyyy-MM-dd`, the sample's `yyyy-MM-dd HH:mm:ss.fffffff` timestamp, and the dev seed's `dd/MM/yyyy`; unparseable input is returned unchanged). **Inclusion is the `INCLUDED` stamp, never a code.** Ingress decides it once: from the slot's file of origin (16-19 included and non-included files), or else from the schema's `x-ingress.inclusion` rule (`field`, `included` codes, `descriptionField`, `descriptions`), which also writes `P_INCL_DESC`. The KS4 June schema includes 401/403/414/421/431; the KS2 schema includes 201. `PupilRecord.IsIncluded` and `Post16PupilRecord.IsIncluded` read only the stamp, and so do the exercise tabs (`PupilInclusion.IsIncluded`). No reader knows a key stage's codes. The one exception is `ZendeskConsumer`'s KS4 add-back removal reason (`PupilInclusion.IsKs4Included`), a ticket-routing rule.

**Country** — EF entity in `Persistence/Entities/Country.cs`. Fields: `Id`, `Code` (ISO 3166-1 alpha-2, or ISO 3166-2 subdivision for UK home nations e.g. `GB-ENG`), `Name`, `OfficialName`, `Kind` (`CountryKind` enum: `Sovereign`, `HomeNation`, `CrownDependency`, `OverseasTerritory`). Seeded from the FCDO Geographical Names Index CSV (embedded resource in Persistence) plus hand-written entries for the four UK home nations and three Crown Dependencies (Isle of Man `IM`, Jersey `JE`, Guernsey `GG`). `GB` (United Kingdom) is deliberately excluded — the home nations replace it. `GB-ENG` is kept so the dataset can serve both "destination" and "country of origin" question flows.

**WikiPage / ContentBlock** — CMS content entities with full version history. Changes create new version records rather than updating in place.

**RequestState** — session state for an in-progress change request (`Application/Journey/RequestState.cs`). Holds the selected pupil, checking window, what-to-change, question answers, and question history. For Merge journeys, also holds `MatchedPupil` / `MatchedPupilId` / `MatchedPupilLabel` — the second record being merged. Stored in `ISession` via `SessionExtensions` (Web layer).

**Request statuses (#536).** Two columns, one job each. `ChangeRequest.Status` is what the school
did (`InProgress`, `ReadyToSubmit`, `Submitted`, `Withdrawn`, `NotSubmitted`), written by the web
app (and the sweep's draft step). `ChangeRequest.ProcessingStatus` is where a submitted request is
on its way to a Zendesk ticket: null (undecided) → `Decided` (RulesConsumer, only from null) →
`TicketQueued` (close sweep for amendments; submit for results enquiries) → `TicketCreating` →
`TicketCreated` + `CrmId` (ZendeskConsumer, which claims only `TicketQueued`). `CrmId` is the only
record of which ticket exists. Never mark a request sent anywhere else.

Session readiness for the journey requires only `SelectedWhatToChange` and `CheckingWindow` — the pupil is selected via in-journey `PupilSearch` pages and therefore need not be set before the journey begins.


## Features

**Landing page** (`LandingController` / `LandingPageService`):
- Shows every window that `ICheckingExerciseService.IsWindowShown` says schools see: started (first exercise's `StartDate`), and either an exercise's dates still run **or** a live exercise has a `VisibleUntil` ahead. That second half is the read-only period after the last exercise closes. KS4 June has one exercise, so without it the window vanished at the moment schools lost their actions. The admin sets the period with the exercise's **Visible until**; blank means the window goes at its last end date. The repository (`GetStartedWindowsAsync`) filters only on the start; the service decides the end. `StatusOf` (admin summary) uses the same rule.
- A window past its pupil-data end date shows the "has closed … You can still view and download" banner above its card. There is no separate closed-windows table.
- **A window with no live exercise is hidden completely** — no card and no "no data" or "not for your school" message, because it is not set up yet. `LandingPageService` decides this, not the repository.
- `HasPupilData` means the school has a file in **any** live exercise (`ICheckingDataReader.HasSchoolDataAsync`: the merged file, or any dataset file of the current release). "Your school has no … data" shows only for a live window where every live exercise lacks this school's file.
- Each shown window with data for the school is a DfE card with a Continue button.

**Check Your Pupil Data** (`CheckYourPupilDataController` / `CheckYourPupilDataService`):
- The "what would you like to do?" options come from `INextStepsService.GetAvailableSteps(window.Exercises)` — whatever the **open checking exercises** offer, never a window-type test. `PupilData` → `RequestChange` + `Confirm` (they appear and disappear together); `ResultsEnquiry` → `ResultsEnquiry`. Adding an exercise type means adding a row to `NextStepsService.StepsByExercise`, not a new branch. The POST re-derives the same list and rejects anything outside it, so a hand-crafted post cannot start a journey for a shut exercise.
- Three form states: **many** options → radios (Confirm last, after the `or` divider); **one** → a single button, never a one-item radio group; **none** → no form at all, and a line saying the window is closed for changes. The form sits between the "Download all" link and the tables (#641). The tables, search and downloads always render — a closed exercise removes actions, never content.
- The deadline sentence uses `EndDateFor(exercises, PupilData)`, never the outer window's `EndDate` (months later on a multi-exercise window). It flips to past tense from `IsOpen(exercises, PupilData)`. `NextStepLabels` (Web) holds the on-screen wording.
- The controller holds **no `TimeProvider`** — every "is it open" question goes through `ICheckingExerciseService`.
- Displays paged, searchable pupil lists for a given checking window and school (by `laestab`).
- `CheckYourPupilDataRepository` loads the school's pupil JSON (cached) and filters/searches/sorts/pages **in memory** — sorted by surname/firstname. **The pupil search boxes share one rule, `PupilSearchTerms`** (parse the query once with `Parse`, then `Matches` per row): the query splits on spaces, tabs and commas, and each word must match the start of a word in some field (words start after a space, tab, hyphen or apostrophe), ignoring case, in any order ("John Smith", "Smith, John"; "ohn" finds nothing). Identifiers match from their start too ("A860" finds a UPN, "8604" does not). The journey autocomplete checks names, UPN/ULN, CYPMD ID and displayed DOB; the pupil tabs check names; the results table checks names, CYPMD ID and subject; the schema-driven tabs check the schema's `searchable` columns. Only the Add-pupil duplicate check (fuzzy on purpose, not a search box) does not use it.
- Pupils are split into included (`IsIncluded`, the `INCLUDED` stamp) and non-included (all others) tabs. A second stamp, `INCLUDABLE` (#711), says who a school may ask to include; only the Include journey reads it.
- **Since #466 the tabs can be exercise-driven.** `IExerciseTabBuilder` (Application) returns one
  tab per *visible* exercise — enabled, within `VisibleFrom`/`VisibleUntil` (exclusive at the end),
  and every exercise has a `TabName`. When no exercise is visible the list is empty and the page
  renders the inclusion tabs above. That fallback is still live code in the `else` branch of
  `Index.cshtml`; removing it is a separate decision.
- **The schema decides the layout, not the tab name or the kind.** `IExerciseDisplayService`
  (Application) parses each uploaded schema's `x-display` / `x-csv` / `x-ingress` and returns
  view-ready records: `x-display.layout: "vertical"` pivots one record per school to label/value
  rows, anything else is a searched, paged table, and an exercise with no schemas falls back to a
  raw column-per-key table. A mix of layouts on one exercise renders as a table and logs a warning.
  All of this lives in Application — the controller reads session, calls the service, returns the
  view. Do not move it back into Web.
- **A summary list is paged by group, not by row.** A schema's `x-display.groups` (`[{key, title}]`,
  in show order) and each property's `x-display.group` split the one record into sections, shown
  as a GOV.UK multi-page guide: contents list, the current group (`section` query value), block
  pagination labelled with the group titles. A group with no field in that file is left out. If
  any visible field names no declared group, the schema is shown ungrouped on one page, so a typo
  never hides a field; `ShippedSchemaGroupTests` checks the shipped schemas. The KS2 groups are the
  supplier's website headings; the 16-19 groups were proposed from the labels.
- `ExerciseDisplayService.DatasetFor` resolves which dataset a row belongs to by `DATASET`, then
  then `INCLUDED`, then best field match — and **refuses to guess on a tie**. Picking
  the first would show a school another dataset's records; a test pins the refusal. It names no
  supplier column: a row is placed by the fields it fills.
- **A table sorts its rows by its visible columns, in the schema's display order**
  (`ExerciseDisplayService.BuildTable`), so the page and its CSV download share one order and the
  code names no field.
- **`ExerciseLayout.InclusionTabs` splits one exercise into two tabs**, "{TabName} Included" and
  "{TabName} Non Included", by `PupilInclusion.IsIncluded` (the `INCLUDED` stamp only). It is
  for pupil data whose one supplier file carries each pupil's own code (KS4); Post16 stays `Table`. KS2's file carries the code
  too, but schools see one Pupils tab and one CSV, so KS2 is `Table`: the code still stamps `INCLUDED`, which is what limits
  removal to included pupils. A KS4 June pupil-data exercise created by the wizard starts with it
  (`WindowExercises.DefaultLayout`); every other kind starts as `Table`. `ExerciseTab.Key` identifies a tab (not the exercise id), and
  the page's `tab` query value keeps a search or page change on the tab it was made on.
- An exercise with no data for this school **still draws its tab**, saying there is nothing. A
  vanishing tab tells a school nothing; an empty one says the exercise exists and holds nothing yet.

**Journey / Change Request** (`JourneyController`):
- Multi-step question flow for submitting a pupil data change request.
- Session state (`RequestState`) tracks answers, page history, selected pupil(s), and reference number.
- Pupil selection is an in-journey `PupilSearch` page (the first page of every flow config). `WhatToChangeController` redirects to `Journey.Page(firstPageId)` after saving the window and `WhatToChange` to session. The journey's `IsSessionReady` check requires only those two fields.
- **What to change offers a change only when its flow file exists** (`IQuestionFlowService.GetPupilDataChangesAsync`: Merge, Include, Remove, Add, in that order, each only when `{WhatToChange}_{CheckingWindowType}.json` exists). The view renders `WhatToChangeViewModel.AvailableChanges` and names no change or window type; the POST refuses any change outside the same list. To give a window type a journey, add its flow file. There are no window-type lists.
- On submission, `IRequestService.ConfirmRequestAsync` builds the `RequestDocument` (including an optional `MatchedPupil` for Merge) and saves it to blob storage (container named by `windowId`, blob `request_{referenceNumber}.json`). The close sweep sends this saved copy.
- The Summary page shows "First record to merge" / "Second record to merge" rows (with Change links to the respective `PupilSearchPage`) when `MatchedPupil` is set; otherwise shows the standard "Pupil name" row.

## Application Services

**`IQuestionFlowService`** — config and navigation
Owns the `QuestionFlowConfig` loaded from the shipped flow files and all logic that operates on its structure: fetch a page, determine the next page from answers, guard whether navigation to a page is valid (`GetNavigationGuard`), build a CMS content key. All methods take a `QuestionFlowConfig` as a parameter.

**`IJourneyValidationService`** — in-journey input validation and reference generation
Stateless rules applied while the user moves through the journey: check whether a question is answered (`IsAnswered`), validate a question answer, validate a file upload against the page limit, enforce a page's `requireAtLeastOne` rule (`ValidateRequireAtLeastOne` → `RequireAtLeastOneResult`), generate a reference number. Does not touch config or session state.

**`IOptionVisibilityService`** — conditional radio option filtering
`GetVisibleOptions(question, ctx)` returns the subset of a question's options that should be shown for the given `JourneyConditionContext`, evaluating each option's `visibleWhen` against the registered `IJourneyCondition` set (fail closed for unknown conditions). See [Conditional option visibility](#conditional-option-visibility-visiblewhen).

**`IRequestService`** — change request lifecycle
Two operations: `ConfirmRequestAsync` resolves the flow config, builds a `RequestDocument` (written to blob or queued), upserts a `ChangeRequest` DB row, and persists the full `RequestState` (stamped with `SubmittedByEmail`/`SubmittedAt`) to the requests blob so the read-only view can rebuild the summary without the `RequestDocument`. `SaveDraftAsync` writes the `RequestState` to the same requests blob and upserts the row with the draft status. Both use `IRequestStateBlobClient`.

**`ISubmittedRequestService`** — read-only view of a submitted request
`GetAsync(windowId, referenceNumber)` loads the persisted `RequestState` via `IRequestStateBlobClient.GetAsync` plus its `QuestionFlowConfig` and projects a `SubmittedRequestView` (Q&A rows, evidence files, merge displays, and the "Submitted by" email/when/reference). It never reads the `RequestDocument` (saved at submit as `request_{ref}.json` for the close sweep). Served by `SubmittedRequestController` → `Views/SubmittedRequest/View.cshtml` (the "View" link on the Amendment Requests submitted-requests grid).

**`ICheckingExerciseService`** — is this checking exercise open?
The only place in the solution that compares a checking exercise's dates against the clock. `IsOpen(exercises, type)`, `OpenCheckingExercises(exercises)` (in tab order, `ExerciseOrder.InTabOrder`) and `EndDateFor(exercises, type)` all read the `IReadOnlyList<CheckingExerciseDto>` they are given. It takes the list, not a window DTO, because two unrelated classes are named `CheckingWindowDto` (LandingPage and WindowManagement) and the second carries its own `IsOpen` property. Time comes from an injected `TimeProvider` (`GetLocalNow()`) and is never accepted from a caller. **Fails closed:** no row for that type, or an empty list, means closed — but closed means "no actions", never "no content", so an empty `OpenCheckingExercises` must not hide a card or a page. No branch in the service may depend on `CheckingWindowType`. `IsWindowShown(exercises)` decides whether schools see a window at all (see **Landing page**). Both window read paths carry the list: `LandingPageRepository.GetStartedWindowsAsync` and `CheckYourPupilDataRepository.GetCheckingWindowAsync` project `Exercises` onto the LandingPage `CheckingWindowDto`; `WindowRepository` already did for the WindowManagement one.

**Journey entry points are gated on the exercise (#318).** Every way into a journey asks `IsOpen` before doing anything: `JourneyController.IsSessionReady` (which every journey action already ran), `WhatToChangeController` and `ConfirmCorrectController` on `PupilData`, `ResultIssueController` on `ResultsEnquiry`, and `CheckYourPupilDataController`'s next-steps POST via `INextStepsService`. `AmendmentRequestsController.Edit` blocks resuming a draft whose exercise has closed — the decision is block the resume, not the submit, so nobody edits a request that could never be sent.

The journey's exercise is **derived** from `RequestState.SelectedWhatToChange` through `WhatToChangeCheckingExerciseMap.CheckingExerciseFor` (`Application/WindowManagement/`, moved there in #320 because it maps every `WhatToChange` member, not just the enquiry one; returns `CheckingExerciseType`), never stored on the session — a stored copy can disagree with the journey's own change type. A new exercise type is therefore gated by adding a row to that map, without touching `IsSessionReady`.

**No gated path returns 404.** Rejections redirect to Check your pupil data and `ClosedExerciseGuard` (Web, beside `NextStepLabels`) stashes the message that `_ClosedExerciseBanner.cshtml` renders there. `MessageFor` has **no default case**, same rule as `CheckingExerciseBlobPaths`; `ClosedExerciseGuardTests` pins that every `CheckingExerciseType` has its own wording, so a new type cannot reach a user with a throw.

**Amendment Requests deadlines are per exercise (#320).** `AmendmentRequestsResult.Deadlines` carries one `ExerciseDeadlineDto` per exercise in tab order, each with its own `EndDate` and its own `IsOpen` from `ICheckingExerciseService`. There is no `WindowEndDate` — the outer window's end is the union of its exercises (#319), so on a 16-19 window it is the results-enquiry close months after pupil data shuts, and reading it told a school it still had time to amend. The page prints one sentence per exercise and the Requests tab lists only the open ones. **The grid itself stays unsplit** — both populations share one table, one set of checkboxes and one bulk submit; only the deadline is per exercise. The bulk-submit confirmation banner is the exception that reads a single exercise: it offers *another amendment*, so it quotes `EndDateFor(..., PupilData)` and is dropped entirely (`WindowCloseLabel` null) on a window with no pupil-data exercise.

**A journey's config key does not name the exercise.** It stays `{WhatToChange}_{CheckingWindowType}.json`. The trigger to add the exercise as a third axis, and everything that must move with it, is recorded in `docs/16-19-window-model.md` — do not re-run that decision from scratch.

**`ICloseExerciseService`** — close one checking exercise
`CloseAsync(windowId, exercise)` sends that exercise's decided amendments (`ProcessingStatus = Decided`)
to the Zendesk queue from the `RequestDocument` saved at submit, sets them `TicketQueued`, reports
undecided ones as waiting, and cancels its `InProgress`/`ReadyToSubmit` drafts;
`PreviewAsync` returns the same counts without writing, for the confirmation page. Replaces
`AdminRequestsService.ProcessCloseWindowEvent`, which swept **every open window at once** and so
could not be driven from a per-window page — `AdminRequestsService` is now only the requests-list
reader it is named for.

Rules the move settled:

- **Scoped to one window AND one exercise row.** Rows match on `ChangeRequest.CheckingExerciseId`
  against the exercise id the admin pressed Close on (a window may hold several releases of one kind). A row with a **null** stamp
  matches nothing and is deliberately left alone — the FK is `onDelete: SetNull`, so such a row was
  orphaned or belongs to a window that never ran the mapped exercise. It stays
  `Submitted` and `Decided` and visible on the Requests page rather than being sent under a guess.
- **It ignores the exercise's dates.** There is no `TimeProvider` and no date predicate: an admin
  may close an exercise whose end date has not passed. The date rules live in the controllers
  around it: Close is offered only while the exercise is open, Send requests only once it has closed.
- **Amendments only.** A results enquiry is queued at submit (`TicketQueued`), and a ConfirmCorrect
  declaration never goes to Zendesk, so the `Decided` filter leaves both out. Add requests are swept
  like any amendment, because they go through the Rules Engine (`AddPupil` outcome).

Driven by `CloseExerciseController` (`admin/windows/{id}/exercises/{exerciseId}/close`, GET confirm
then POST), offered only while that exercise is open (AB#301022: it first ends the exercise early
through `IExerciseEarlyClosureService`, then sweeps). Once it has closed, the summary offers
`SendExerciseRequestsController` (`.../exercises/{exerciseId}/send-requests`) instead. The old disabled "Process Close Window" button on the Amendment
Requests page is removed.

**`ICheckYourPupilDataService`** — pupil data queries (partial)
`GetPupilSuggestionsAsync(windowId, query, PupilFilter filter, Guid? excludeId, bool requireResults)` returns up to 10 `PupilSuggestionDto(Id, Label)` records. `PupilFilter.Included` restricts to pupils stamped `INCLUDED`; `PupilFilter.All` returns all pupils for the school; `PupilFilter.Includable` restricts to pupils stamped `INCLUDABLE` and `PupilFilter.NotIncludable` to the rest (#711). `excludeId` removes a specific pupil from results (used by the match-pupil page to prevent selecting the primary pupil again). `requireResults` restricts the search to students in `IResultsClient.GetCypmdIdsWithResultsAsync` (the same cached school results file the enquiry itself reads), and is passed down to `SearchPupilsAsync` as a CYPMD id allow-list so it applies **before** the ten-suggestion cap — filtering afterwards would drop the one student who holds results whenever ten who do not sort ahead of them. Served by `PupilSuggestionsController` at `GET /pupils/suggestions`. The `WhatToChange?`-based overload no longer exists — callers must pass an explicit `PupilFilter`.

**`ICountryService`** — country/autocomplete lookup
`SearchAsync(query)` returns up to 10 `CountrySuggestionDto(Code, Name)` records matching the query against `Country.Name` (case-insensitive `ILike`). Used by `CountryController.Suggestions` to serve the `Autocomplete` question type. The underlying `Countries` table is seeded from the FCDO Geographical Names Index and is not checking-window-specific.


## Other patterns

**Authentication:** Controlled by config. Local dev uses `AddFakeSignInAuthentication()` (hardcoded claims). Production uses `AddDfeSignInAuthentication()` (OpenID Connect against DfE SignIn).

**Database:** PostgreSQL via EF Core with Npgsql. Fluent API configurations live in `Persistence/Configurations/`. Local dev uses docker-compose for the database.

**DTO Mapping:** Uses Mapperly (source-generation) for `WikiPage` and `ContentBlock` mappers (`Persistence/Mappers/`). CheckingWindow projections (EF) and `PupilRecord` → pupil DTO mapping (in-memory LINQ) both use plain `.Select()` in the repository — do not introduce Mapperly for these.

**Blob clients:** Each blob concern has its own Application interface and Infrastructure implementation. All follow the same pattern: a `BlobServiceClient` singleton, a fixed container name, and `CreateIfNotExistsAsync` before writes. Current blob clients: `IRequestBlobClient` (submitted `RequestDocument`), `IRequestStateBlobClient` (full `RequestState` for drafts **and** submitted requests, blob `requests/{referenceNumber}.json`), `IFileStorageService` (uploaded evidence files), `IPupilDataBlobClient` (per-school pupil JSON, container `{windowId}`, blob `data/{laestab}_pupils.json`).

**Queue Processing:** The `RulesEngineWorker` communicates via Azure Storage Queues (emulated locally with Azurite from the docker-compose stack). The same `AzureStorage` connection string is used for both queues and blobs.

**Frontend:** GOV.UK Frontend ASP.NET Core (`GovUk.Frontend.AspNetCore`) for all UI components. Markdown content is rendered via Markdig and sanitised with HtmlSanitizer.

**Window admin wizard (#319).** Title → window type → a date page per default exercise → check answers. **There is no exercise step:** choosing the type gives the draft `WindowExercises.DefaultsFor(windowType)` (`CheckingWindowDraft.UseDefaultExercises`, which keeps the dates of an exercise the new type also runs), and check answers has no Change link on the exercises row. An admin adds other exercises after the window is created, from its summary page. There is no key stage step: `WindowKeyStage.For(windowType)` derives it, and `WindowService` sets it on every create and update (`CheckingWindowDto.DeriveKeyStageFromWindowType`), so changing a window's type moves its key stage with it. There is no "which exercises" page: on an existing window an admin adds an exercise (`CreateCheckingExerciseController`), deletes one (`DeleteExerciseController`), or switches one on and off with its Enable box. The wizard creates only kind-bearing exercises, each with its kind's default tab name and **disabled**; a display-only data share is added afterwards through `CreateCheckingExerciseController`. `WindowExercises` decides what a new window starts with; the admin may add or delete any kind on the existing window, which is how KS4 Autumn can be given a results enquiry without a code change.

Each exercise's dates are captured on **one** page (`ExerciseDatesController` / `ExerciseDates.cshtml`), both ends together. `StartDateController` and `EndDateController` no longer exist. Editing an existing window's exercise — its name, tab name, tab order, visibility, layout, display-only flag, the exercise it replaces, and its dates — is `EditCheckingExerciseController` (`admin/windows/{id}/exercises/{exerciseId}/edit`), beside `CreateCheckingExerciseController` and `ExerciseDataController`.

**Schema, ingress and validate are all per exercise.** Their routes name it — `admin/windows/{id}/{exercise}/schema-file/{dataset}`, `.../ingress-file/{dataset}`, `.../validate` — because a dataset name is only unique within one exercise. `ValidateWindowController` runs **one exercise per request** rather than looping: a loop would emit several terminal events down a progress stream whose client expects one, and would stop an admin revalidating a single exercise after swapping one of its files. `CheckingWindowDto.AllDatasets` is retired; use `FindExercise(type).Datasets`.

