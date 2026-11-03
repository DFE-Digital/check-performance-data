# CYPMD Performance Tests

This project contains a reusable k6 performance and journey-testing framework for validating CYPMD amendment and Results Enquiry journeys.

The suite currently contains 25 registered journeys covering:

- KS4 amendments
- 16 to 19 amendments
- 16 to 19 Results Enquiries

Journeys are configuration-driven rather than being implemented as individual k6 scripts. Shared behaviour such as authentication, pupil selection, navigation, question handling, evidence upload, submission and cleanup is implemented once and reused by journey configurations.

> IMPORTANT: Concurrent load can fail until a known application defect involving duplicate request reference numbers is fixed. See "Known application defect: duplicate reference numbers".

---

# Quick start

The k6 entry point is:

```text
tests/journey.ts
```

## Type check

```bash
npx tsc --noEmit -p tsconfig.json
```

## Smoke test

Runs a single safe amendment journey:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=smoke tests/journey.ts
```

## Run a single journey

For example:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=single -e JOURNEY=remove-pupil-died tests/journey.ts
```

## Run Add Pupil

Add Pupil generates pupil data from `RUN_ID`, so it requires a `RUN_ID`:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=single -e JOURNEY=add-pupil -e RUN_ID=add-01 tests/journey.ts
```

## Full regression

The regression profile runs all 25 registered journeys sequentially:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=regression -e RUN_ID=reg-01 tests/journey.ts
```

> NOTE: Regression includes Add Pupil and Results Enquiry journeys. Neither is cleaned up, because both can be submitted as many times as needed, so the records they create remain in the environment.

## Repeat journeys without concurrency

Run 50 journeys sequentially, with a two-second pause after each journey:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=sequential -e SEQUENTIAL_ITERATIONS=50 -e SEQUENTIAL_PAUSE_SECONDS=2 tests/journey.ts
```

Do not combine any profile with CLI `--vus`, `--iterations`, `--duration` or `--stages`; the profile defines the execution model.

For example, do not run:

```bash
k6 run --vus 2 --iterations 2 -e PROFILE=regression tests/journey.ts
```

k6 can reject these options because they override the scenario configuration defined by the test framework.

---

# Profiles

Five execution profiles are supported.

| PROFILE | Journeys | Execution | Purpose |
| --- | --- | --- | --- |
| `smoke` | `remove-pupil-died` | 1 VU, 1 iteration | Check that the framework and target environment are working |
| `single` | Journey specified by `JOURNEY` | 1 VU, 1 iteration | Run and debug one journey |
| `regression` | All 25 registered journeys | Sequential, each journey once | Full functional regression of the performance-test journeys |
| `sequential` | Repeating weighted journey schedule | 1 VU, configured iteration count and pause | Repeated journeys without overlap within this test instance |
| `load` | Weighted journey schedule | Constant arrival rate | Concurrent workload execution |

If `PROFILE` is not supplied:

- `JOURNEY=all` selects `regression`
- any other `JOURNEY` selects `single`
- specifying neither is an error

---

# Safety and known limitations

## Known application defect: duplicate reference numbers

Concurrent amendment submissions can fail because the application can attempt to persist duplicate `ReferenceNumber` values.

The observed failure is:

```text
PostgreSQL: 23505
IX_ChangeRequests_ReferenceNumber
HTTP 500
```

This was reproduced with overlapping submissions.

The performance framework deliberately does not retry the failed submission or otherwise hide this behaviour. If the defect occurs, the performance test reports a failure.

## Results Enquiry and Add Pupil records remain in the environment

Submitted Results Enquiries cannot currently be deleted or withdrawn using the application.

Results Enquiry and Add Pupil journeys are not cleaned up by the framework, because they can be submitted as many times as needed. Every execution of either journey therefore leaves its record behind.

Results Enquiry records must be cleaned up separately using an appropriate database or administrative process where required.

## Amendment cleanup

Amendment requests created by the framework are cleaned up after successful submission. The application allows only one amendment request per pupil, so without cleanup the same pupil could not be amended again.

If the cleanup call fails, the framework tries once more when the journey has a reference number. If the journey fails before it gets a reference number, the framework cannot clean up. Find and withdraw that request by hand in the application.

The implementation lives in:

```text
journeys/cleanup-request.ts
```

Submitted requests are withdrawn by the application, which leaves them in the `Withdrawn` state. The request is not physically deleted.

---

# Architecture

## Amendment flow

A typical amendment journey follows this server-side flow:

```text
Check Your Pupil Data
        ↓
