# Checking exercises: many activities inside one checking window

*Design note — Dave Gouge, 2026-08-19.*

## Context

A checking window currently means one thing happening over one date range. 16-19 breaks that
assumption: it runs two activities, on two different ranges, behind one card.

| | Runs | What the user can do |
|---|---|---|
| Outer — 16-19 Results Enquiry | 7 Oct to 31 Mar | Results enquiry journeys |
| Inner — Pupil data checking | 7 Oct to 18 Oct | Pupil data change journeys |

The user sees **one** "16-19 Data" card on the landing page. Inside it, results enquiry stays
available all the way to March. Pupil data journeys stop when the inner window closes on 18 Oct.

KS4 Autumn is the same shape: pupil data checking plus a results enquiry.

This note calls each of those activities a **checking exercise**, and describes how a window comes
to hold several of them. It is a design note. Implementation is ticketed separately in
`16-19-window-model-tickets.md`.

### What the code does today

The results-enquiry journey exists and works. What does not exist is any notion that it and pupil
data checking are separate activities with separate dates.

- `CheckingWindow` (`Persistence/Entities/CheckingWindow.cs:9-11`) has exactly one
  `StartDate`/`EndDate` pair, and one `Validated` stamp for the window as a whole.
- `LandingPageRepository:45` brackets `now` against that single pair, so a window is either fully
  open or fully closed.
- A journey is identified by `WhatToChange` plus `CheckingWindowType`, and its config blob is
  `{WhatToChange}_{CheckingWindowType}.json`. The enquiry journey is `IncorrectGrade_Post16.json`.
- `WhatToChangeCheckingExerciseMap` already maps each `WhatToChange` member to the exercise it
  belongs to, but returns `const string` values because there is no exercise type to return.
- Whether the enquiry option appears is a **window-type test**: `CheckYourPupilDataController:202`
  returns `windowType == CheckingWindowType.Post16`, surfaced as `ShowResultsEnquiryOption` and
  re-checked on the POST at `:103-104`. Both sites are marked `PARKED` against this note.

Two consequences follow from that last point, and they are the reason this work exists:

1. **The enquiry option shows for the whole outer range, and so do the pupil-data options.** Nothing
   can express "pupil data closes on 18 Oct while results enquiry runs to March".
2. **KS4 Autumn gets no enquiry option at all**, because the test names `Post16` specifically.

### Agreed decisions

1. When a checking exercise closes, its **actions** go and its **content stays**. For pupil data
   that means the list, the search, the CSV download and the ZIP download all keep working after 18
   Oct. Only the change journeys disappear.
2. The card still lands the user on **Check your pupil data**. That page offers whichever exercises
   are open.
3. The model is a **checking-exercise child collection** on `CheckingWindow`.

---

## The model

`CheckingWindow` keeps its outer `StartDate`/`EndDate` and gains a `CheckingExercises` child
collection. The existing `Datasets` collection **moves down onto the exercise**: a dataset belongs
to the exercise that consumes it, not to the window.

```
CheckingWindow
  Id, KeyStage, CheckingWindowType, Title
  StartDate 07 Oct    EndDate 31 Mar      <- outer, the union of all checking exercises
  CheckingExercises [
    { PupilData,      07 Oct - 18 Oct,
        Datasets [ included, nonincluded ] },
    { ResultsEnquiry, 07 Oct - 31 Mar,
        Datasets [ the six results files ] }
  ]
```

The collection is sized by data, not by code:

- A window type with one activity gets one row. KS2 and KS4 June get a single `PupilData` exercise
  on the window's own dates, and nothing about them changes.
- A window type with several gets several rows. 16-19 and KS4 Autumn get two. A third exercise later
  is a row, not a migration.

Three rules hold the model together:

- The outer pair is the **only** thing that decides whether a window shows a card. It must equal the
  union of its exercise dates.
- A window that is open but has **no** exercise open still shows its card, and still shows its
  content. Exercise state controls actions, never visibility.
- Nothing outside one Application service compares an exercise's dates to the clock.

### Why this shape

- **A second date pair on `CheckingWindow`** (`PupilDataEndDate`) hardcodes "exactly two phases, one
  of which is pupil data". A third exercise needs another migration, and KS4 Autumn's shape has to
  be inferred from which columns are null.
