# Journeys are configuration, not code

Date: 2026-10-27

Status: Proposed

## Goal

A journey (a `QuestionFlows/*.json` file) is configuration. Its pages, questions, options and rules
are in the JSON, and no C# names its ids.

We are not near that goal today. Each time C# names a page id, a question id or an option value
from a flow file, a change to the journey also needs a change to code. If a flow renames or deletes
the id, the code stops working and nothing reports it.

This note describes the problem, the rule, the patterns that replace journey-specific code, the
guard test that stops new coupling, and the order of work.

## The problem

The flow engine started as configuration first. Page types, question types, `visibleWhen`,
`validator` and `nextPageId` are generic, and a new journey needed only a new JSON file.

Since then, more and more code knows about one journey. On 2026-10-27, the baseline in
`FlowIdCouplingTests` holds 131 ids in 25 files. 20 files are journey code, 3 are dev tools or
seed data, and 2 use the same text with a different meaning.

| Group | Files | Example | Why it is there |
|---|---|---|---|
| Integration maps | `AnswerFieldMap`, `EgressRecordBuilder`, `ZendeskTicketFieldOptions`, `CorrectionCodes`, `ZendeskConsumer`, `EgressViewModels` | `["date-removed-from-roll"] = "dateOfRemoval"` | The rules engine, the egress file and Zendesk need to know what an answer means. |
| Validation in code | `PageDateRules`, `RemovalJourneyDateRules`, `AddJourneyDateRules`, `MissingQualificationDateRules` | `EalDetailsPageId = "english-not-first-language-details"` | Cross-field date rules. The comment in `PageDateRules` says that they are in code because configs once arrived through a seeding step. That reason is gone (`docs/question-flow-deployment.md`). |
| Single-journey conditions | `EalEvidenceIsOptionalCondition`, `NotOnRollReasonIsOtherCondition`, `OriginCountryLanguageCapture` | `reason == "english-not-first-language" && first-language in {...}` | Each one is "answer X has one of the values Y", written as a class. |
| Journey code in the engine and Web | `JourneyViewModelBuilder`, `JourneyController`, `AddPupilJourney`, `ResultIssueController`, `EditAdviceService`, two views | `"q-cohort-scope"`, `"q-revised-grade"`, `"learner-details"` | The Web layer needs one answer for a summary, or must clear one answer when the selection changes. |

## The rule

**Code may depend on a closed vocabulary that the engine defines. Code must not depend on a name
that the author of a journey chooses.**

- Closed vocabulary: page types, question types, condition names, validator names, date rule
  names, question **roles** and option **meanings**. Each is an enum or a registered name.
- Names that the author chooses: page ids, question ids, option values, titles.

When code needs to know what a question means, the question declares its role in the JSON. The
code then searches for the role, not for the id.

## Patterns that replace journey-specific code

### 1. Branch on an answer that is already on the path

If the deciding answer is on an earlier page, use a radio option's `nextPageId` to go to a
different page. Do not write a condition. The flow engine already handles a changed answer: from
the summary, `PagePost` sees that the next page changed, trims the history and sends the user down
the new branch.

Use this pattern only when the page order allows it. Use a named C# condition only for a fact that
is not in the answers: school type, the not-on-roll college list, add-back status.

### 2. One generic answer condition

Let `visibleWhen`, `optionalWhen` and `requireAtLeastOneWhen` take an answer test, not only a
condition name. All the entries in a list must be true:

```json
"optionalWhen": [
  { "answer": "reason", "in": ["english-not-first-language"] },
  { "answer": "first-language", "in": ["other", "believed-other", "chose-not-to-say", "not-known"] }
]
```

This replaces `EalEvidenceIsOptionalCondition` and `NotOnRollReasonIsOtherCondition`. A bare string
stays a condition name.

A missing or blank answer fails closed, the same as an unknown condition name: `visibleWhen` hides,
`optionalWhen` keeps the question mandatory, and `requireAtLeastOneWhen` keeps the rule on.

The test names a question id, but the id is in the JSON. A generic config test checks that the
question is in the same flow and that each value is one of its options.

### 3. Declare date rules on the question

Give `Date` questions declared constraints:

```json
{ "id": "date-pupil-started-school-in-england", "type": "Date",
  "notInFuture": true,
  "onOrBefore": "date-pupil-started",
  "onOrAfter": "date-pupil-arrived-in-england" }
```

