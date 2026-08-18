# 16-19 window: two checking exercises inside one window

*Design note by Dave Gouge, 2026-08-13. Terminology updated 2026-08-18: the window's child items
are now called **CheckingExercises** (not activities), per the lead developer's review comment on
the AB#296648 PR.*

## Context

The 16-19 checking window is really two windows in one:

| | Runs | What the user can do |
|---|---|---|
| Outer — 16-19 Results Enquiry | 7 Oct to March | Results enquiry journeys |
| Inner — Pupil data checking | 7 Oct to 18 Oct | Pupil data change journeys |

The user sees **one** "16-19 Data" card on the landing page. Inside it, results enquiry is always
available. Pupil data journeys stop when the inner window closes on 18 Oct.

KS4 Autumn repeats the same pattern: pupil data checking plus KS4 results enquiry.

The code cannot express this today:

- `CheckingWindow` (`Persistence/Entities/CheckingWindow.cs:9-11`) has exactly one
  `StartDate`/`EndDate` pair.
- `LandingPageRepository.GetOpenWindowsAsync` brackets `now` against that single pair, so a window
  is either fully open or fully closed.
- There is **no results-enquiry concept anywhere in the repo**. A repo-wide grep for
  "results enquiry" / `ResultsEnquiry` returns nothing.
- `WhatToChange` (`Application/CheckYourPupilData/WhatToChange.cs`) is a flat enum
  (`Merge`, `Include`, `Remove`, `Add`) and the options are hardcoded in
  `Views/WhatToChange/Index.cshtml`.
- Journey configs key off `{WhatToChange}_{CheckingWindowType}.json`
  (`IQuestionFlowService.GetConfigAsync`), so every journey belongs to a change type, not to a
  checking exercise.

**Deliverable: a design note only.** No code changes in this piece of work. The note describes the
two-level window model and how KS4 Autumn reuses it. Implementation is planned separately.

Agreed decisions (from this session):

1. After 18 Oct the Check your pupil data page stays **read-only**. Pupil list, search, CSV and ZIP
   downloads all keep working. Only the change journeys disappear.
2. The card still lands the user on **Check your pupil data**. That page offers the two checking
   exercises.
3. The model is a **checking-exercise child collection** on `CheckingWindow`.

---

## The model

`CheckingWindow` keeps its outer `StartDate`/`EndDate`. It gains a `CheckingExercises` child
collection. The existing `Datasets` collection **moves down onto the checking exercise**: a dataset
belongs to the exercise that consumes it, not to the window.

```
CheckingWindow
  Id, KeyStage, CheckingWindowType, Title
  StartDate 07 Oct    EndDate 31 Mar      <- outer, the union of all checking exercises
  CheckingExercises [
    { PupilData,      07 Oct - 18 Oct,
        Datasets [ included, nonincluded ] },
    { ResultsEnquiry, 07 Oct - 31 Mar,
        Datasets [ ... ] }
  ]
```

- KS4 June and KS2 get **one** exercise row, `PupilData`, with the same dates as the window. Their
  behaviour does not change.
- 16-19 and KS4 Autumn get **two** rows.
- The outer pair is the **only** thing that decides whether the window shows a card. It must equal
  the union of the exercise dates.
- A window that is open but has **no** checking exercise open still shows its card. The user goes
  into a read-only page: pupil list, search, CSV and ZIP downloads, and no action options at all.
  Exercise state controls actions, never visibility.

Why this shape and not the alternatives:

- A second date pair on `CheckingWindow` (`PupilDataEndDate`) hardcodes "exactly two phases, one of
  which is pupil data". A third checking exercise later needs another migration.
- Two linked `CheckingWindow` rows (`ParentWindowId`) would reuse the window machinery per row, but
  every `windowId`-keyed thing would then have to pick the right row: the session `RequestState`,
  the blob container named by `windowId`, `ChangeRequest`, and the pupil data blobs. That is a large
  blast radius for no gain.
- The `Datasets` collection already set the precedent for child rows on `CheckingWindow`. This
  follows it, and then reparents `Datasets` onto the checking exercise — see below.

### New types

```csharp
// Domain/Enums/CheckingExerciseType.cs
public enum CheckingExerciseType
{
    [Display(Name = "Pupil data checking")]  PupilData,
    [Display(Name = "Results enquiry")]      ResultsEnquiry
}
```

```csharp
// Persistence/Entities/CheckingExercise.cs
public sealed class CheckingExercise
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; set; }
    public CheckingExerciseType ExerciseType { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int SortOrder { get; init; }
}
```

Mirror `CheckingWindowDatasetConfiguration` for the `IEntityTypeConfiguration`. Store the enum as a
string, as `CheckingWindow` does for `KeyStage` and `CheckingWindowType`.

### Datasets belong to the checking exercise