- **Two linked `CheckingWindow` rows** (`ParentWindowId`) would reuse the window machinery per row,
  but every `windowId`-keyed thing would then have to pick the right row: the session
  `RequestState`, the blob container named by `windowId`, `ChangeRequest`, and the pupil blobs.
  Large blast radius, no gain.
- **A child collection** follows the precedent `Datasets` already set on this entity, and reparents
  `Datasets` onto the exercise on the way past.

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
    public List<CheckingWindowDataset> Datasets { get; init; } = [];
}
```

Mirror `CheckingWindowDatasetConfiguration` for the `IEntityTypeConfiguration`, and store the enum
as a string, as `CheckingWindow` already does for `KeyStage` and `CheckingWindowType`. One row per
type per window is a sensible unique index; the number of **types** a window may hold must stay
open.

### Datasets belong to the exercise

`CheckingWindowDataset.CheckingWindowId` becomes `CheckingExerciseId`. `CheckingWindow` stops
holding `Datasets` directly and reaches them through `CheckingExercises`.

A dataset is an input to one activity. The two 16-19 pupil CSVs feed pupil data checking; the six
results files feed results enquiry, on their own dates and against their own schemas. Hanging both
off the window would put unrelated files in one flat list with nothing to say which activity each
one serves — and would leave the admin wizard unable to tell an incomplete pupil-data window from a
complete one that has not had its results files loaded yet.

Two consequences follow.

**Ingress runs per exercise, not per window.** The rule that a window type's ingress files must be
validated and written in a **single run** — because a second run's sweep would wipe the first run's
output — still holds, but now *within* an exercise. Two exercises are two independent runs, which is
what you want: results-enquiry data must never have to be re-uploaded to correct a pupil file.

**Each exercise owns a blob prefix.** `CsvSchemaFileProcessor:300` writes
`data/{schoolId}_pupils.json`, and its clear sweep at `:411` deletes everything under the `data/`
prefix. Two exercises writing into the same `{windowId}` container under the same prefix would mean
the second run destroys the first's output.

The layout that avoids this is already half in place. Results enquiry reads
`results-enquiry/data/{laestab}_results.json`; pupil data sits at the bare `data/` prefix. Because
blob prefixes match as plain strings, a `data/` sweep does not touch `results-enquiry/data/`, so the
two are already isolated. Formalise it rather than change it:

| Exercise | Prefix |
|---|---|
| `PupilData` | `data/` |
| `ResultsEnquiry` | `results-enquiry/data/` |

Keeping pupil data on the bare prefix is deliberate. It costs one legacy-looking row in a lookup and
saves migrating every window's blobs. Derive the prefix from the exercise in one place, and let an
unmapped exercise type throw rather than default — a new exercise silently sharing another's prefix
is the one failure this design exists to prevent.

That one place is `Application/WindowManagement/CheckingExerciseBlobPaths.cs`. The per-school data
files are not the whole story: a run also writes a timestamped **summary** and an **error log**, and
both were named on the window alone, so an unscoped sweep would still have let one exercise delete
another's summaries. They carry the exercise prefix too — `{windowId}_summary_…` and
`{windowId}_error_log.txt` for pupil data, `results-enquiry/{windowId}_summary_…` and
`results-enquiry/{windowId}_error_log.txt` for results enquiry — so pupil data's stay exactly where
they already are and nothing has to move.

**Backfill note (#317).** #313's migration gave every existing window a `PupilData` row and nothing
else. Once the page's options follow the exercises, that would have silently withdrawn the
results-enquiry option from every deployed 16-19 window, so `BackfillResultsEnquiryExercise` gives
each `Post16` window a `ResultsEnquiry` exercise on the window's own dates — reproducing exactly the
behaviour it has today. It is transitional and guarded by `NOT EXISTS`, so a window someone has
configured with real enquiry dates keeps them, and #319's admin replaces the placeholder dates.

`HasPupilData` on the landing page becomes a question about the pupil-data exercise's prefix. Note
that it now only decides whether the read-only pupil content can render. It no longer decides
whether a card appears.

Per-exercise too: the admin wizard's schema and ingress steps, and the `Validated` stamp. A window
is not validated as a whole any more — each exercise is.

### Migration

Add **new** migrations. Never amend a shipped one — `AddCheckingWindowDatasets` shipped on
2026-07-28 and has migrations after it, so it is live everywhere.

1. Create `CheckingExercises`, and backfill one `PupilData` row per existing window copying that
   window's `StartDate` and `EndDate`. Every window in the database is single-exercise, so the
   backfill is uniform.
2. Add `CheckingExerciseId` to `CheckingWindowDatasets` and point every existing row at its window's
   backfilled `PupilData` row. Drop `CheckingWindowId` in a **later** migration, once the readers
   have moved, so a rollback stays safe — the same treatment the legacy scalar
   `IngressFile`/`SchemaFile` columns got.

No blob migration is needed, because pupil data keeps its existing paths.

---

## Where "is this exercise open" is answered

One rule, in one place, in Application. Nothing else may compare dates.

```csharp
// Application/WindowManagement/ICheckingExerciseService.cs
bool IsOpen(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);
IReadOnlyList<CheckingExerciseType> OpenCheckingExercises(
    IReadOnlyList<CheckingExerciseDto> exercises);