What To Change
        ↓
Pupil Search
        ↓
Question Flow
        ↓
Summary
        ↓
Submit
        ↓
Confirmation
        ↓
Cleanup
    (request withdrawn)
```

Not every journey contains every step. For example, Add Pupil does not start with a pupil search.

Results Enquiry is a separate flow family and has its own start and submission behaviour.

---

# Project structure

The framework is organised into the following layers:

- `journey-config/`: describes individual journeys and contains the central journey registry
- `journeys/`: reusable journey flow functions
- `lib/`: shared HTTP, assertions, authentication, configuration, workload and test-data helpers
- `types/`: shared TypeScript types
- `tests/`: k6 entry point

```text
DfE.CheckPerformanceData.PerformanceTests/
├── data/
│   └── evidence-upload.pdf
├── journey-config/
│   ├── registry.ts
│   ├── ks4/
│   │   ├── add/
│   │   │   └── add-pupil.ts
│   │   ├── include/
│   │   │   └── include-pupil.ts
│   │   ├── merge/
│   │   │   └── merge-duplicate-pupils.ts
│   │   └── remove/
│   │       └── *.ts
│   └── post16/
│       ├── shared.ts
│       ├── merge/
│       │   └── merge-duplicate-students.ts
│       ├── remove/
│       │   └── *.ts
│       └── results-enquiry/
│           ├── pupils.ts
│           └── *.ts
├── journeys/
│   ├── answer-question.ts
│   ├── cleanup-request.ts
│   ├── find-draft-request.ts
│   ├── pupil-search.ts
│   ├── run-journey.ts
│   ├── save-request.ts
│   ├── start-amendment.ts
│   ├── start-results-enquiry.ts
│   ├── submit-request.ts
│   └── upload-evidence.ts
├── lib/
│   ├── workload.ts
│   ├── journey-selection.ts
│   ├── metrics.ts
│   ├── run-id.ts
│   ├── navigation.ts
│   ├── assertions.ts
│   ├── antiforgery.ts
│   ├── auth.ts
│   ├── config.ts
│   ├── pupil-selection.ts
│   ├── test-data.ts
│   └── windows.ts
├── tests/
│   └── journey.ts
└── types/
    └── ...
```

`tests/journey.ts` is the only k6 entry point.

---

# Journey runner

## `journeys/run-journey.ts`

`run-journey.ts` is the generic journey engine.

A journey is primarily described by a `JourneyConfig`, rather than being implemented as a standalone script.

For amendment journeys the runner:

1. starts the amendment
2. performs pupil search where configured
3. executes configured question steps
4. handles evidence upload where required
5. reaches the summary
6. completes the journey using its configured completion mode

The configured completion mode can be:

```text
submit
```

or:

```text
save-for-later
```

The runner returns:

```text
{ referenceNumber, completion }
```

No individual amendment type is hard-coded into the generic runner.

---

# Amendment bootstrap

## `journeys/start-amendment.ts`

Implements the common amendment startup flow used by amendment journeys.

The appropriate `WindowProfile` determines the window-specific route and page expectations.

---

# Results Enquiry bootstrap

## `journeys/start-results-enquiry.ts`

Results Enquiry uses a separate start flow rather than forcing Results Enquiry behaviour through the amendment bootstrap.

Results Enquiry configs can also define `preSteps` that are executed before pupil selection.

---

# Pupil search

## `journeys/pupil-search.ts`

Handles pupil/student search and selection.

The journey configuration controls values including:

- page ID
- expected heading
- expected redirect
- pupil filtering where required

The framework also supports journeys requiring more than one pupil search, such as Merge Duplicate Pupils.

---

# Question engine

## `journeys/answer-question.ts`

Reusable engine for answering configured journey pages.

Answers can contain:

- individual string values
- repeated or multi-value fields
- values calculated for the current iteration

Iteration-aware answers are used by journeys such as Add Pupil, where generated test data must differ between iterations.

---

# Evidence upload

## `journeys/upload-evidence.ts`

Handles evidence-file upload.

The live evidence flow uses the evidence page's upload `formaction` and performs a multipart POST.

The observed flow is:

```text
Evidence page
      ↓
multipart upload
      ↓
302 back to evidence page
      ↓
verify uploaded filename
      ↓
refresh antiforgery token
      ↓
