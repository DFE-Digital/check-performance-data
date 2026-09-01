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
| `select-student-cohort` | `PupilSearch` | One student as an example (cohort branch). Lists only students who hold results — see [Only students who hold results](#only-students-who-hold-results). |
| `select-student-single` | `PupilSearch` | The affected student (single branch). Same restriction. |
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

Written by the results-enquiry checking exercise's own ingress run (#324): one dataset slot per
source file, each stamping its `SOURCE` tag onto every record it contributes, all merged into one
file per school in a single run. `SeedStudentResults` still writes the same blob in development, so
a developer needs no supplier files. Only the main file is required to validate the exercise — the
late, revised and retention files are optional slots, because they land weeks apart and one may
never land, and each run rewrites the school's whole file from the slots that are filled. The supplier CSVs must carry a `LAESTAB` column — that is what
splits one file into one blob per school — and a file without one fails the run by name.

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

## Only students who hold results

Both `PupilSearch` pages set `"requireResults": true`. A student with no result has no grade to
correct, so they are not a candidate, and offering them leads only to a dead end.

How it is wired, layer by layer:

1. `IStudentResultsClient.GetStudentIdsWithResultsAsync(windowId, laestab)` returns the school's CYPMD
   ids, case-insensitively, from the **already cached** results file — an autocomplete keystroke costs
   no download.
2. `CheckYourPupilDataService.GetPupilSuggestionsAsync(..., requireResults)` resolves that set only
   when asked, and hands it to the repository. Every other journey passes null and searches the whole
   roll.
3. `CheckYourPupilDataRepository.SearchPupilsAsync(..., cypmdIdAllowList)` applies it **before** the
   ten-suggestion cap. Filtering after the cap would drop the one student who holds results whenever
   ten who do not sort ahead of them.

Persistence never learns what a result is — it receives a set of ids.

The restriction is a search restriction, never a permission. It only ever narrows a search that is
already scoped to the signed-in school's own file, so a request that forges or omits
`requireResults=true` reaches nothing new.

**Because it hides students, the pages say so.** The `subheading` ends "You can only search for
students who have results", and the autocomplete's no-match text becomes "No students found with
results" rather than the component's default "No results found" — otherwise a school cannot tell a
typo from a student who holds nothing. Copy on both is FLAGGED for content sign-off.

`select-result` keeps its own empty state for the cases the restriction cannot cover — back
navigation, a stale session, or a results file that changes mid-journey. Rather than an autocomplete
that can never answer, it states that we hold no results for the student and links back to the
student search. It renders instead of the control and the Continue button, which could only ever
fail validation.

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
- `AdminRequestsService.ProcessCloseWindowEvent` skips **every** results-enquiry journey. That replay
  builds a *pupil-amendment* ticket, and an enquiry's QAN, syllabus code, session, current and revised
  grade have no place in that shape. Replaying one would create a malformed ticket **and** flip the row
  to `SubmittedCommitted`, so the real dispatch could never find it again.

  The guard asks `WhatToChangeCheckingExerciseMap` whether the journey belongs to the ResultsEnquiry
  exercise rather than naming enum members. It originally tested `IncorrectGrade` alone, and the
  missing-qualification journey (AB#297848) walked straight through it — the map keeps the next
  sibling right by construction. `AdminRequestsServiceEnquiryGuardTests` drives its cases from the
  same map, so a new enquiry kind is covered the moment it is mapped.
- `QuestionFlowOutcomeKeyAlignmentTests` lists `IncorrectGrade` in
  `FlowPrefixesThatDoNotRouteToTheRulesEngine` and asserts it has **no** outcome key, so nobody can
  quietly bind it to rules-engine routing. That list going empty is the signal every flow routes.

## Missing qualification (AB#297848)

The sibling 16-19 results-enquiry journey to "report an incorrect grade": a student is missing a
qualification the school expected the data to hold. Reuses the same journey engine end-to-end
(cohort scope → student search → qualification → details → comments → summary → submit) and the
same `ResultsEnquiry` persistence shape.

| Page id | Type | What it asks |
|---|---|---|
| `cohort-scope` | `Question` / Radio | Does the missing qualification affect the whole cohort? Branches the journey. |
| `cohort-count` | `Question` / FreeText | How many students (cohort branch only). |
| `select-student-cohort` | `PupilSearch` | One student as an example (cohort branch). |
| `select-student-single` | `PupilSearch` | The affected student (single branch). |
| `select-qualification` | `QualificationSearch` | AO then QAN, resolved server-side (fails closed on a mismatched pair) — see below. |
| `qualification-details` | `QualificationDetails` | Shows the chosen qualification; asks for syllabus code, award date, missing grade, NCN. |
| `additional-info` | `Question` / TextArea | Optional comments, 250 characters. |

Flow config: `src/DfE.CheckPerformanceData.Web/Data/QuestionFlows/MissingQualification_Post16.json`.
`MissingQualificationFlowTests` pins the page/question ids. Two differences from incorrect-grade, both
deliberate:

- **No `check-late-results` interstitial.** That guidance is about the second late results file
  possibly containing the fix; it cannot contain a qualification the data does not hold at all.
- **Neither `PupilSearch` page sets `requireResults`.** Per AB#297004 the search covers both
  populations, unfiltered — a student whose only qualification is the missing one holds no results at
  all, so `requireResults` would make exactly the students this journey exists for unfindable.

### Qualification reference — `IQualificationReferenceClient`

Container `rules-config`, blob `qualification-reference.json`, beside `grade-reference.json` — same
arrangement, same seed-if-missing (`If-None-Match: *`), same 5-minute cache, same empty-lookup
degrade on a missing blob. Deliberately a separate document from the grade reference: the two come
from different teams on different cadences, and merging them would couple incorrect-grade's
validation contract to the QualList export.

Seeded from `src/DfE.CheckPerformanceData.Web/Data/QualificationReference/qualification-reference.json`,
generated by `scripts/Convert-QualListToReference.ps1` merging two supplier exports:

- **QualList.xlsx** — 974 QANs across 25 awarding organisations, with grades per QAN. Grade order is
  source order (a scale's order is meaningful).
- **SyllabusCodes.xlsx** — the QUID→syllabus-code mapping, filtered to `1619 == 1` rows. `quid` is
  QualList's QAN with the slashes removed.

**Sparse syllabus coverage, FLAGGED to the BA:** only 13 of the 974 QANs carry any 16-19 syllabus
rows, and the syllabus code is a required field on the details page. For the other 961 qualifications
the details page shows "We cannot list syllabus codes for this qualification yet" and the enquiry
cannot be completed — mirroring exactly how the grade picker degrades for a QAN absent from the
grade reference. The Figma summary's "Syllabus code: NA" hints an "NA / not applicable" fallback may
be intended; that is a spec decision, not implemented here. Regenerate the seed via the script when a
fuller SyllabusCodes export arrives.

QualList's "Included in KS4" column is ignored — no ticket instructs filtering the AO/QAN dropdowns
by it, so all 974 QANs are offered.

### Qualification search — AO then QAN, resolved server-side

`QualificationSearchPage`/`QualificationSearchPost` mirror `ResultSearchPage`/`ResultSearchPost`: the
posted AO and QAN are a claim, not a fact. The server re-resolves the QAN against the reference
lookup and rejects it (fails closed, exactly as an unresolvable result key does) unless it also
belongs to the posted AO — the client-side JS cascade that narrows the QAN `<select>` to the chosen
AO is presentation only, and a tampered pair must not record an AO the qualification does not belong
to. The QAN `<select>` renders every option grouped by `<optgroup>` per AO, so the page works with
JavaScript off.

Changing the resolved qualification clears the syllabus-code and grade answers (they belong to one
qualification); re-confirming the same QAN is not a change, so they survive back-navigation — the
same rule the result-search page applies to the revised grade.

### Grade scale without a blob call

The missing-qualification grade picker (`q-missing-grade`, `QuestionType.GradeSelect`) reuses the
existing `GradeSelect` engine machinery, but its scale comes from `QualificationReference.
ToGradeReference()` — the QAN's grades from the QualList entry already resolved at qualification
selection — rather than an `IGradeReferenceClient` blob lookup. Every grade is offered as a "pass"
grade (the QualList export has no pass/fail split, and the missing grade is the user's claim, not a
ranked correction against a held result), and the must-differ rule does not apply — there is no
current grade to differ from.

### Syllabus code — a server-sourced select

`QuestionType.SyllabusSelect` (`_SyllabusSelect.cshtml`, clone of `_GradeSelect.cshtml`) validates
through `IJourneyValidationService.ValidateOptionSelect` — a generic fail-closed membership check
(blank, unknown, and nothing-to-offer all produce the same message) rather than `ValidateGradeSelect`'s
current-grade-aware rules. Options come from the resolved qualification's `SyllabusCodes`, each
rendered as `"{code} — {title}"` (FLAGGED: label format needs content sign-off) — the posted and
validated value is the bare code.

### NCN and the award-date window

The optional National Centre Number is format-validated by `NcnValidator` (`Ncn`, ≤ 5 characters,
AB#298201's exact copy). The award date is validated by `MissingQualificationDateRules` — compiled
rather than flow-JSON-declared, for the same reason as `RemovalJourneyDateRules`/`AddJourneyDateRules`:
a JSON-declared rule can be silently absent in a deployed environment. It must be no later than
UK-today and no earlier than 1 September 2023 (the 2023/24 and 2024/25 academic years).

### Submission

`RequestService.SubmitResultsEnquiryAsync` now serves both enquiry kinds. A missing-qualification row
persists with `AmendmentType = WhatToChange.MissingQualification`,
`RequestTypeDescription = "Results enquiry - Missing qualification"`, same `RequestType.ResultsEnquiry`
/ `Status.SubmittedUnCommitted` shape, same **no enqueue** (Zendesk dispatch is parked for both
enquiry kinds), same `QuestionFlowOutcomeKeyAlignmentTests` exclusion from rules-engine routing.
`MissingQualificationSummary` supplies its own check-answers row set (AO and QAN change through the
qualification-search page; syllabus code, award date, grade and NCN each change through the details
page).

## Confirmation email

`NotificationType.ResultsEnquirySubmitted`, dispatched by
`RequestNotificationService.NotifyResultsEnquirySubmittedAsync` through the existing
`INotificationDispatcher` → `NotificationSender` pipeline. Personalisation is `ref number` and
`email address`. It carries **no deadline** (an enquiry is not something the school must come back and
finish) and goes to the **submitter only** (nothing is being asked of the rest of the school).

**Creating the template (AB#298309 — ops runbook).** The template does not exist yet; create it in
the GOV.UK Notify admin UI with exactly this content (the copy is from ticket AB#298309; the only
placeholder is `((ref number))` — Task/PR AB#298309 pinned the personalisation contract to
`ref number` + `email address` and nothing else, so any other placeholder will fail every send):

> **Subject:** `Your enquiry - ((ref number))`
>
> **Body:**
>
> Thank you for submitting an enquiry to the Department for Education.
>
> Reference number: ((ref number))
>
> We will investigate your enquiry and respond where appropriate.

Then set the template's id as `Notify__ResultsEnquirySubmittedTemplateId` in each
`terraform/application/config/*.yml` (one template can serve every environment, or per-environment
copies — match whatever the existing six template ids do). Until an environment has a value,
`NotifyService` logs one warning per submission and sends nothing; the enquiry journey is unaffected.
The same email is sent for **every** enquiry type — incorrect grade, missing qualification, and the
future result-does-not-belong-to-pupil all share `ConfirmResultsEnquiryAsync`, and the wording is
type-agnostic by design (AB#298309 AC4).

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

`SeedStudentResults` writes results for Kingsmead (`860/4070`) in the seeded Post16 window: mixed
`16to19_MAIN` / `16to19_LR1` tags, one qualification held twice in different sessions, and **no
`16to19_LR2` rows** so the interstitial is on the happy path.

Three students (`500001`–`500003`) carry the Figma screens' own qualification fixtures. The CYPMD ids
are the ones `SeedPupilData` actually generates — a result keyed to Figma's own id would belong to no
selectable student and dead-end the journey. E2E drives `500001` by name, so those three rows are
pinned by `SeedStudentResultsTests`.

The rest is generated across both populations (every third included student, every fifth
non-included), giving roughly a quarter of the school. That is deliberate on both sides: with the
search restricted to students who hold results, three students leave a manual tester unable to
exercise a common-surname search or the ten-suggestion cap, while seeding *everyone* would hide both
the restriction and the empty state behind data that never exercises them. Generated qualifications
come from the seeded grade reference, so the revised-grade picker can always list grades.

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
ticket); the six-file ingestion pipeline itself (FACT tickets — the portal side of it, the admin upload and
ingress run that fill these blobs, is #324);
result-does-not-belong-to-student flow (sibling ticket, still no journey); drafts (decided
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
