# Add a pupil not found on my DfE pupil roll (simple path)

AB#297310. A school adds a pupil who is missing from their DfE pupil roll, for KS4 June, KS4 Autumn
and KS2 checking windows. This is the **simple path**: no dataset matching against the roll — the
soft-match story (AB#297780) is a separate ticket that will sit in front of this one.

## The journey

| Page id | Type | What it asks |
|---|---|---|
| `learner-details` | `Question` (multi) / `pupilFromAnswers: true` | First name, last name, date of birth, sex, UPN (optional). **These answers ARE the pupil** — see [The synthetic pupil](#the-synthetic-pupil). |
| `admission-details` | `Question` (multi) | Admission date, year group, SEN status. |
| `evidence` | `EvidenceUpload` | Optional file upload + optional free-text explanation, same shape as every other amendment journey's evidence page. |

Then the shared journey summary (`Journey/Summary.cshtml`) and `Journey/Confirmation.cshtml` —
both unchanged, reused exactly as every amendment journey uses them.

Flow configs: `src/DfE.CheckPerformanceData.Web/Data/QuestionFlows/Add_KS4June.json`,
`Add_KS4Autumn.json`, `Add_KS2.json`, resolved by the usual `{WhatToChange}_{CheckingWindowType}`
key. **Page and question ids are a serialization contract** — they are written into session state
and into the persisted journey blob, so renaming one after merge orphans stored requests.
`AddFlowTests` pins them.

### Getting in

The what-to-change page (`WhatToChangeController`) gains a fourth radio, **Add a pupil to data**
(`WhatToChange.Add`), shown only when `WhatToChangeViewModel.CheckingWindowType` is `KS4June`,
`KS4Autumn` or `KS2`. Post16 has no `Add_Post16.json` flow and the option is never offered there.

### Starting clean: the pupil-search-less reset

`Confirm` clears the whole per-request identity — reference number, selected pupil, matched pupil,
selected result, answers, history — whenever the chosen flow has **no `PageType.PupilSearch` page**.

This is not an Add special case, it is the invariant Add was the first flow to need. Every other
journey opens with a pupil search, and `JourneyController.PupilSearchPost` unconditionally
regenerates the reference and the selected pupil and nulls the matched pupil and result on every
selection — so those flows have always started clean by accident of their shape. A flow without one
inherits whatever the previous journey left in session. Two things went wrong before this reset
existed:

- **A submitted request's reference was reused.** `SummaryConfirm` clears every other per-request
  field after a successful submission but deliberately keeps `ReferenceNumber`, because the
  confirmation page reads it back out of session to render. `AddPupilJourney.BuildPupil` then reuses
  it (`refreshed.ReferenceNumber ?? …`, correct for re-edits *within* one journey), and the upsert
  overwrote the already-submitted row.
- **An abandoned Merge journey's matched pupil surfaced on the Add summary.**
  `JourneyViewModelBuilder` and `SubmittedRequestService.BuildMergeDisplays` build the
  "First/Second record to merge" rows from `MatchedPupil != null && SelectedPupil != null` alone,
  with no check on `WhatToChange` — so the Add summary replaced its "Pupil name" row with an
  unrelated pupil's name and CYPMD id, and that pupil's full `PupilDto` was persisted into the Add
  request's journey blob.

Keying the reset on the flow's shape rather than on `WhatToChange.Add` means the next
pupil-search-less journey inherits the guarantee instead of the bug.

## The synthetic pupil

Every other amendment journey starts with a pupil-search step that resolves a real dataset pupil
into `RequestState.SelectedPupil` (a `PupilDto`, not the supplier-file `PupilRecord` — that shape
has no bearing on a pupil that was never in a supplier file). The Add journey has no such step: the
pupil doesn't exist in the roll yet, which is the entire point of the ticket.

Instead, `AddPupilJourney.BuildPupil` (`Web/Controllers/Journey/AddPupilJourney.cs`) mints a
`PupilDto` directly from the `learner-details` page's typed answers, and `JourneyController.PagePost`
stores it as `SelectedPupil` the moment that page is successfully posted
(`MintSyntheticPupilIfNeeded`, called from both `PagePost` save points — the normal redirect and the
`fromSummary` edit-from-summary branch). This is the one genuinely new mechanism the ticket needed:
every downstream consumer — summary `{pupilName}` templating, drafts, `BuildChangeRequestData`, the
amendment grid, withdraw — reads `SelectedPupil` exactly as it already does, unchanged.

The synthetic `PupilDto.Id` is a **fresh `Guid`**, stable across re-edits of the same journey
(`BuildPupil` reuses the existing id when one is already set) but never colliding with another
typed-in pupil. This is correct specifically because dataset matching is out of scope here — a
fresh id per journey means the one-request-per-pupil duplicate rule can never falsely conflate two
different pupils a school is adding. `PupilDto.Age`/`Cypmd_Id` have no learner-details equivalent
and are set to `0`/`""` — verified unread for Add, since `RequestService.SubmitRequestAsync` only
builds `BuildRequestDocument` (the reader of those fields) for non-Add submissions.

### AB#297780 seam

The `learner-details` page's successful POST — `JourneyController.PagePost`, the block guarded by
`page.PupilFromAnswers` — is the interception point the future soft-match story will use to check
the typed name/DOB/UPN against the roll before minting a synthetic pupil. It carries a named
`AB#297780 SEAM` comment. No speculative branching code exists yet.

## Validation

- Required-field + valid-date on every mandatory question, exactly as the ticket's validation
  column states.
- **Character limits**: names capped at 150, UPN at 13
  (`Question.CharacterLimit`/`characterLimit` in the flow JSON). This required extending
  `JourneyValidationService.ValidateAnswer`'s `CharacterLimit` arm from `TextArea`-only to also
  cover `FreeText` — the arm didn't exist for `FreeText` before this ticket.
- **Future-date rules** (`AddJourneyDateRules`, mirroring `RemovalJourneyDateRules`): date of birth
  and admission date must not be later than today. Today itself is accepted. Compiled into code
  rather than the flow JSON, for the same reason as every other date rule in this codebase — a
  JSON-declared rule can be silently absent if blob seeding hasn't run in an environment; a
  compiled rule cannot. `QuestionFlowValidatorAlignmentTests.AddJourneyDateRules_PageAndQuestionIds_MatchTheShippedFlowConfig`
  pins the ids to the shipped flow.

Both the character-limit extension and the future-date rules were **flagged assumptions**, not
directly specified by the ticket's own validation column — confirm with the BA, but cheap and
defensible to keep either way.

## LDS bound values

These are a hard contract against the `LDS_CYPMD_Data specification v2.4` the future egress story
reads from.

**Where the egress story will find them.** Not in a `RequestDocument`: `BuildAnswerRecord` and its
`RawValue`/`Value` split only exist inside `RequestService.BuildRequestDocument`, which an Add
submission never reaches (no rules-engine enqueue — see below). The record is the **persisted
journey blob**, and it stores raw `RequestState.QuestionAnswers`:

- **Radio answers** (sex, year group, SEN status) store the option's stable **value** — the LDS code —
  in `QuestionAnswer.TextValue`. The display label lives only in the flow config and is looked up at
  render time, so copy changes cannot move the code. This half is exactly as intended.
- **Date answers** store a `DateAnswer { Day, Month, Year }` object, **not** an ISO string. Nothing
  in the Add path produces `YYYY-MM-DD`, so the egress story has to format the parts itself (a
  one-liner — `DateAnswer.ToDateOnly()` already hands back a `DateOnly` for a complete date). Worth
  knowing before that story assumes an ISO string is waiting for it.

| Field | Question id | Bound values |
|---|---|---|
| Sex | `sex` | `F`, `M`, `U` (labelled Female / Male / Not known) |
| SEN status | `sen-status` | `E`, `K`, `N` (EHC plan / SEN support / No recorded SEN — in that order) |
| Year group (KS4) | `year-group` | `10`, `11` |
| Year group (KS2) | `year-group` | `3`, `4`, `5`, `6` |
| First/last name | `first-name` / `last-name` | Free text, ≤150 characters |
| UPN | `upn` | Free text, ≤13 characters, optional |
| Date of birth / admission date | `date-of-birth` / `admission-date` | Real calendar date, not in the future — persisted in the journey blob as `DateAnswer { Day, Month, Year }`, to be formatted to `YYYY-MM-DD` by the egress story |

## Submission — no rules-engine outcomes

**Ticket B2: "No rules engine outcomes."** `RequestService.SubmitRequestAsync` makes the same write
every other amendment makes — a `ChangeRequests` row (`RequestType.Amendment`,
`AmendmentType = WhatToChange.Add`, `Status = SubmittedUnCommitted`) plus the journey JSON via
`IRequestStateBlobClient` — but **skips the rules-engine enqueue** for `WhatToChange.Add`
specifically. The row still appears on the school's amendment grid as **Add**, can be withdrawn,
and gets the standard confirmation email — it is only the rules-engine dispatch that is skipped.

Two places carry a `PARKED AB#297310` comment, both mirroring the AB#296648 ResultsEnquiry
precedent exactly:

- `RequestService.SubmitRequestAsync` — the enqueue is guarded by
  `if (journey.SelectedWhatToChange != WhatToChange.Add)`. When the LDS egress story lands, its
  dispatch goes **here and nowhere else**.
- `AdminRequestsService.ProcessCloseWindowEvent` — Add rows `continue` past the window-close
  Zendesk replay. That replay builds a *pupil-amendment* Zendesk ticket, which an Add doesn't fit,
  and committing the row (`SubmittedUnCommitted` → `SubmittedCommitted`) would hide it from the
  future egress before that egress exists to read it.

`QuestionFlowOutcomeKeyAlignmentTests` lists `Add` in `FlowPrefixesThatDoNotRouteToTheRulesEngine`
alongside `IncorrectGrade` and asserts it has **no** outcome key, so nobody can quietly bind it to
rules-engine routing. That list going empty is the signal every flow routes.

Downstream is the **LDS egress** (`LDS_CYPMD_Data specification v2.4.xlsx`) — a separate story that
reads the `ChangeRequests` row and journey blob this ticket persists.

## No new enum members, no new pages

Deliberate, per the plan's design decisions:

- No `Add` entry in `WhatToChangeToOutcomeKey` — see above.
- No new `RequestType` enum member — `RequestType.Amendment` + `AmendmentType.Add` is the typed
  identity, same pattern every other amendment type uses.
- No pupil-search page in the Add flows — the synthetic-pupil mechanism replaces it entirely.
- No new `PageType` — `learner-details` and `admission-details` are ordinary `Question` pages; only
  the `pupilFromAnswers` flag is new.

## Local development

```
docker compose up -d --build
# then, as an impersonated editor:
#   /WhatToChange/f34d285b-8660-4d12-9c30-787328deaa0a   (the seeded KS4June window)
```

The KS2 and KS4 Autumn flows ship but have no seeded checking window in dev yet — they are
exercised by `AddFlowTests` (flow-config pinning) only, not by a live journey walk, until those
windows exist.

## Deliberately out of scope

Dataset matching against the roll (ticket A: "do not test for matches" — this is the whole reason
AB#297780 exists as a separate story); the soft-match query/branching journeys; UPN format
validation beyond the 13-character cap (the matching story owns it).

## Still open

| Item | Owner |
|---|---|
| Copy sign-off: the radio label "Add a pupil to data"; the sex option `U` labelled "Not known"; the SEN option order E→K→N; every page/question/error string in the three flow configs — none of this reached Figma Epic B1 from this environment, so it was implemented verbatim and pinned by tests | Content designer |
| KS2 and KS4 Autumn checking windows are not yet seeded in dev — the flows ship but are untested by a live browser walk until a window exists | Dev team |
| The two flagged assumptions (future-date rules, character caps) — confirm with the BA; if struck, delete the corresponding validation and the JSON `characterLimit` fields, nothing else changes | BA |
| AB#297780 soft-match: the interception point is named and commented (`JourneyController.PagePost`, `page.PupilFromAnswers`), but no matching logic exists yet | Dev team (future ticket) |
