# 16-19 results enquiry — report an incorrect grade

AB#296648. A school reports that one of its 16-19 students has been given the wrong exam grade, from
choosing the enquiry type through to a reference number on screen and by email.

Related tickets: AB#296999 (results data), AB#297004 (student selection), AB#297130 (grade reference
data), AB#297013.

## The journey

| Page id | Type | What it asks |
|---|---|---|
| `check-late-results` | `Content` | Guidance: check your second late results file first. Entered by the controller, not the flow's `firstPageId` — see [Late results guidance](#late-results-guidance). |
| `cohort-scope` | `Question` / Radio | Does the incorrect grade affect the whole cohort? Branches the journey. |
| `cohort-count` | `Question` / FreeText | How many students (cohort branch only). Validated by the `WholeNumber` format validator. |
| `select-student-cohort` | `PupilSearch` | One student as an example (cohort branch). |
| `select-student-single` | `PupilSearch` | The affected student (single branch). |
| `select-result` | `ResultSearch` | Which of the student's results is wrong. |
| `grade-details` | `ResultDetails` | Shows the chosen result; asks for the revised grade. |
| `additional-info` | `Question` / TextArea | Optional comments, 250 characters. |

Then the shared journey summary (`Journey/Summary.cshtml`, enquiry branch) and
`Journey/EnquiryConfirmation.cshtml`.

Flow config: `src/DfE.CheckPerformanceData.Web/Data/QuestionFlows/IncorrectGrade_Post16.json`, resolved
by the usual `{WhatToChange}_{CheckingWindowType}` key. **Page and question ids are a serialization
contract** — they are written into session state and into the persisted journey blob, so renaming one
after merge orphans stored enquiries. `IncorrectGradeFlowTests` pins them.

### Getting in

The check-your-student-data page's "What would you like to do?" radios gain a third option for 16-19
windows: **Report an issue with an exam result**, routed to `ResultIssueController`. The issue page
renders only the *Incorrect grade* option; the two others on the Figma screen belong to sibling
tickets, and posting their values is rejected as unanswered rather than starting a journey with no flow
behind it.

## Late results guidance

Exam results arrive in batches: main + non-included + late results 1 in October, **late results 2 in
November**, revised in February, retention in March. Nearly all incorrect grades correct themselves in
the November batch, so a school reporting one in October may be doing work that batch was about to do
for them.

**Decision (BA, 2026-08-17): the guidance informs, it never blocks.** When the school holds no
`16to19_LR2` row, `ResultIssueController` routes into `check-late-results`; when it does, straight to
`cohort-scope`. The option itself is always selectable. A Figma frame showing the option greyed out
with *"Incorrect grade option will be available after releasing second late results"* was considered
and not chosen — it contradicts the ticket's own acceptance criteria.

Because the flow's `firstPageId` is `cohort-scope`, the controller **seeds `QuestionHistory` with
`check-late-results`** when that is the entry point. Without it the journey engine's out-of-sequence
guard bounces the user straight past the guidance and the AC silently never happens.

Availability is derived, never configured: `ILateResultsAvailability` asks
`IStudentResultsClient.AnyForSourceAsync(..., "16to19_LR2")`. That is the only place the service
decides what "the second late results file has landed" means.

## Data seams

### Student results — `IStudentResultsClient`

Container `{windowId}`, blob `results-enquiry/data/{laestab}_results.json`. One merged array per
school across all six supplier files, each row stamped with its source tag.

The `results-enquiry/` prefix is deliberate: per consequence #2 of `docs/16-19-window-model.md` each
checking exercise owns its own blob prefix, so when ingress becomes per-exercise no migration is
needed and one exercise's sweep cannot destroy another's output. Pupil-data checking keeps its bare
`data/` prefix. Every path segment lives in `ResultsEnquiryBlobPaths`.

