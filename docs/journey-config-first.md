# Journeys are configuration, not code

Date: 2026-10-27

Status: In progress. Steps 1, 2 and 4 are done.

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

Since then, more and more code knows about one journey. On 2026-10-29, the baseline in
`FlowIdCouplingTests` holds 102 ids in 20 files. 15 files are journey code, 3 are dev tools or
seed data, and 2 use the same text with a different meaning.

| Group | Files | Example | Why it is there |
|---|---|---|---|
| Integration maps | `AnswerFieldMap`, `EgressRecordBuilder`, `ZendeskTicketFieldOptions`, `CorrectionCodes`, `ZendeskConsumer`, `EgressViewModels` | `["date-removed-from-roll"] = "dateOfRemoval"` | The rules engine, the egress file and Zendesk need to know what an answer means. |
| Single-journey conditions | `OriginCountryLanguageCapture` | `"country-originally-from"` | It reads the origin country answer, by id, to find the languages of that country. |
| Journey code in the engine and Web | `JourneyViewModelBuilder`, `JourneyController`, `AddPupilJourney`, `ResultIssueController`, `EditAdviceService`, `EnquiryQuestionIds`, two views | `"q-cohort-scope"`, `"q-revised-grade"`, `"learner-details"` | The Web layer needs one answer for a summary, or must clear one answer when the selection changes. |

## The rule

**Code may depend on a closed vocabulary that the engine defines. Code must not depend on a name
that the author of a journey chooses.**

- Closed vocabulary: page types, question types, condition names, validator names, date checks,
  date fact names, question **roles** and option **meanings**. Each is an enum or a registered name.
- Names that the author chooses: page ids, question ids, option values, titles.

When code needs to know what a question means, the question declares its role in the JSON. The
code then searches for the role, not for the id.

## Patterns that replace journey-specific code

### 1. Branch on an answer that is already on the path

If the deciding answer is on an earlier page, use a radio option's `nextPageId` to go to a
different page. Do not write a condition. The flow engine already handles a changed answer: from
the summary, `PagePost` sees that the next page changed, trims the history and sends the user down
the new branch.

Use this pattern only when the page order allows it. Use a fact only for something that is not in
the answers: school type, the not-on-roll college list, add-back status.

### 2. One generic answer condition

Let `visibleWhen`, `optionalWhen` and `requireAtLeastOneWhen` take an answer test, not only a
condition name. All the entries in a list must be true:

```json
"optionalWhen": [
  { "answer": "reason", "in": ["english-not-first-language"] },
  { "answer": "first-language", "in": ["other", "believed-other", "chose-not-to-say", "not-known"] }
]
```

This replaced `EalEvidenceIsOptionalCondition` and `NotOnRollReasonIsOtherCondition`.

A fact test does the same for a fact that is not an answer. Code gives the fact, and the flow file
gives the logic. `"is": false` replaces a "not" class, and `anyOf` replaces an "or" class:

```json
"visibleWhen": [
  { "fact": "pupil.isAddBack", "is": false },
  { "anyOf": [
    { "fact": "school.isIndependent", "is": true },
    { "fact": "school.isOnNotOnRollList", "is": true }
  ] }
]
```