DateTime? EndDateFor(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);
```

These take the exercise rows rather than a window DTO for two reasons. There are two unrelated
classes named `CheckingWindowDto` — `Application/LandingPage/ILandingPageService.cs:23` and
`Application/WindowManagement/IWindowService.cs:18` — so a DTO parameter is ambiguous at the call
site; and the second already carries its own `IsOpen` property (`:33`), which would read as a direct
contradiction of `IsOpen(...)` on this service.

Time comes from a `TimeProvider` injected into the implementation, as `LandingPageService` already
does (`timeProvider.GetLocalNow()`). Keeping `now` inside is what stops a caller supplying its own
clock; do not call `DateTime.Now`, and do not accept `now` as a parameter.

**Fail closed.** A window with no row for a type is closed for that type. A window with an empty
exercise list is closed for everything. A half-configured window must not open a journey by
accident.

**Fail closed applies to actions only.** An empty `OpenCheckingExercises` must never remove the card
or hide content. Read-only content is available for the whole outer window.

The window DTOs that reach Web must carry the exercise list, so the `LandingPageRepository`
projection and the `CheckYourPupilData` window read both need the extra `.Select`. The property is
`Exercises` on both `CheckingWindowDto` classes — `WindowManagement` named it that when datasets
were reparented onto the exercise, and the two read paths match it rather than introducing a second
name for the same list. Persistence aliases the shared `CheckingExerciseDto` on import, because
importing the whole `WindowManagement` namespace would make `CheckingWindowDto` ambiguous there.

---

## What changes on screen

### Landing page

Nothing. The window appears while the **outer** pair brackets `now`, whether or not any exercise is
open, and the card title stays the window title.

Showing the pupil-data deadline on the card as a hint while that exercise is open would be useful,
but it is a content design decision, not something the model requires.

### Check your pupil data (`Views/CheckYourPupilData/Index.cshtml`)

The page ends with a `NextSteps` radio group whose options are decided by window type
(`Index.cshtml:68` onwards, `CheckYourPupilDataController:118-121`):

```
( ) Request an amendment to pupil data
( ) Report an issue with an exam result     <- Post16 only
( ) Confirm pupil data is correct
```

Four changes:

1. The controller builds the option list from `OpenCheckingExercises(...)`, mapping each open
   exercise to the options that belong to it. `RequestChange` and `Confirm` belong to `PupilData`
   and disappear together when it closes; `ResultsEnquiry` appears only while its exercise is open.
   This replaces `OffersResultsEnquiry` at `:202` and `ShowResultsEnquiryOption` on the view model,
   and fixes KS4 Autumn along the way.
2. The mapping from exercise to options belongs in Application, not in the controller. Adding a
   future exercise type should mean adding a mapping entry, not editing branching logic.
3. If only one option survives, render a **single button**, not a one-item radio group. A radio
   group with one choice is a poor pattern and contradicts its own "select one option" hint.
4. If **no** option survives, render no form at all. The tables, the search and the downloads stay,
   with a short statement that the window is closed for changes. Everything above the form is
   unchanged, so this needs no new page and no redirect.

The deadline sentence at `Index.cshtml:22` reads:

> You must request any changes to pupil data before `@Model.WindowEndTime` on `@Model.WindowEndDate`

Those values come from the **outer** window, so on a 16-19 window the sentence promises March when
the real pupil-data deadline is 18 Oct. They must come from `EndDateFor(..., PupilData)`, and after
that date the sentence becomes a past-tense statement that the pupil data window has closed. Keep it
in London time, per the existing display convention.

### Server-side gating

The option list is presentation. The gate must also sit on the POST paths, because a user can hold a
bookmarked URL or a stale tab across a closing date:

- `CheckYourPupilDataController` next-steps POST — currently rejects a results enquiry on a
  non-Post16 window at `:103-104`; becomes a closed-exercise rejection for every option.
- `WhatToChangeController.Index` and `.Confirm` — require `PupilData` open.
- `ResultIssueController.Index` and `.Confirm` — require `ResultsEnquiry` open.
- `JourneyController` — the `IsSessionReady` guard at `:1143` already runs on every action (`:51,
  94, 118, 285, 312, 455, 710, 808, 842, 887, 907, 1037`). Extending that one helper covers every
  journey action, for every exercise.
- `ConfirmCorrectController` — same gate.

A rejected request redirects back to Check your pupil data with an explanation. It must not 404.

The gate needs no new session state: `RequestState.SelectedWhatToChange` plus
`WhatToChangeCheckingExerciseMap` already yields the journey's exercise. Deriving beats storing — a
stored copy can disagree with the journey's own change type.

---

## How a journey knows its exercise

A journey is identified by `WhatToChange` plus `CheckingWindowType`, and
`WhatToChangeCheckingExerciseMap` maps the member to its exercise. Results enquiry did not need a
new axis in the config key; `IncorrectGrade` is a `WhatToChange` member like any other, and its flow
is `IncorrectGrade_Post16.json`.

Keep that. #318 made the map's values `CheckingExerciseType` instead of `const string`, so there is
one spelling of an exercise name in the solution, and #320 moved the map out of
`Application/ResultsEnquiry/` into `Application/WindowManagement/` — it answers a question about
every exercise, so filing it under one of them read as if results enquiry were a special case. The
naming rule for the config key itself needs no change.

### The trigger to put the exercise in the config key (#320)

The config key stays `{WhatToChange}_{CheckingWindowType}.json`. The one thing that forces a third
axis is a **name collision**: two exercises both wanting the same `WhatToChange` for the same
`CheckingWindowType` — say a pupil-data `Remove` and a results-enquiry `Remove` on `Post16`. The
key cannot name both files, and `WhatToChangeCheckingExerciseMap` cannot answer which exercise a
`Remove` belongs to, because the answer would depend on the window type.

When that happens:

1. The exercise joins the key: `{Exercise}_{WhatToChange}_{CheckingWindowType}.json`. Every existing
   file is renamed to read `PupilData_*` except the results-enquiry ones, which read
   `ResultsEnquiry_*`. Blobs are renamed in the `question-flows` container, and
   `Web/Data/QuestionFlows/` renamed to match, in the same change — the seeder uploads by filename.
2. `WhatToChangeCheckingExerciseMap` is retired. The exercise is no longer derived from the change
   type; it comes from whichever page started the journey and is carried into the key.
3. `IsSessionReady`'s gate then needs the exercise from somewhere else. Storing it on `RequestState`
   is the obvious move but reintroduces the disagreement the map exists to prevent, so pass it from
   the entry-point controller rather than persisting it.

Until a collision exists, none of that buys anything: the key is shorter, the map is three lines,
and the blobs need no rename.

`ChangeRequest` needs no exercise column either — `AmendmentType` (`:33`) plus the map derives it.

---

## Open questions

These do not block the model.

1. **Drafts across the boundary.** A user saves a pupil-data draft on 17 Oct and returns on 19 Oct.
   `AmendmentRequestsController.ResumeDraft` (`:117`, `:172`) would rebuild a journey for a closed
   exercise. ~~Block the resume with a clear message, or allow the resume and block the submit?~~
   **Decided in #318: block the resume.** `AmendmentRequestsController.Edit` refuses to rebuild a
   journey whose exercise has closed, so nobody edits a request that could never be sent. The gate
   also sits in `IsSessionReady`, and it holds for every exercise type.
2. **The Amendment Requests grid.** ~~It now holds two populations. One list, or filtered/grouped by
   exercise?~~ **Decided in #320: one list, unsplit.** Both populations keep one table, one set of
   checkboxes and one bulk submit. Splitting the grid would double the bulk-submit control and the
   empty states for a school that in practice holds a handful of requests, and it would make the
   common case — a window with one exercise — carry a grouping header that says nothing.

   What was wrong was never the grid; it was the deadline. The page printed the **window's** end
   date once, and the window's end is the union of its exercises (#319), so on a 16-19 window it is
   the results-enquiry close — months after pupil data shuts. It told a school it still had time to
   amend pupil data when that had closed. `AmendmentRequestsResult.Deadlines` now carries one
   `ExerciseDeadlineDto` per exercise, in `SortOrder`, each with its own `EndDate` and its own
   `IsOpen` from `ICheckingExerciseService`, and the page prints one sentence per exercise — "Submit
   your … by …" while open, "The deadline for … passed at …" once closed. The confirmation page
   after a bulk submit reads the **pupil-data** exercise's end, because the banner it sits in offers
   another amendment and that journey shuts when pupil data shuts; a window with no pupil-data
   exercise drops the banner rather than quoting a date from elsewhere.
3. **Results-enquiry ingress.** Split out of #319 into #324. ~~Nothing writes `results-enquiry/data/`
   outside `Web/Seeding/SeedStudentResults.cs`, which is development-only.~~ **Done in #324.** The
   results-enquiry exercise owns one dataset slot per source file, named by the `ResultsFileTags`
   tag it stamps (five for a 16-19 window, four for KS4, none for KS2 — it has no results feed), so
   an admin uploads the supplier's files and validates the exercise exactly as for pupil data. A
   dataset carries a `SourceFile` tag, the exact analogue of `Included`: `Included` stamps inclusion
   by file of origin, `SourceFile` stamps provenance by file of origin, and the run stamps it onto
   every record because no supplier CSV carries a `SOURCE` column. The run writes
   `CheckingExerciseBlobPaths.DataBlobName(exercise, laestab)` — pupil data strips only the slash
   from the laestab, results normalises it, and the choice is made in the lookup rather than left to
   whichever name a caller reached for.

   Only the main file is required. The late, revised and retention files land weeks apart and one
   may never land, so `CheckingExerciseDto.HasRequiredFiles` asks that every *required* slot is
   filled and at least one slot is, and the run reads `DatasetsToIngest` — the complete slots only.
   A run rewrites the exercise's whole output, so the exercise is re-run when the next file lands.
   Pupil-data slots stay required: each 16-19 pupil file carries a whole population.

   Two things about the input remain assumptions rather than confirmed facts, both flagged on #324:
   the results CSVs are read as carrying the output contract's own column names (`CYPMD_ID`, `QAN`,
   `QUAL_NAME`, `SYLLABUS`, `SESSION`, `GRADE`), because ingress passes CSV columns through verbatim
   against the admin-supplied JSON schema and has never had a renaming step; and they must carry a
   `LAESTAB` column, which is what splits one supplier file into one blob per school. A file without
   it now fails the run by name instead of throwing. If a supplier sample shows different headers,
   the mapping step is new work — it is not a matter of editing a schema.
4. **Window admin wizard.** ~~The exercise dates need a step.~~ **Done in #319.** The wizard asks
   which exercises the window runs, then one date page per ticked exercise, and derives the outer
   pair as their union (`CheckingWindowDto.DeriveDatesFromExercises`) so the two can never disagree.
   There is no window-level date step left.
5. **KS4 Autumn dates.** Confirm its inner and outer dates. The model assumes they nest the same way
   as 16-19.
6. **Learner noun.** A 16-19 window calls a learner a **student**; every other key stage calls one a
   **pupil**. The word is derived from `CheckingWindowType` by
   `Application/WindowManagement/LearnerNoun.cs` — there is no column for it on `CheckingWindow`, no
   step in the admin wizard and no migration, so a window's noun cannot drift from its key stage. It
   has no default case, the same rule as `CheckingExerciseBlobPaths`: a new window type must state
   its noun.

   The noun is carried on each school-facing view model, assembled in the controller; a journey
   takes it from `RequestState.LearnerNoun` (derived from the window in session, never stored).
   Admin and ops screens keep "pupil" as internal vocabulary, and so do routes
   (`/CheckYourPupilData/{id}`), blob paths (`data/{laestab}_pupils.json`), CSV headers and
   filenames, and every C# type name. A 16-19 user therefore reads "student" on the page and "pupil"
   in the address bar; changing that is a redirect-and-migration job for no gain to the school.

   Question flow JSON needs no mechanism: the configs are already per window type, so a Post16 flow
   simply writes "student".

   The three CMS content keys on Check your pupil data are suffixed with the window type
   (`check-pupil-data-title-post16`, and the two empty-state blocks) because a block seeds its
   default once per key and one key cannot hold both nouns. The unsuffixed keys are left orphaned,
   not deleted.