Rules that need data from outside the journey (for example "no earlier than the start of the
cohort") become named rules, like validators: `"rule": "NotBeforeCohortStart"`. This replaces the
four `*DateRules` classes.

The classes say that a rule in JSON can be dropped and nothing notices. Generic config tests cover
that risk: each `Date` question declares `notInFuture` or `"allowFuture": true`, each
`onOrBefore` and `onOrAfter` names a `Date` question in the same flow, and each named rule is
registered.

### 4. Question roles

A question declares what it means:

```json
{ "id": "first-name", "type": "FreeText", "role": "Pupil.FirstName" }
{ "id": "date-removed-from-roll", "type": "Date", "role": "Removal.Date" }
{ "id": "q-revised-grade", "type": "FreeText", "role": "Result.RevisedGrade", "clearWhen": "ResultChanged" }
```

`QuestionRole` is an enum in Application. Code asks for "the answer that has role
`Pupil.FirstName`", not "the answer to `first-name`". A few pages that code enters or names get a
`PageRole` in the same way, for example `Reason`, `Evidence` and `LearnerDetails`.

The role is copied onto each answer when the answer is saved. A submitted `RequestState` then does
not depend on the flow version that wrote it.

Roles replace most of groups 1 and 4:

- `AnswerFieldMap.QuestionToField` becomes the map from a role to a rules-engine field. The map
  stays in code.
- `EgressRecordBuilder` and `AddPupilJourney` read roles.
- `JourneyController` clears the answers that are marked `clearWhen: ResultChanged`.
- `JourneyViewModelBuilder` gets the summary values by role.

### 5. Option meanings

Zendesk and egress map option values such as `english-not-first-language` to codes. Give each
reason option a meaning from a closed list (a `RemovalReason` enum in Domain):

```json
{ "value": "english-not-first-language", "label": "...", "meaning": "Removal.EnglishNotFirstLanguage" }
```

The code that picks a Zendesk or egress code stays in C#, keyed on the meaning. A fixed code on the
option does not fit every case: `other` is code 329 with evidence and 330 without.

## Ids are keys in saved data

Question ids are keys in saved drafts and in submitted `RequestState` blobs. Do not rename a
question id in a flow that can have drafts. Add a new question with a new id.

Roles remove the need for code to know an id. After step 3, an id is only a key in the JSON.

## The guard: `FlowIdCouplingTests`

`tests/DfE.CheckPerformanceData.UnitTests/Journey/FlowIdCouplingTests.cs` reads every page id and
question id from the shipped flow files. It then scans every `.cs` and `.cshtml` file under `src/`
for a string literal that is equal to one of the ids. Comments are ignored.

It compares the result with a **baseline**: a list of each file and the ids that the file is
permitted to name.

- **A new id in a file, or a new file, fails the test.** Use a pattern above. If coupling is truly
  necessary, add it to the baseline with a reason, so the reviewer can see it.
- **A baseline entry that the code no longer uses fails the test too.** Remove it. The baseline can
  then only become smaller.

The test does not check option values: values such as `yes`, `other` and `1` occur too often in
code that has no relation to the flows. Step 5 adds option values to the guard.

## Order of work

Steps 2 to 5 each make the baseline smaller. Step 4 does not depend on step 3, so the two can run
at the same time.

1. **The guard.** Add `FlowIdCouplingTests` and its baseline. It stops new coupling.
2. **Generic answer condition (pattern 2).** Change the condition lists to hold an answer test.
   Move `Remove_KS4June.json`, `Remove_KS2.json` and `Remove_Post16.json` to answer tests. Delete
   the two condition classes. Removes 6 ids in 2 files.
3. **Question roles (pattern 4).** Five pull requests. Removes 82 ids in 12 files.
   1. Infrastructure: `QuestionRole`, `PageRole`, the role copied onto each saved answer, and
      generic config tests (a role occurs at most once in a flow, and each flow has the roles that
      its consumers need).
   2. `AddPupilJourney`, `DuplicateCheck.cshtml`, `AlreadyIncluded.cshtml`, `EditAdviceService`.
   3. `JourneyController`, `JourneyViewModelBuilder`, `ResultIssueController`.
   4. `EgressRecordBuilder`, `EgressViewModels`, `DevEgressController`.
   5. `AnswerFieldMap`, `OriginCountryLanguageCapture`, `OutcomePresets`, `SeedChangeRequests`.
      The queue message carries the role, and the worker maps on the role. Change the Web side and
      the worker in the same pull request.
4. **Date rules on the question (pattern 3).** One generic date validator, and `IDateRule` for
   named rules. Move one class at a time. Removes 22 ids in 4 files.
5. **Option meanings (pattern 5).** Key `ZendeskTicketFieldOptions`, `CorrectionCodes` and
   `ZendeskConsumer` on `RemovalReason`. Build `RequestTypeCode` from the meaning, not the option
   value. Then add option values to the guard. Removes 18 ids in 3 files.

The 3 baseline ids that stay are not coupling: guidance anchors and a database column.

## Decisions

| Decision | Step | Reason |
|---|---|---|
| Copy the role onto each saved answer. | 3 | A submitted request does not depend on the flow version. |
| Change the queue message and `RequestTypeCode` to carry roles and meanings, in one pull request with the worker. | 3 and 5 | The service is not live, so no queued message needs the old form. |
| Keep the integration maps in code, keyed on roles. | 3 | Keyed on roles, they name no flow id. |