submit evidence answers
```

The default evidence file is:

```text
data/evidence-upload.pdf
```

---

# Completion

## `journeys/submit-request.ts`

Submits an amendment from its summary page and obtains the resulting reference number from the confirmation page.

## `journeys/save-request.ts`

Uses the application's "Save and continue later" behaviour.

The button field name and form action are read from the page rather than being hard-coded.

## `journeys/find-draft-request.ts`

Finds a saved draft associated with the relevant pupil.

The helper throws unless exactly one draft matches.

This is intentional. Ambiguous matching must not result in one test run cleaning up another run's request.

---

# Amendment request cleanup

## `journeys/cleanup-request.ts`

This performs post-test cleanup for amendment requests. Cleanup lets the same pupil be amended again, because the application allows only one amendment request per pupil.

Draft requests are deleted. Submitted requests are withdrawn by the application, which leaves them in the `Withdrawn` state.

`tests/journey.ts` performs this cleanup after successful amendment submission. Add Pupil and Results Enquiry journeys are not cleaned up, because they can be submitted as many times as needed.

---

# Journey registry

## `journey-config/registry.ts`

The registry contains every journey available to the test runner.

`lib/journey-selection.ts` uses the registry for:

- single-journey lookup
- regression ordering
- journey classification
- weighted workload selection

## KS4 journeys

```text
add-pupil
include-pupil
merge-duplicate-pupils
remove-admitted-following-exclusion
remove-admitted-from-abroad
remove-child-missing-education
remove-dual-registered-moved
remove-elective-home-education
remove-life-limiting-illness
remove-permanently-excluded
remove-permanently-left-england
remove-pupil-died
remove-social-care-involvement
remove-year-group-change-higher
remove-year-group-change-lower
```

## 16 to 19 journeys

```text
post16-merge-duplicate-students
post16-remove-not-at-end
post16-remove-not-on-roll
post16-remove-other
post16-remove-student-died
post16-results-enquiry-incorrect-grade-not-whole-cohort
post16-results-enquiry-incorrect-grade-whole-cohort
post16-results-enquiry-missing-qualification-not-whole-cohort
post16-results-enquiry-missing-qualification-whole-cohort
post16-results-enquiry-result-does-not-belong
```

There are currently 25 registered journeys.

---

# Results Enquiry (16 to 19)

The `ResultsEnquiry` journey type covers the 16 to 19 Results Enquiry flow.

Configs are stored in:

```text
journey-config/post16/results-enquiry/
```

| Journey ID | Config file | Selection |
| --- | --- | --- |
| `post16-results-enquiry-incorrect-grade-not-whole-cohort` | `incorrect-grade-not-whole-cohort.ts` | Single student from enquiry pool |
| `post16-results-enquiry-incorrect-grade-whole-cohort` | `incorrect-grade-whole-cohort.ts` | Cohort count 2, then student from enquiry pool |
| `post16-results-enquiry-missing-qualification-not-whole-cohort` | `missing-qualification-not-whole-cohort.ts` | Single student from enquiry pool |
| `post16-results-enquiry-missing-qualification-whole-cohort` | `missing-qualification-whole-cohort.ts` | Cohort count 5, then student from enquiry pool |
| `post16-results-enquiry-result-does-not-belong` | `result-does-not-belong.ts` | Single student from enquiry pool |

Routes, live page headings and submitted answer values were confirmed against the running application when the configs were created.

Each config uses `preSteps` where necessary to answer issue-type and cohort-scope questions before pupil search.

## Results Enquiry pupil pool

Enquiry pupils are derived at load time from the 16 to 19 results blob, set by `resultsBlob` in `lib/windows.ts`:

```text
exercises/346ccd29-e9fb-4e33-a47a-da9210c8e305/releases/48b54c09-2bc0-4f61-8993-0d0474d2569c/data/8604070_results.json
```

`lib/test-data.ts` matches each results row to a pupil by `CYPMD_ID` and sets the pupil's `resultKey` to `QAN|SESSION|SOURCE` from the pupil's first result. Pupils with no result are not enquiry pupils.

Results Enquiry configurations use `isEnquiryPupil` (pupil has a `resultKey`) as the pupil filter, and `enquiryResultKey` to read the key.

Selection is deterministic:

```text
iteration % pupils.length
```

Record IDs and result keys are regenerated when the data is seeded, so `CYPMD_ID` is used as the join key and nothing is hard-coded.

> NOTE: The pool follows the results blob of the current release. Changing the release changes the pool.

## Results Enquiry cleanup

Results Enquiries cannot currently be deleted through the application. They can be submitted as many times as needed, so `tests/journey.ts` does not call cleanup for `ResultsEnquiry`.

Each Results Enquiry execution leaves one submitted record behind.

---

# Window profiles

## `lib/windows.ts`

Window-specific behaviour is defined using `WindowProfile`.

Each profile contains:

- window ID
- pupil/student data source
- expected page headings
- other window-specific configuration

Current profiles:

| Key | Window | Pupil data | Headings |
| --- | --- | --- | --- |
| `ks4` | KS4 | `exercises/71e0ee21-3f3f-4ba6-945d-a1b1edc1bf3b/releases/1d8dba47-63e4-48f7-b029-039f50b58cd9/data/8604070_pupils.json` | "Check your pupil data" / "What pupil data would you like to change?" |
| `post16` | 16 to 19 | `exercises/41476d6c-20b6-420b-887c-a531bea3c74c/releases/5ac44d35-81a3-490d-8e5f-424cec112335/data/8604070_pupils.json` | "Check your student data" / "What student data would you like to change?" |

A journey selects 16 to 19 using:

```typescript
window: "post16"
```

A configuration without an explicit `window` uses KS4.

16 to 19 pupil suggestions exclude non-included records by default.

16 to 19 journeys use `isIncluded` from:

```text
journey-config/post16/shared.ts
```

to select included students.

---

# Shared libraries

## `lib/navigation.ts`

Provides common HTTP navigation and form-post functionality, including multi-value form data and multipart upload support.

## `lib/assertions.ts`

Provides shared response and page assertions.

Failure diagnostics include GOV.UK error-summary information where available.

## `lib/antiforgery.ts`

Handles antiforgery-token extraction used by forms.

## `lib/auth.ts`

Handles test authentication.

Authentication is performed for each iteration because k6 can clear its cookie jar between iterations.

The test environment uses the application's development impersonation endpoint rather than browser-based DfE Sign-in.

## `lib/config.ts`

Contains common framework configuration.

`BASE_URL` defaults to:

```text
http://localhost:8080
```

Override it using:

```text
-e BASE_URL=<target>
```

## `lib/journey-selection.ts`

Handles:

- journey lookup
- journey classification
- regression ordering
- weighted load selection

## `lib/workload.ts`

Defines:

- profiles
- k6 scenarios
- thresholds
- workload validation
- safety guards

## `lib/metrics.ts`

Defines custom metrics used to determine whether expected business journeys actually completed.

These include:

```text
journey_completion
journeys_completed
```

---

# Configuration-driven testing

Journey behaviour is primarily expressed as configuration.

Example:

```typescript
{
    id: "remove-pupil-died",
    journeyType: "Remove",
    reason: "Pupil died",
    pupilSearch: {
        pageId: "select-pupil",
        expectedHeading: "What is the name of the pupil to be removed?",
        expectedRedirect: "/page/reason",
    },
    completion: "submit",
    steps: [
        {
            pageId: "reason",
            expectedHeading: "Why are you asking to remove",
            answers: {
                Q_Reason: "pupil-died",
            },
            expectedRedirect: "page/pupil-died",
        },
    ],
}
```

This allows new journeys to reuse the same HTTP and flow implementation where the application behaviour is genuinely shared.

---

# Environment variables

| Variable | Used by | Meaning |
| --- | --- | --- |
| `BASE_URL` | all | Target URL. Defaults to local application |
| `PROFILE` | all | `smoke`, `single`, `regression`, `sequential` or `load` |
| `JOURNEY` | single/regression | Journey ID. `all` is accepted with regression |
| `RUN_ID` | Add Pupil | Deterministic run identifier |
| `SEQUENTIAL_ITERATIONS` | sequential | Required positive whole number of journeys to run |
| `SEQUENTIAL_PAUSE_SECONDS` | sequential | Required pause after each journey; finite number of at least 0, including fractions |
| `LOAD_RATE_PER_MINUTE` | load | Iterations started per minute |
| `LOAD_DURATION` | load | Load duration using `Ns` or `Nm` format |
| `LOAD_PREALLOCATED_VUS` | load | Number of pre-allocated VUs |
| `LOAD_MAX_VUS` | load | Maximum VUs; must be at least the pre-allocated VU count |
| `JOURNEY_WEIGHTS` | load/sequential | JSON journey-to-weight mapping |

Profile-specific variables are rejected under other profiles. `JOURNEY_WEIGHTS` is accepted only by `load` and `sequential`.

---

# RUN_ID and Add Pupil

`add-pupil` generates deterministic test data from `RUN_ID`.

The generated surname is:

```text
Add + 5-letter RUN_ID hash + 3-letter iteration code
```

Example:

```text
Addjrmfcaaa
```

Properties:

- the same `RUN_ID` and iteration always generate the same surname
- different iterations in one run receive different surnames
- the generated value contains letters only
- up to 17,576 iteration codes are supported
- `RUN_ID` accepts 1–32 letters, digits, `-` or `_`
- uncontrolled randomness is not used

A different `RUN_ID` should be used when fresh Add Pupil data is required.

Different `RUN_ID` values can rarely map to the same 5-letter hash code, so use distinct descriptive identifiers.

`RUN_ID` is mandatory when an execution can include `add-pupil`.

Missing or invalid configuration fails before application requests are sent.

Example:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=single -e JOURNEY=add-pupil -e RUN_ID=run-01 tests/journey.ts
```