Source tags (`ResultsFileTags`, verbatim from AB#296999 — a data contract with ingestion):

| Constant | Value |
|---|---|
| `Post16Main` | `16to19_MAIN` |
| `Post16LateResults1` | `16to19_LR1` |
| `Post16LateResults2` | `16to19_LR2` |
| `Post16Revised` | `16to19_Revised` |
| `Post16Retention` | `16to19_Retention` |
| `Ks4Main` | `KS4_MAIN` |
| `Ks4LateResults1` | `KS4_LR1` |
| `Ks4LateResults2` | `KS4_LR2` |
| `Ks4Revised` | `KS4_Revised` |

Reads are cached 30 minutes per `results:{windowId}:{laestab}`, matching the pupil cache so a school's
results and pupils go stale together. A missing blob reads as empty; malformed JSON throws so a corrupt
file surfaces. Numeric-looking values are tolerated unquoted, because CSV-to-JSON converters routinely
emit them that way (`TolerantStringJsonConverter`).

A result is identified by a **composite key** `QAN|SESSION|SOURCE`, not by QAN — a student can hold the
same qualification across sessions and across source files.

### Grade reference — `IGradeReferenceClient`

Container `rules-config`, blob `grade-reference.json`, beside `rules.json` because it is the same kind
of thing: slow-moving reference data from another team, shared by every window, self-seeded from a
bundled copy. Cached 5 minutes; a missing blob reads as an empty lookup rather than throwing.

Seeded from `src/DfE.CheckPerformanceData.Web/Data/GradeReference/grade-reference.json`,
**seed-if-missing** (`If-None-Match: *`) so an environment that has had the real AODC export loaded is
never clobbered by a redeploy of an older bundled copy.

The checked-in seed holds the three AB#297130 examples plus the dev QANs. The IB Diploma's 93-grade
scale is derived from the ticket (44 pass: `24B`/`24D` … `45B`/`45D`; 49 fail: `00F`–`45F`, `R`, `U`,
`X`) and is what gives the tests their `24F`-vs-`24D` case.

## Revised-grade rules

Server-authoritative, in this order (`JourneyValidationService.ValidateGradeSelect`):

1. Unanswered → `Select the revised grade`
2. Equal to the result's current grade → `The revised grade must be different from the current grade`
3. Not a grade the QAN offers → treated as unanswered (fail closed against a forged post)
4. QAN absent from the reference data → the picker is empty, the page says
   `We cannot list grades for this qualification yet`, a warning is logged, and validation can never
   pass

Comparison is **ordinal and case-sensitive**. The IB Diploma is why: `24F` is a fail and `24D` a pass,
so `24F` → `24D` is a real enquiry, and any normalising comparison risks either rejecting it or
accepting a no-op.

## Progressive enhancement

Both pickers are server-rendered GOV.UK `<select>`s that accessible-autocomplete upgrades in place via
`enhanceSelectElement`, so **both pages work with JavaScript off**. The result options are rendered
server-side rather than fetched: a student holds a handful of results, so there is nothing to gain from
a round-trip, and a fetch-only control would be unusable without script. The grade picker is enhanced
because some qualifications award 93 grades.

`/results/suggestions` (`ResultSuggestionsController`) exists and is tested but is **not used by the
result page** as a consequence. It is scoped to the session's selected student — no pupil id is ever
taken from the query string. Decide whether to keep it for the sibling "Review exam results" tickets or
remove it.

## Submission

`RequestService.SubmitResultsEnquiryAsync` makes the same two writes a pupil change request does:

1. a `ChangeRequests` row — `RequestType.ResultsEnquiry`, `AmendmentType = IncorrectGrade`,
   `Status = SubmittedUnCommitted`, description `Results enquiry - Incorrect grade`
2. the journey JSON via `IRequestStateBlobClient`

Both enum columns are `HasConversion<string>()` capped at 20 characters; `ResultsEnquiry` (14) and
`IncorrectGrade` (14) fit, which is why **no migration was needed**.

Reference format: `CYPMD_16to19_RE_{7 hex}` (`GenerateEnquiryReference`). The `RE` segment lets support
staff tell an enquiry from an amendment when a school reads one out. The confirmation mockup's
`3014023_RE10005` was confirmed illustrative.

No duplicate check — the spec allows several enquiries for the same student and result.

### Two places the duplicate rule had to be taught about enquiries

`RequestRepository` enforces one submitted request per pupil per window, to stop two competing
*amendments* to the same record. An enquiry changes no pupil data, so it neither raises that conflict
nor counts as one. `ResultsEnquiry` rows are therefore excluded from both:

- `UpsertAsync`'s hard block (and the check is skipped entirely for an incoming enquiry)
- `CheckForConflictAsync`, which drives the pupil-search duplicate warning

The two must stay in step, or a user is warned about something that then submits fine — or worse, the
reverse. Without these exclusions, reporting a wrong grade for a student blocked every later amendment
for them, with an error naming an unrelated request.

### Choosing a student discards only what came after

`PupilSearchPost` used to clear *every* answer when a primary pupil was selected — correct for the
amendment journeys, where the pupil page is first. The enquiry journey asks about the cohort **before**
the student, so it now keeps answers from pages earlier in the history and discards only those after,
along with `SelectedResult` (a result belongs to one student). Wiping them lost the cohort scope and
count, and the summary silently presented a cohort-wide enquiry as a single-student one.

## Downstream processing — a separate story

**Decision (BA, 2026-08-17):** an enquiry drops into `ChangeRequests` and saves its journey JSON, and
is **not enqueued**. It *is* ultimately bound for Zendesk, but how it gets there is a separate ticket.

Two consequences, both deliberate and both commented in code:

- `SubmitResultsEnquiryAsync` does not enqueue. When the dispatch story lands, the enqueue belongs
  **there and nowhere else**.
- `AdminRequestsService.ProcessCloseWindowEvent` skips `IncorrectGrade` journeys. That replay builds a
  *pupil-amendment* ticket, and an enquiry's QAN, session, current and revised grade have no place in
  that shape. Replaying one would create a malformed ticket **and** flip the row to
  `SubmittedCommitted`, so the real dispatch could never find it again.
- `QuestionFlowOutcomeKeyAlignmentTests` lists `IncorrectGrade` in
  `FlowPrefixesThatDoNotRouteToTheRulesEngine` and asserts it has **no** outcome key, so nobody can
  quietly bind it to rules-engine routing. That list going empty is the signal every flow routes.

## Confirmation email

`NotificationType.ResultsEnquirySubmitted`, dispatched by
`RequestNotificationService.NotifyResultsEnquirySubmittedAsync` through the existing
`INotificationDispatcher` → `NotificationSender` pipeline. Personalisation is `ref number` and
`email address`. It carries **no deadline** (an enquiry is not something the school must come back and
finish) and goes to the **submitter only** (nothing is being asked of the rest of the school).

**The template does not exist yet.** `Notify:ResultsEnquirySubmittedTemplateId` is empty in every
environment config. `NotifyService` now logs a warning and sends nothing when a template id is blank,
rather than throwing out of its template-id switch — so the journey completes without it, but **no
enquiry email is sent until the template is created and the id configured.**

## Analytics

Event catalogue additions — see also `docs/bigquery-analytics.md`.

| Event | Fields | Notes |
|---|---|---|
| `results_enquiry_started` | `enquiry_type`, `checking_window_type`, `late_results_guidance_shown` | Emitted by `ResultIssueController`. The guidance flag answers the question behind the interstitial: are we stopping enquiries that did not need raising? |
| `results_enquiry_submitted` | `enquiry_type`, `cohort_wide`, `checking_window_type`, `reference_number` **(hidden)** | Emitted by `JourneyController` on submission. |

PII rules: the reference number is always `Hidden`. No grade, QAN, student name, session or free text
ever leaves as a plain field — a grade paired with a school and a date is identifying, and the comments
box is free text by definition. `cohort_wide` is a boolean rather than the count for the same reason.

Validation failures flow through the existing `validation_error` event; `GradeSelect` codes as
`selection_invalid` alongside Radio and Autocomplete.

## Local development

`SeedStudentResults` writes results for Kingsmead (`860/4070`) in the seeded Post16 window. Three
students, mixed `16to19_MAIN` / `16to19_LR1` tags, one qualification held twice in different sessions,
and **no `16to19_LR2` rows** so the interstitial is on the happy path.

The qualification fixtures come from the Figma screens, but the CYPMD ids are the ones
`SeedPupilData` actually generates (`500001`–`500003`) — a result keyed to Figma's own id would belong
to no selectable student and dead-end the journey.

```
docker compose --profile web --profile database --profile storage up -d --build
# then, as an impersonated editor:
#   /CheckYourPupilData/6c2e1f4a-9b7d-4e38-8a15-3d9c2b4e7f01
```

## Alignment with the 16-19 window model

Per `docs/16-19-window-model.md`, without building any of the checking-exercise model:

- **Naming** is `ResultsEnquiry` (plural) throughout, matching `CheckingExerciseType`.
- **Journey identity** uses Option A: `WhatToChangeCheckingExerciseMap.CheckingExerciseFor` is the
  one lookup from a `WhatToChange` to its checking exercise. The future `IsSessionReady` gating
  consumes it; nothing else may hardcode the mapping.
- **Blob layout** is born exercise-scoped (`results-enquiry/data/`).
- **The entry radio** shows for any open 16-19 window. `// PARKED` comments mark where visibility
  moves to `ICheckingExerciseService.OpenCheckingExercises`.

Not built here: `CheckingExercise` entity and migrations, `ICheckingExerciseService`, read-only page
states, dataset reparenting, per-exercise ingress, draft-across-boundary rules.

## Deliberately out of scope

The "Review exam results" / Results / Late-results tab pages and CSV/ZIP downloads (entry-point
ticket); the six-file ingestion pipeline (FACT tickets — this feature seeds the blobs it reads);
missing-qualification and result-does-not-belong-to-student flows (sibling tickets); drafts (decided
against); duplicate-enquiry blocking (the spec allows multiples).

## Still open

| Item | Owner |
|---|---|
| Full AODC export (`Dynamic form QAN list 2026 v1.xlsx`, SharePoint) → replace the seeded `grade-reference.json` | AODC team |
| GOV.UK Notify template + `Notify:ResultsEnquirySubmittedTemplateId` — **no email sends without it** | Ops / content |
| Copy sign-off: the must-differ message; "We cannot list grades for this qualification yet"; the issue page's expander body (never captured in Figma); the result label's appended session | Content designer |
| Whether to keep or delete `/results/suggestions` | Dev team |
| Breadcrumb: the designs show `Check your student data - 16 to 19 and result enquiry`, but no journey view in the service renders a breadcrumb. Worth doing across the whole 16-19 journey at once rather than on one page | Design / dev |
| `PupilSearch.cshtml` is still JavaScript-dependent (pre-existing). The same `enhanceSelectElement` approach used here would fix it | Dev team |
