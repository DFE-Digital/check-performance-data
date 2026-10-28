# KS2 September 2027 journeys

The decisions and assumptions behind the KS2 change-request journeys. Code comments refer to
them by number ("decision 1", "assumption 2"). Keep the numbers stable.

Source: the business requirements document "September 2027 - Go Live timeline" and the KS2
feature comparison spreadsheet. Where those did not answer a question, the assumptions below
apply.

## Scope

KS2 has two journeys:

- **Remove a pupil**, with 11 removal categories (see below). Flow file: `Remove_KS2.json`.
- **Confirm pupil data is correct**. This is the same as on KS4 June.

KS2 has no Merge, Include or Add journey. The requirements say so.

## Decisions

1. **The What to change options come from the flow files.** An option shows only when
   `{WhatToChange}_{CheckingWindowType}.json` exists (`IQuestionFlowService.GetPupilDataChangesAsync`).
   This applies to the pupil-data changes only: Merge, Include, Remove, Add. The results changes
   start from the results-enquiry page. The POST uses the same list, so a posted change with no
   flow file is refused. This replaces the hard-coded Merge and Remove radios and the
   `IncludeJourney.SupportedWindowTypes` and `AddPupilJourney.SupportedWindowTypes` lists.

   | Window | Flow files | Options shown |
   |---|---|---|
   | KS4 June | Add, Include, Merge, Remove | All four (no change) |
   | Post16 | Include, Merge, Remove | Include (since #711), Merge, Remove |
   | KS4 Autumn | Add | Add only. Before, it also showed Merge, Include and Remove, which had no flow file and went nowhere. |
   | KS2 | Remove | Remove only |

   To give a window type a journey, add its flow file. Note: the rules engine is keyed by
   window type too. A new flow file whose reasons have no rules for that window type sends
   every request to Scrutiny. `FlowReasonRuleCoverageTests` fails when a flow sends a reason that
   auto-decides on other window types but has no rule that can auto-decide on the flow's own.
2. **`Add_KS2.json` is deleted.**
3. **The `DfeNumber` validator stays.** It accepts `123/4567` and `1234567`. The requirements'
   "exactly 7 numbers" means 7 digits, with or without the slash.
4. **No school-type differences on KS2.** Every school type gets the same options.
   `Remove_KS2.json` has no `visibleWhen`.
5. **KS2 has no add-back pupils** (no 403 code). The KS2 rules have no add-back branch.
6. **Dates are for 2027.** Every rule date and every hint uses the 2027 exercise.
7. **"Tests", never "exams"**, on every KS2 page.
8. **Only included pupils can be removed.** `select-pupil` uses `pupilFilter: "Included"`. The
   KS2 schema stamps `INCLUDED` for P_INCL 201 only.

## Removal categories

`Remove_KS2.json` starts from `Remove_KS4June.json` and **uses the same page ids** where the
page is the same, so the date rules keyed by page id (`RemovalJourneyDateRules`,
`PageDateRules`) apply to both. "Evidence" means the shared `evidence` page.

| # | Category | Option value | Evidence | Rule |
|---|---|---|---|---|
| 1 | Admitted following permanent exclusion | `permanent-exclusion` | Evidence | `AFE-KS2`: reject if excluded before 1 Sept 2025 |
| 2 | Admitted from abroad with English not first language | `english-not-first-language` | Evidence | `EAL-ACC-KS2-*`: accept on or after 1 Sept 2024 (see EAL below) |
| 3 | Child missing education | `child-missing-education` | Evidence | `PMIE-OTHER-REJ`: reject unless Ground H or I |
| 4 | Pupil has died | `pupil-died` | None | `DEC-1`: accept |
| 5 | Elective home education | `elective-home-education` | Evidence | `EHE-KS2`: reject if removed on or after 10 May 2027 |
| 6 | Permanently excluded from current school | `permanently-excluded` | Evidence | `PEX-KS2`: reject if excluded on or after 10 May 2027 |
| 7 | Terminal or critical illness | `life-limiting-illness` | Evidence | `TCI-KS2-REJ` if tests sat; terminal illness goes to Scrutiny |
| 8 | Permanently left England | `permanently-left-england` | Evidence | `PLE-KS2`: reject if removed on or after 10 May 2027 |
| 9 | Social care involvement including police | `social-care-involvement` | Evidence | `SCI-KS2-REJ` if tests sat |
| 10 | End-of-key-stage assessments deferred by a year | `assessments-deferred` | `evidence-with-comments` | None: Scrutiny only (`ASD-DEF`) |
| 11 | Pupil added to school roll after start of summer term | `added-after-summer-term` | `evidence-with-comments` | `PAS-REJ`: reject if arrived before 1 April 2027 |

`evidence-with-comments` asks for a file and "Additional comments", both mandatory.

KS4 June reasons that KS2 does not have: dual registered or moved school, not on roll, year
group change, completed KS4 elsewhere, Other (add-back).

### EAL

The boundary for a "new admission" in 2027 is 1 Sept 2024. On or after it, the request is
auto-accepted (`EAL-ACC-KS2-ALL`, `EAL-ACC-KS2-ARRIVAL`). Before it, the request goes to
Scrutiny (`EAL-DEF`). The four KS4 accept rules (`EAL-ACC-NEWADM-2022-*`,
`EAL-ACC-ADDBACK-2021-*`) had no window-type condition, so they also fired on KS2. They are
now limited to `KS4June`. The two reject rules stay shared.

`AFE-PRE2023` had no window-type condition either. It is now limited to `KS4June`, and
`AFE-KS2` is the KS2 rule.

## The rule dates only work in 2027

The KS2 rule dates are written into `rules.json` for the 2027 exercise. Before then:

- `PAS-REJ` rejects every request, because the arrival date must be in the past and so is
  before 1 April 2027.
- `EHE-KS2`, `PEX-KS2` and `PLE-KS2` never reject, because no past date is on or after
  10 May 2027.

So in dev and QA these outcomes do not show what schools will see. Test them with a rule
context that has a 2027 date, not by a browser walk.

## Assumptions

These questions are not answered. We use the assumptions below and change them if the
business answers differently.

1. **Elective home education and terminal illness wording.** The requirements say the
   elective home education wording differs from KS4 June but do not give it. Use the KS4 June
   wording, with "tests", "year 6" and 2027 hints.
2. **No EAL cohort check on KS2.** On KS4, `PageDateRules` rejects a "started at your school"
   date before the cohort started (scenario 007). The requirements give no such rule for KS2,
   so `Remove_KS2.json` sets `"checksCohortStart": false` on its `english-not-first-language-details`
   page (`JourneyPage.ChecksCohortStart`, on by default). The flow file decides, not the window
   type, as for decision 1. A KS2 pupil who started at
   the school before the cohort also started in England before 2024, so the request goes to
   Scrutiny with evidence. It does not need a hard error.
3. **Child missing education** uses the current KS4 June page (Ground H, Ground I, other).
4. **Terminal illness when tests were sat** goes to Scrutiny (`TCI-TERM-SCR`), as on KS4 June,
   not to auto-accept.

## Open

Two Zendesk fields have no option for `assessments-deferred` or `added-after-summer-term`:
**Reason for removal** (`ZendeskTicketFieldOptions.ReasonForRemoval`) and **Correction reason
(31)** (`CorrectionReason31`). The worker leaves out an unmapped field and logs a warning, so
these tickets are not fully populated. We need the option values and the two CYPMD codes from
the business.