---

# JOURNEY_WEIGHTS

The load and sequential profiles can select journeys using weighted scheduling.

`JOURNEY_WEIGHTS` is JSON mapping journey IDs to integer weights.

Example:

```json
{
  "remove-pupil-died": 2,
  "include-pupil": 1
}
```

Unlisted journeys receive weight `0`.

Each configured weight must be between:

```text
0 and 1000
```

and the total must not exceed:

```text
10000
```

Invalid journey IDs or invalid weights cause configuration validation to fail before application requests are sent.

## Default workload mix

The default mix for load and sequential profiles is provisional.

It currently gives weight `1` to each amendment journey that needs cleanup. Add Pupil and Results Enquiry receive weight `0`. Include them with `JOURNEY_WEIGHTS` if required.

These weights do not represent agreed production traffic volumes.

Production-like workload percentages must be agreed before using the profile for representative performance testing.

---

# Sequential profile

`PROFILE=sequential` repeats the weighted journey schedule using one VU for `SEQUENTIAL_ITERATIONS` iterations. Each journey, including amendment cleanup, finishes before the next starts. Unlike regression, the schedule repeats until the configured iteration count is reached; it does not necessarily cover every journey.

`SEQUENTIAL_PAUSE_SECONDS` replaces the usual one-second end-of-iteration pause. It applies after successful journeys and failures caught by the journey runner, including after the final journey. Set it to `0` for back-to-back journeys or a positive number for a gap between journeys.