This replaced `PupilIsNotAddBackCondition`, `SchoolIsNotIndependentCondition` and
`SchoolCanRecordNotOnRollCondition`. A fact test always names its `"is"` value. A plain string is not allowed. The format is in [question-flows.md](question-flows.md#conditions-visiblewhen-optionalwhen-requireatleastonewhen).

A missing or blank answer fails closed, the same as an unknown fact: `visibleWhen` hides,
`optionalWhen` keeps the question mandatory, and `requireAtLeastOneWhen` keeps the rule on.

The test names a question id, but the id is in the JSON. A generic config test checks that the
question is in the same flow and that each value is one of its options.

### 3. Declare date rules on the question

A `Date` question holds a `dateRules` list. Each rule has a check, a target and its own
message:

```json
{ "id": "date-pupil-started", "type": "Date",
  "dateRules": [
    { "check": "notInFuture", "message": "The date {pupilName} started at your school must be in the past" },
    { "check": "onOrAfter", "fact": "cohort.start", "message": "..." },
    { "check": "onOrAfter", "question": "date-pupil-started-school-in-england", "message": "... {date} ..." }
  ] }
```

The checks are `notInFuture`, `onOrBefore` and `onOrAfter`. A comparison names one target: another
`question`, a date `fact`, or a fixed `date`. The rules run in list order, and the first rule that
fails gives the message.

A date that the answers do not hold (for example the start of the cohort) is a **date fact**. An
`IJourneyDateFact` gives the date, and the flow file holds the rule. This is the same split as for
boolean facts in pattern 2.

This replaced the four `*DateRules` classes. The format is in
[question-flows.md](question-flows.md#date-rules-daterules).

A rule in JSON can be dropped, and nothing at run time notices. Generic config tests
(`DateRuleConfigTests`) cover that risk:

- each `Date` question has `notInFuture` as its first rule;
- only `Date` questions have rules, and each rule has a message;
- `notInFuture` names no target, and a comparison names exactly one;
- each `question` target is a `Date` question in the same flow;
- each comparison between two questions has its mirror rule on the other question;
- each date fact has an implementation, and the implementation is registered.

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
reason option a meaning:

```json
{ "value": "english-not-first-language", "label": "...", "meaning": "Removal.EnglishNotFirstLanguage" }
```

The codes for each meaning are in one JSON file, `Data/Integration/removal-reasons.json`. It ships
in the Web image and in the worker image, as the flow files do. Each row is one meaning:

```json
{
  "Removal.EnglishNotFirstLanguage": {
    "zendeskReasonForRemoval": "admitted_from_abroad_with_english_not_first_language",
    "correctionReason31": "2_31",
    "ldsCorrectionCode": "2"
  },
  "Removal.Other": {
    "zendeskReasonForRemoval": {
      "withEvidence": "other_-_with_evidence",
      "withoutEvidence": "other_-_evidence_not_required"
    },
    "ldsCorrectionCode": { "withEvidence": "329", "withoutEvidence": "330" }
  },
  "Removal.NotOnRoll.Apprentice": {
    "zendeskReasonForRemoval": "not_on_roll_apprentice",
    "ldsCorrectionCode": "331"
  }
}
```

- **The rows are the closed list.** No `RemovalReason` enum is necessary. A new removal reason is
  a flow change and a new row, with no C#.
- **Evidence.** A value is a string, or a `withEvidence` and `withoutEvidence` pair. The code
  decides "has evidence" once. It does not know which meanings use a pair.
- **Sub-reasons.** Each Post16 `not-on-roll-reason` option has its own meaning, for example
  `Removal.NotOnRoll.Apprentice`. The worker uses the most specific meaning on the request.
- **One meaning for many values.** `pupil-died` and `student-died` both have the meaning
  `Removal.Deceased`.
- **Fail closed.** A missing row or a missing target leaves the field out and logs a warning.

These stay in code: the evidence test, the KS4 add-back test on `Pincl` (a fact about the pupil,
not an answer), and the code that reads the file. The Zendesk field ids are already configuration
in `ZendeskTicketFieldSettings`.

Generic config tests check that each option `meaning` in a flow has a row, that each row is used
by a flow, that no value is empty, and that each pair has both sides.

The `DecisionReasonApproved` and `DecisionReasonRejected` maps are keyed on the rules-engine
outcome, not on a journey option. They are not part of this pattern.

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

## Check each step by hand

Each step must change no behaviour that a user or a downstream system sees. Before a step's pull
request merges, check by hand in the browser each journey that the step changes:

- each branch that the changed code or flow file controls, to the confirmation page;
- the error messages on those pages, with an empty submit;
- the "(Optional)" labels and the summary page rows;
- the Zendesk ticket in the dev outbox, for a request that the step changes.

Name the journeys that you checked in the pull request description. If a difference is wanted, the
step is wrong for this plan. Fix the code.

## Order of work

Steps 2 to 5 each make the baseline smaller.

1. **Done. The guard.** Add `FlowIdCouplingTests` and its baseline. It stops new coupling.
2. **Done. Generic answer condition (pattern 2).** Change the condition lists to hold an answer test.
   Move `Remove_KS4June.json`, `Remove_KS2.json` and `Remove_Post16.json` to answer tests. Delete
   the two condition classes. Removes 6 ids in 2 files.
3. **Question roles (pattern 4).** Five pull requests. Removes 81 ids in 15 files.
   1. Infrastructure: `QuestionRole`, `PageRole`, the role copied onto each saved answer, and
      generic config tests (a role occurs at most once in a flow, and each flow has the roles that
      its consumers need).
   2. `AddPupilJourney`, `DuplicateCheck.cshtml`, `AlreadyIncluded.cshtml`, `EditAdviceService`.
   3. `JourneyController`, `JourneyViewModelBuilder`, `EnquiryQuestionIds`, `ResultIssueController`.
   4. `EgressRecordBuilder`, `EgressViewModels`, `DevEgressController`.
   5. `AnswerFieldMap`, `OriginCountryLanguageCapture`, `OutcomePresets`, `SeedChangeRequests`.
      The queue message carries the role, and the worker maps on the role. Change the Web side and
      the worker in the same pull request.
4. **Done. Date rules on the question (pattern 3).** One generic date rule evaluator, and
   `IJourneyDateFact` for dates from outside the journey. Removed 22 ids in 4 files.
5. **Option meanings (pattern 5).** Add `meaning` to the reason options. Move the reason maps in
   `ZendeskTicketFieldOptions`, `CorrectionCodes` and `ZendeskConsumer` to
   `removal-reasons.json`. Build `RequestTypeCode` from the meaning, not the option value. Then
   add option values to the guard. Removes 18 ids in 3 files.

The 3 baseline ids that stay are not coupling: guidance anchors and a database column.

## Decisions

| Decision | Step | Reason |
|---|---|---|
| Copy the role onto each saved answer. | 3 | A submitted request does not depend on the flow version. |
| Change the queue message and `RequestTypeCode` to carry roles and meanings, in one pull request with the worker. | 3 and 5 | The service is not live, so no queued message needs the old form. |
| Keep the integration maps in code, keyed on roles. | 3 | Keyed on roles, they name no flow id. |
| Put the removal reason codes in `removal-reasons.json`, keyed on meaning. The rows are the list of meanings. | 5 | A new reason needs no C#. One table holds the Zendesk and LDS codes. |
| Date facts give a date, and the flow holds the rule. A fact with no value skips the rule and logs a warning. | 4 | The same split as boolean facts. A null fact cannot happen today, and the rules engine checks the same boundary again. |