`CheckingWindowDataset.CheckingWindowId` becomes `CheckingExerciseId`. `CheckingWindow` no longer
holds `Datasets` directly; it reaches them through `CheckingExercises`.

This is the right home for them. A dataset is an input to one checking exercise. The two 16-19
pupil CSVs feed pupil data checking. Results enquiry will have its own inputs, on its own dates,
validated against its own schemas. Hanging both off the window would put unrelated files in one
flat list with nothing to say which exercise each serves.

Three consequences fall out of the move:

1. **Ingress runs per checking exercise, not per window.** `16-19-pupils-plan.md` requires the two
   16-19 pupil CSVs to be ingested in a **single run**, because a second run would wipe the first
   run's output. That constraint now applies **within a checking exercise**. Two exercises are two
   independent runs, which is what you want — results-enquiry data must not have to be re-uploaded
   to correct a pupil file.

2. **The blob layout must become exercise-scoped.** `CsvSchemaFileProcessor` writes
   `data/{schoolId}_pupils.json` (`:300`) and its clear sweep deletes the whole `data/` prefix
   (`:411`). If two checking exercises ingest into the same `{windowId}` container, the second run
   destroys the first exercise's output. Give each exercise its own prefix — `{windowId}` container,
   `{exercise}/data/{laestab}.json` — and scope the sweep to that prefix. Pupil data keeps its
   current path shape under the new prefix.

3. **`HasPupilData` becomes exercise-scoped.** The landing page existence check
   (`IPupilDataBlobClient.HasPupilDataAsync(windowId, laestab)`) must ask about the pupil-data
   exercise's prefix. Note this is now only about whether the read-only pupil content can render —
   it no longer decides whether a card appears.

Also per-exercise: the window admin wizard's schema and ingress steps, and `WindowValidated` — a
window is not validated as a whole any more, each checking exercise is.

### Migration

Add **new** migrations. Never amend a shipped one — `AddCheckingWindowDatasets` shipped on
2026-07-28 and has five migrations after it, so it is live.

1. Create `CheckingExercises`. Backfill one `PupilData` row per existing window, copying that
   window's `StartDate` and `EndDate`.
2. Add `CheckingExerciseId` to `CheckingWindowDatasets` and point every existing row at its
   window's backfilled `PupilData` row. Drop `CheckingWindowId` in a **later** migration, once the
   readers have moved, so a rollback is safe. This mirrors how the legacy scalar
   `IngressFile`/`SchemaFile` columns were left in place for a release.

Existing blobs sit at the old unprefixed paths. Either move them as part of the release or have the
reader fall back to the old path when the prefixed one is absent. Decide before implementation —
this is the one step that is not purely additive.

---

## Where "is this checking exercise open" is answered

One rule, in one place, in Application. Nothing else may compare dates.

```csharp
// Application/WindowManagement/ICheckingExerciseService.cs
bool IsOpen(CheckingWindowDto window, CheckingExerciseType exercise, DateTime now);
IReadOnlyList<CheckingExerciseType> OpenCheckingExercises(CheckingWindowDto window, DateTime now);
DateTime? EndDateFor(CheckingWindowDto window, CheckingExerciseType exercise);
```

Fail closed: a window with **no** checking-exercise row for a type is closed for that type. A
window whose exercise list is empty is closed for everything. That way a half-configured window
cannot open a journey by accident.

Fail closed applies to **actions only**. `OpenCheckingExercises` returning an empty list must never
remove the card or the pupil data. The read-only content is always available for the whole outer
window.

Time comes from the injected `TimeProvider`, as `LandingPageService` already does
(`timeProvider.GetLocalNow()`). Do not call `DateTime.Now`.

The window DTO that reaches the Web layer must carry the checking-exercise list, so the repository
projection in `LandingPageRepository` and the `CheckYourPupilData` window read both need the extra
`.Select`.

---

## What changes on screen

### Landing page

No change to the card itself. The window appears while the **outer** pair brackets `now`, whether or
not any checking exercise is open. The card title is the window title ("16-19 Data").

Consider showing the pupil-data deadline on the card as a hint while that exercise is open. Confirm
with content design; it is not required by the model.

### Check your pupil data (`Views/CheckYourPupilData/Index.cshtml`)

This page already ends with a `NextSteps` radio group, not buttons:

```
( ) Request a change to pupil data
        or
( ) Confirm pupil data is correct
```

`CheckYourPupilDataController:110-111` routes `RequestChange` to `WhatToChange` and `Confirm` to
`ConfirmCorrect`.

Four changes:

1. `NextSteps` gains `ResultsEnquiry`, routed to the results-enquiry entry point.
2. The controller builds the visible option list from `OpenCheckingExercises(...)`. `RequestChange`
   and `Confirm` both belong to `PupilData` and both disappear together when it closes.
   `ResultsEnquiry` appears only while that checking exercise is open.