The maximum duration is two hours, including pauses. Choose an iteration count and pause that fit within that limit. The completion threshold requires exactly the configured number of successful journeys; failures or a duration limit that prevents completion cause the test to fail.

The profile uses the same default weights and optional `JOURNEY_WEIGHTS` as load. Add Pupil and Results Enquiry are excluded by default. If Add Pupil is included, `RUN_ID` is required and the iteration count cannot exceed 17,576.

Throughput depends on journey duration plus the pause; there is no fixed arrival rate or queue of scheduled starts. One VU prevents overlapping journeys within this test instance only, not requests from other test processes or application users.

---

# Load profile

The load profile uses a constant arrival-rate workload.

A load execution requires:

```text
LOAD_RATE_PER_MINUTE
LOAD_DURATION
LOAD_PREALLOCATED_VUS
LOAD_MAX_VUS
```

Example structure:

```bash
k6 run -e PROFILE=load -e RUN_ID=load-01 -e LOAD_RATE_PER_MINUTE=<rate> -e LOAD_DURATION=<duration> -e LOAD_PREALLOCATED_VUS=<vus> -e LOAD_MAX_VUS=<max> tests/journey.ts
```

> NOTE: The load-profile example deliberately contains placeholders. No production traffic rate, concurrency model or journey distribution has yet been agreed.

---

# Pass criteria

Do not use the k6 process exit code alone as proof that a run succeeded.

A run passes only when:

- all configured thresholds pass
- no unexpected HTTP failures occur
- all checks pass
- every expected journey reaches its successful business completion point

Successful completion also produces diagnostic output containing the submitted reference, for example:

```text
Completed (submit): reference ...
```

These log entries are useful for investigation and cleanup, but are not the sole pass criterion.

Current correctness thresholds include:

| Threshold | Meaning |
| --- | --- |
| `checks rate==1` | Every check must pass |
| `http_req_failed rate==0` | No unexpected HTTP failures |
| `journey_completion rate==1` | Every journey must reach its expected completion point |
| `journeys_completed count==N` | Expected number of completed journeys |

For `smoke` and `single`, `N` is:

```text
1
```

For `regression`, `N` is:

```text
25
```

A journey-count threshold is not used for the load profile.

---

# Response-time thresholds

Selected submit and cleanup transactions currently have a provisional threshold:

```text
p(95) < 1000 ms
```

This is a development threshold only.

It is not an agreed SLO, SLA or production performance requirement.

Formal response-time targets should be agreed before using these thresholds as acceptance criteria.

Cleanup transaction thresholds are included only when the selected workload contains amendment journeys that need cleanup.

---

# Metric tags

Each iteration tags metrics with:

```text
journey
journey_type
window
```

This allows metrics to be analysed by individual journey and flow type.

Authentication and pupil loading performed during `setup()` are not tagged as journey execution.

---

# Regression profile

Regression runs all registered journeys exactly once and sequentially.

Current expected journey count:

```text
25
```

A regression run includes:

- KS4 Remove journeys
- KS4 Merge
- KS4 Include
- KS4 Add
- 16 to 19 Remove journeys
- 16 to 19 Merge
- 16 to 19 Results Enquiry journeys

Regression requires:

```text
RUN_ID
```

Example:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e PROFILE=regression -e RUN_ID=reg-01 tests/journey.ts
```

Do not add:

```text
--vus
```

or:

```text
--iterations
```

to this command.

Regression controls its own sequential execution.

---

# Capturing output

## PowerShell

```powershell
k6 run <options> tests/journey.ts *> "$env:TEMP\run.txt"
```

## Bash

```bash
k6 run <options> tests/journey.ts > "${TMPDIR:-/tmp}/run.txt" 2>&1
```

---

# Authentication

The performance-test environment authenticates using:

```text
/dev/impersonate/{role}
```

rather than exercising the browser-based DfE Sign-in flow.

Authentication runs for each iteration so that an iteration does not rely on authentication state retained by a previous iteration.

---

# Test data and cleanup considerations

Performance tests write data to the target application.

The expected cleanup behaviour depends on journey type.

| Type | Behaviour |
| --- | --- |
| Amendment | Cleaned up after submission. The request becomes `Withdrawn`, so the same pupil can be amended again |
| Add Pupil | Not cleaned up. Each submission creates a generated pupil record that remains |
| Results Enquiry | Not cleaned up. Each submission creates a record that remains; no application cleanup mechanism has been identified |

Use appropriate non-production environments for executions that write test data.

---

# Known limitations

## Application concurrency

Concurrent amendment submissions can fail with:

```text
PostgreSQL 23505
IX_ChangeRequests_ReferenceNumber
```

The framework does not retry the failure.

## Results Enquiry accumulation

Submitted Results Enquiry records cannot currently be removed by the test framework through the application.

## Results Enquiry pupil pool

The Results Enquiry pupil/result pool is read from the results blob of the post16 window's current release, so it follows that release.

Recheck the pool after the window's release changes.

## Workload distribution

The default journey weights are provisional and are not based on agreed production usage statistics.

## Response-time targets

The current:

```text
p(95) < 1000 ms
```

limit is provisional and is not an agreed service-level target.

---

# Current verification baseline

The current full sequential regression baseline is:

```text
25 journeys
1714 checks
0 failed checks
0 unexpected failed HTTP requests
```

This baseline was established using sequential execution. Two consecutive sequential runs both gave 1714 checks.

It does not demonstrate successful concurrent load behaviour because concurrent execution is currently constrained by the known application duplicate-reference defect.

Changes to the number of configured checks may legitimately change the expected `1714` total. Journey completion and threshold results remain the primary regression criteria.

---

# Development guidance

When adding a new journey:

1. Identify the equivalent E2E behaviour where available.
2. Confirm live routes, headings, form actions and submitted values rather than assuming the E2E representation exactly matches the current application.
3. Prefer adding a `JourneyConfig` when the existing runner already supports the flow.
4. Add generic framework behaviour only when a genuinely reusable capability is missing.
5. Verify the new journey independently using the `single` profile.
6. Require zero failed checks and zero unexpected HTTP failures.
7. Confirm the expected business completion point.
8. Confirm cleanup behaviour.
9. Run the complete sequential regression afterwards.
10. Do not work around genuine application failures simply to produce a passing performance test.

The live application behaviour is the final source of truth when older E2E tests, page-object names or historical assumptions differ from the running service.

---

# Type checking

Run:

```bash
npx tsc --noEmit -p tsconfig.json
```

A clean type check should be maintained alongside successful k6 verification.