3. If only one option survives, do not render a one-item radio group. Render a single button
   instead — a radio group with one choice is a poor pattern and fails the "select one option" hint.
4. If **no** option survives, render no form at all. The page becomes read-only: the tables, search
   and downloads stay, and a short statement says the window is closed for changes. Everything above
   the form is unchanged, so this needs no new page and no redirect.

The deadline sentence at the top of the page currently reads:

> You must request any changes to pupil data before `@Model.WindowEndTime` on `@Model.WindowEndDate`

`WindowEndTime` / `WindowEndDate` come from the **outer** window. For 16-19 that would show March,
which is wrong — the pupil-data deadline is 18 Oct. They must come from
`EndDateFor(window, PupilData)`. After that date the sentence must change to a past-tense statement
that the pupil data window has closed.

Keep this in London time, per the existing display convention.

### Server-side gating

The radio list is presentation. The gate must also sit on the POST paths, because a user can hold a
bookmarked URL or a stale tab across the 18 Oct boundary:

- `CheckYourPupilDataController` next-steps POST — reject a closed checking exercise.
- `WhatToChangeController.Index` and `.Confirm` — both require `PupilData` open.
- `JourneyController` — its existing `IsSessionReady` guard runs on every action
  (`JourneyController.cs:39, 69, 93, 237, 454, 539, 573, 610, 630, 693`). Extend that one helper to
  also require the journey's checking exercise to be open. One change covers every journey action.
- `ConfirmCorrectController` — same gate.

A rejected request should redirect back to Check your pupil data with an explanation, not 404.

---

## How a journey knows its checking exercise

Today a journey is identified by `WhatToChange` plus `CheckingWindowType`, and the config blob is
`{WhatToChange}_{CheckingWindowType}.json`.

Results enquiry journeys are a different **checking exercise**, not a different change type. Two
options, both compatible with the model above. Pick when the results-enquiry flows are specified:

**A — new `WhatToChange` members, with a checking-exercise attribute.** Add e.g.
`WhatToChange.ResultsEnquiry`. Map each member to its checking exercise in one lookup in
Application. The config naming rule is untouched. Smallest change; works while results enquiry is
one journey.

**B — the checking exercise becomes part of the config key.** The blob becomes
`{Exercise}_{WhatToChange}_{CheckingWindowType}.json`, with the existing files treated as
`PupilData_*`. Cleaner if results enquiry grows several distinct journeys.

Either way `RequestState` should carry the checking exercise so `IsSessionReady` can gate on it,
and so `ChangeRequest` rows and the Amendment Requests grid can tell the two populations apart.

---

## Open questions

These do not block the model. Resolve them before implementation.

1. **Drafts across the boundary.** A user saves a pupil-data draft on 17 Oct and returns on 19 Oct.
   `AmendmentRequestsController.ResumeDraft` (`:117`, `:172`) would rebuild a journey for a closed
   checking exercise. Options: block resume with a clear message, or allow resume but block submit.
   Product decision. The gate must be in `IsSessionReady` either way.
2. **Are results enquiries pupil-centric?** If a results enquiry starts by picking a pupil, it can
   reuse the `PupilSearch` page type and the pupil blobs. If it starts from a qualification or a
   result, it needs a new data source that does not exist yet. This is the largest unknown in the
   whole feature.
3. **Amendment Requests grid.** Does it show both checking exercises' requests in one list, or
   split them?
4. **Window admin wizard.** The exercise dates need a step. Suggest: for a window type with more
   than one checking exercise, the wizard asks for each exercise's dates and derives the outer pair
   as their union, so the two can never disagree.
5. **KS4 Autumn dates.** Confirm its inner and outer dates. The model assumes they nest the same
   way as 16-19.

---

## Plan

This note lives at `docs/16-19-window-model.md`. Its companion notes `16-19-pupils-plan.md` and
`16-19-reuse-investigation.md` are on the original design branch.

Reconcile `16-19-pupils-plan.md`. Two of its statements are superseded by this note:

- Its step 2 puts the dataset collection on `CheckingWindow`. Datasets now hang off the checking
  exercise.
- Its step 1 treats "one ingress run per window" as the unit. The unit is now the checking
  exercise, and `clearExistingFiles` must be scoped to the exercise's blob prefix rather than all
  of `data/`.

Its "Deliberately out of scope" section also defers 16-19 journey configs. This note is where the
checking-exercise model for those now lives.

No code changes. No migration. No tests.

## Verification

- Read the note back and check every file path and line anchor still resolves.
- Check the note against `16-19-pupils-plan.md` for contradictions, in particular the
  `CheckingWindow` shape, since both notes add a child collection to the same entity.
