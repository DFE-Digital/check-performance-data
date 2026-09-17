# Data egress to LDS (AB#294553 / spec AB#292610)

A back-office admin section where a CYPMD ops user pulls the scrutiny decisions for one checking
window's Add and Remove amendment requests, preprocesses the approved ones into LDS-spec CSV
files, persists the processed records in the database, and transfers the files to the LDS storage
account. Everything lives inside the web app — no separate worker or scheduled job.

## 1. What it does

Five screens under `/admin/egress`, gated by `[RequireAdminSection(AdminNavKeys.Egress)]`:

- **Pull** (`GET`/`POST /admin/egress`) — choose a checking window and one or more output types
  (New learners, Remove learners), then pull. Also lists saved (in-progress) and completed runs.
- **Results** (`GET /admin/egress/runs/{id}/results`) — every pulled request, one tab per output
  type, with its raw Zendesk decision and CYPMD values, before any filtering happens.
- **Preprocessing** (`GET /admin/egress/runs/{id}/preprocessing`, plus
  `GET .../preprocessing/stream` for the SSE progress feed and `POST .../preprocessing` for the
  no-JS run-to-completion fallback) — runs the eight-step pipeline and shows live progress.
- **Failed** (`GET /admin/egress/runs/{id}/failed`) — reached only when preprocessing found a
  problem; lists every failure.
- **Summary** (`GET /admin/egress/runs/{id}/summary`, `POST .../transfer`) — confirms the target
  container and file list, with a preview (`GET .../preview/{outputType}`) and download
  (`GET .../download/{outputType}`) per file, then transfers.
- **Complete** (`GET /admin/egress/runs/{id}/complete`) — the transfer summary: files, record
  counts, hashes, who did it and when.

`GET /admin/egress/runs/{id}` (Resume) sends the browser to whichever of these a run's status
implies, so a saved run reopens without re-pulling. `POST /admin/egress/runs/{id}/abandon` ends a
run at any non-terminal point.

## 2. Where the data comes from

Zendesk supplies **only the decision**: the "Decision status" custom field on the ticket id already
stored on `ChangeRequests.CrmId`, read via `GET /api/v2/tickets/show_many.json` (100 ids per call).
Every other value — pupil details, dates, the school's establishment number, the journey's answers
— comes from CYPMD's own data: the `ChangeRequests` row and the persisted journey blob
(`requests/{reference}.json`, `IRequestStateBlobClient`). Production configures no Zendesk custom
fields at all, and an Add (new learner) request carries no establishment number, admission date,
year group or SEN status in Zendesk — so the database has to be the source of truth regardless.

`ChangeRequests.OrganisationLaestab` is a new column, written at submit time from the DfE Sign-In
`organisation_laestab` claim (`RequestService.OrganisationLaestabOrNull`). It exists because a new
learner's pupil record is synthetic and carries no LAESTAB of its own — the school's LAESTAB from
the request row is the only source for that case.

The pull's output, `EgressSourceRecord`, is the "as pulled" shape shown on Results and persisted as
the run's raw JSON (so a resume never re-pulls): identifiers (`ChangeRequestId`, `ReferenceNumber`,
`TicketId`), the `Decision` (a Zendesk value, or `EgressDecisions.NoTicket` / `NotFound`), the
output type and window type, submission metadata, the school's URN/LAESTAB, every pupil field
CYPMD holds, and the journey's answers flattened by question id (`EgressAnswers.Flatten`).

## 3. Run lifecycle

```
Pulled → Preprocessing → (PreprocessingFailed | Preprocessed) → Transferring → (TransferFailed | Transferred)
```

`Abandoned` is reachable from any non-terminal status, including `Preprocessing` and
`Transferring` — the lock has no expiry, so a run stuck there after a pod restart or a crashed
tab must always be releasable, and every screen from Preprocessing onwards carries an Abandon
form. Abandoning a `Transferring` run also sweeps any blob it actually wrote (matched by the
`egressRunId` metadata stamped on upload) before releasing the pair, so a same-named retry never
collides with an orphaned file; the confirmation banner names what was removed, if anything, or
says nothing was transferred. A run is persisted the moment the pull succeeds
(`EgressRunService.StartAsync` → `IEgressRunRepository.CreateRunAsync`), so "Save and exit" on any
screen is just leaving the page — there is no separate save action. Resume
(`EgressController.Resume`) maps status to screen: `Pulled` → Results, `Preprocessing` →
Preprocessing, `PreprocessingFailed` → Failed, `Preprocessed`/`TransferFailed`/`Transferring` →
Summary, `Transferred` → Complete; `Abandoned` (or an unknown run) sends the user home with a
banner. A failed **transfer** can be re-run from the same screen; a `PreprocessingFailed` run
cannot — it releases its pair (its outputs go inactive) and the Failed screen's own copy directs
starting a new run instead, so two runs (the retried one and a colleague's fresh one) can never
both hold the same pair.

## 4. Concurrency

One active run per checking window **and** output type, enforced by a database constraint, not
just a service check: a partial unique index on `egress_run_outputs ("WindowId", "OutputType")
WHERE "IsActive"`. `IsActive` is true from `Pulled` all the way through `Transferred` — a
successful transfer leaves it active forever, so the same window and output type can never be sent
twice — and false for `PreprocessingFailed`, `TransferFailed` and `Abandoned`, so a failed or
abandoned run never blocks a new one.

`EgressRunService.StartAsync` checks for a blocker first (a friendly refusal naming who holds it
and since when) and `EgressRunRepository.CreateRunAsync`/`TryReactivateAsync` also catch the
database's unique-violation as a race guard, in case two admins start at the same instant. The two
refusal sentences (`EgressController.Describe`, FLAGGED copy):

- *"{Output type} for this checking window is already being processed by {name}, started {date} at
  {time} UTC. Wait for that run to finish or be abandoned."*
- *"{Output type} for this checking window has already been transferred to LDS by {name} on {date}
  at {time} UTC. It cannot be sent again."*

## 5. Preprocessing

Eight steps, reported one at a time over the same `IAsyncEnumerable<EgressProgress>`
(`EgressPreprocessor.RunAsync`) whether driven by the SSE stream or the no-JS POST:

1. **Filter records** — keep `approved` and `auto_approved`; everything else is discarded here,
   not shown as a failure.
2. **Derive correction codes** — Remove: the bare LDS code from the Zendesk "Correction reason
   (31)" option for the journey's `reason` answer (plus two Post16 reasons mapped by hand); New:
   the fixed code `10`.
3. **Split DfE establishment number** — the pupil's LAESTAB first, then the request row's
   `OrganisationLaestab`; 7 digits only, split 3+4.
4. **Standardise dates** — date of birth (and, for New learners, admission date) to `yyyy-MM-dd`.
5. **Build LDS records** — the typed `NewLearnerRow`/`RemoveLearnerRow`.
6. **Trim values**.
7. **Validate against LDS spec** — required fields, permitted values, digit widths, date shapes
   (`LdsSpecValidator`).
8. **Save to database** — all or nothing: if **any** record failed at any step, nothing is written,
   the run becomes `PreprocessingFailed`, and every failure (step, ticket id, reference, field,
   reason) is listed on the Failed screen; otherwise rows go to `new_learners`/`remove_learners`,
   file names are fixed for the run, and the run becomes `Preprocessed`. Every independent step
   (2–4) runs regardless of an earlier one's outcome, so a record with two unrelated problems (say
   an unmapped correction reason and an invalid LAESTAB) lists both in one pass rather than the
   second only surfacing on a later re-run.

Leaving the page mid-run (closing the SSE connection, or the no-JS POST being interrupted) puts the
run back to its status before preprocessing started — nothing durable happens until step 8 commits.
If the run was abandoned by someone else while this pipeline was still running, step 8's write is
guarded by the run's expected status and does nothing; the stream reports "this run was abandoned
while preprocessing" rather than claiming `Preprocessed` or `PreprocessingFailed` for a run that is
actually `Abandoned`.

## 6. Files

Both LDS files use the same writer (`EgressCsvWriter`): RFC 4180 quoting, `\r\n` between lines, no
line terminator after the final data row, every value trimmed, UTF-8 without a byte-order mark.

Both files follow `LDS_CYPMD_Data specification_v2.4.xlsx` (sheets "New Learner" and "Remove
Learner", read top to bottom, keeping the rows marked X for the window's key stage). The column set
is therefore the *window's*: `EgressColumnSets.RemoveLearnersFor(windowType)` /
`NewLearnersFor(windowType)`.

**Remove learners** — every key stage:

```
Correction_ID, Correction_Type, Correction_Reason, Key_Stage, Establishment_Number, Surname,
Forename, Sex, Date_of_Birth, Cycle_Year, Cycle_Month, Local_Authority, Learner_ID
```

then, KS4 only: `Year_Group` (populated for year-group-change removals from the journey's
`year-group-higher/lower-moved-to` answer, blank otherwise); 16-19 only: `Removal_Year_0`,
`Removal_Year_1`, `Removal_Year_2` (`TRUE`/`FALSE` from the `years-to-remove` checkbox — Year_0 is
the academic year ending in Cycle_Year; blank when the journey route did not ask).

**New learners** — every key stage (the spec's `Middle_Name` is struck through in v2.4, "CYPMD will
not be sending this field from June 2026", so it is not emitted; there is no SEN attribute):

```
Correction_ID, Correction_Type, Key_Stage, Establishment_Number, Surname, Forename, Sex,
Date_of_Birth, Admission_Date, Post_Code, Cycle_Year, Cycle_Month, Local_Authority, URN, ULN, UPN,
Learner_ID, Year_Group
```

then, 16-19 only: `Attendance_Year_0`, `Attendance_Year_1`, `Attendance_Year_2`, `KS4_Year` — all
blank today because no Post16 Add journey exists.

Values: `Key_Stage` is `KS2` / `KS4` / `16-19` (`EgressOutputTypes.KeyStageValue`; the *file name*
still uses AB#292610's `KS5` token). `Cycle_Year` / `Cycle_Month` are the checking window's start
year and month (the spec's "month in which the cycle takes place"), not each record's submission
date. `Sex` is `F` / `M` / `U` on both files. 16-19 `Correction_Reason` codes are the spec's
(`CorrectionCodes`): 4 deceased, 325 not at end of study, 326/328/331 not on roll (international /
external / apprentice), 329 other with evidence.

File names are fixed when preprocessing completes: `CYPMD_LDS_{stage}_{type}_{yyyy_MM_dd}.csv`,
where `{stage}` is `KS2`/`KS4`/`KS5` (`EgressOutputTypes.StageToken`) and the date is the **London**
calendar date at the moment preprocessing finishes (`EgressOutputTypes.FileName`).

## 7. Transfer and audit

`EgressTransferService.TransferAsync` refuses before touching anything if every output's saved row
count is zero (every record was rejected, undecided, or lost to a misconfigured ticket source) —
Summary shows a plain message instead of Confirm in that case, so LDS is never sent a header-only
file for a pair that then locks forever. Otherwise it builds each file's bytes from the
**persisted rows**, never from the pulled payload, and uploads with `IfNoneMatch: *` (create-only —
an existing blob is never overwritten).

Once the run's status has flipped to `Transferring`, the upload loop, the commit
(`MarkTransferredAsync`) and all compensation run with `CancellationToken.None`, not the request's
own token — a browser tab closing mid-upload must not abandon a run with files already in LDS. If
any file fails to upload, every file already written by that attempt is deleted
(`IEgressBlobClient.DeleteIfExistsAsync`), and the specific file that was mid-upload when the
failure happened is *also* checked and removed if it turns out to have landed server-side despite
the client seeing a failure (matched by the `egressRunId` metadata stamped on upload, so a blob
belonging to a different run is never touched); the run becomes `TransferFailed` with the reason
recorded. A post-upload failure in the commit itself (every file uploaded, but marking the run
`Transferred` throws, or loses a race because the run was abandoned in between) is caught the same
way and compensated identically — no partial state survives silently. A retry re-activates the
run's outputs first and is refused if another run has since claimed the pair.

Every terminal write (`MarkPreprocessingFailedAsync`, `SavePreprocessedAsync`,
`MarkTransferredAsync`, `MarkTransferFailedAsync`) is guarded by the status the caller expects the
run to currently hold, and reports rows affected; a caller that gets zero back knows it lost a race
(most often to a concurrent Abandon) and never overwrites what actually happened with a stale
outcome. Success and failure each write an `AuditEntry` in the same transaction as the guarded
state change: `EntityType` `"EgressRun"`, `Action` `"Transfer"` or `"TransferFailed"`, `NewValues` a
JSON object with the outcome, window id, output types, file names, record counts, SHA-256 hashes,
target container and who/when. No audit row is ever written for a write that lost its race, and no
audit row ever claims success for a failed transfer.

## 8. Configuration

| Setting | Purpose |
|---|---|
| `ConnectionStrings:EgressStorage` | The LDS storage account. Absent → the app refuses to transfer with a clear "not configured" message rather than failing at startup, and the dev cleanup skips its blob sweep. **`appsettings.json` deliberately carries no default** (pinned by `AppSettingsEgressStorageTests`): a local-Azurite default there made every deployed environment without an account believe it had one at `127.0.0.1:10000` inside the pod, so transfers and cleanups failed after the SDK's retries instead of refusing. Local runs get it from `docker-compose.yaml` and the launch profiles. Must be added to each deployed environment's Key Vault/Terraform secrets; **not done in this PR** — see gaps below. |
| `EgressStorage:Container` (default `cypmd`) / `EgressStorage:Prefix` (default `extracts_input/`) | Where in the account files land. Bindable only so a test can point at a scratch container — never user-editable. |
| `Zendesk:UseFake` (default **`false`**) | Selects the ticket source: the real Zendesk client (`ZendeskEgressTicketSource`, via the same `AddZendeskApiClient` the worker uses) unless explicitly set to `true`, which selects the dev outbox (`DevOutboxEgressTicketSource`, no Zendesk settings needed). The default matches the worker's own configured default — a fresh environment that sets nothing reads real Zendesk decisions, not the dev outbox. `AddCpdEgress` refuses to start if `UseFake=true` is set in Production, regardless of configuration, so the dev outbox can never be reached there. Local/E2E stacks opt in explicitly via `Zendesk__UseFake=true` (`docker-compose.yaml`, `docker-compose.sandbox.yaml`), since neither has real Zendesk credentials. |
| `ZendeskTicketFields:DecisionStatusId` | The real ticket source's required field id; `0` in production today, so it refuses to pull until configured. |
| Admin grant `egress` | `DefaultAdminAccessSeeder.AllSections` — without it a fresh database 404s on `/admin/egress` even for an admin. |

## 9. Local development and E2E

Locally the web container gets a third Azurite account (`docker-compose.yaml`,
`ConnectionStrings__EgressStorage`, alongside the app and ingress accounts), and opts in to the
dev outbox fake explicitly with `Zendesk__UseFake=true` — the code/config default is now the real
Zendesk client, which this stack has no credentials for.

`DevEgressController` (dev-only, 404 unless `Dev:ToolsEnabled` and not Production, same rule as
`DevPipelineController`) stages fixture data with no worker and no real Zendesk:

```bash
# Seed 3 approved Remove-learner requests for the KS4 June window
curl -X POST "http://localhost:8080/dev/egress/seed?windowId=F34D285B-8660-4D12-9C30-787328DEAA0A&outputType=RemoveLearners&decision=auto_approved&count=3&laestab=860/4070&urn=142313&reason=pupil-died"

# Remove everything the harness created for that window (runs, requests, blobs)
curl -X POST "http://localhost:8080/dev/egress/cleanup?windowId=F34D285B-8660-4D12-9C30-787328DEAA0A"
```

`tests/DfE.CheckPerformanceData.E2ETests/Admin/DataEgressTests.cs` walks the whole journey over
plain HTTP (pull → results → preprocessing → summary → download → transfer → complete → refused),
a failing-record path, and — Linux-only — a real-browser fact for the streamed progress. Run just
this class: `docker compose --profile e2e run --rm e2e-tests sh -c 'dotnet test
tests/DfE.CheckPerformanceData.E2ETests/ --filter "FullyQualifiedName~DataEgressTests"'`.

To inspect what actually landed in the local LDS account, the Storage browser under Danger zone
covers the **app** account only — the egress account needs a separate client (e.g. Azure Storage
Explorer, or the Azure CLI, pointed at the `EgressStorage` connection string from
`docker-compose.yaml` or `Properties/launchSettings.json`), container `cypmd`, prefix `extracts_input/`.

Two rules keep a dev or review environment recoverable after a failed run:

- `POST /dev/egress/cleanup` always deletes its database rows. The blob sweep is best effort — a
  blob that could not be deleted is counted in the response's `blobErrors`, never thrown — because
  a cleanup that answered 500 and left the runs behind is what broke the next deploy (below).
- The dev seeder (`SeedCheckingWindows`, run at start-up wherever `SeedDevelopmentData` is on)
  deletes `egress_runs` before it wipes `CheckingWindows`. The foreign key from a run to its window
  is RESTRICT on purpose — an egress is an audit record — so a leftover run used to make the wipe
  throw before the host listened. On a review app that looked like a stalled rollout: the new pod
  never became Ready, the old one kept serving, terraform reported "old replicas are pending
  termination" and a re-run reported "No changes". PR #441's review app served a two-day-old image
  that way; `CheckingWindowSeedWithEgressHistoryTests` pins the fix.

## 10. Known gaps and follow-ups

- **Merged learners** (and the KS4 June code `20`→`21` correction-code rule, AB#292610) is
  deliberately out of scope — its own column set and rule, tracked as a follow-up ticket.
- **Runs history with filters and pagination** is AB#294590; the Pull page carries only a minimal
  saved/completed list.
- **LDS spec v2.4 questions still open with LDS/BA** (see the PR notes §10): the struck
  `Middle_Name` heading is omitted entirely; `Key_Stage` says `16-19` (v2.4 changed the Remove
  sheet from 16-18, the New Learner sheet was not updated); year-group-change removals go out as
  Correction_Type 31 / reason 17 (business-confirmed 2026-08-06) although the spec's hidden Addback
  sheet has them as type 30; `Removal_Year_0..2` are blank unless the 16-19 journey took the
  "other" route; 16-19 "other" is always 329 (evidence is always collected, so 330 never occurs);
  Correction_Type 11 (Include learner) is in the spec but Include is not an egress output yet.
- **16-19 gaps**: no Add journey exists, so the New learners file for a 16-19 window can only ever
  be header-only (and Transfer refuses a run whose outputs are all empty); 16-19 pupil records have
  no MATCHREF, so `Learner_ID` fails validation for them until the 16-19 pupil file supplies one.
- **Trailing newline**: files end after the last data row with no trailing line terminator, to
  honour "no additional rows below the final data row" — confirm this reading with LDS.
- **`ConnectionStrings__EgressStorage` is not yet in Terraform** (`terraform/application/
  application.tf` `secret_variables`) for any deployed environment — add it once the real LDS
  account details are known.
- **Production's `ZendeskTicketFields__DecisionStatusId` is `0`** — the real ticket source refuses
  to pull until an environment configures it.
- The pre-existing gap that `Controllers/WindowAdmin/*` carries no `[RequireAdminSection]` is a
  separate ticket and was not touched here; the new `EgressController` **is** gated.
- Every copy string introduced by this feature is FLAGGED for content sign-off — see the PR notes.
- **Same-stage same-day file-name collision**: two different checking windows of the same stage
  transferred on the same day produce the same file name, so the second run's transfer fails with
  "already exists" — the only ways out today are waiting a day or a manual delete in LDS. Question
  for LDS: is one-window-per-stage-per-day a real constraint? Tracked as a follow-up, not fixed by
  this pass.
- **`PreprocessingStream` is a state-mutating GET** (the accepted `ValidateWindowController`
  pattern) — the JS closes the `EventSource` on a terminal/error event, so the browser's automatic
  reconnect never restarts the server-side pipeline. Left as-is; noted for the next contributor.
- **Abandon crash-window orphan**: R1 reordered `AbandonAsync` to write `Abandoned` before
  sweeping the run's blobs (closing a data-loss race — see the R1 commit), but that also moved
  the crash window rather than removing it. If the process dies after the write commits but
  before the sweep loop finishes, the run ends up `Abandoned` (not stuck `Transferring`) with one
  or more files still sitting in LDS storage; the pair is released, so a same-stage/same-day retry
  can hit the pre-existing "file already exists" collision above, and the orphaned file itself has
  no UI-reachable recovery — only a manual LDS delete. Follow-up, not an open production incident.
- Related: `EgressTransferService.FailAsync`'s compensation delete of its own just-uploaded files
  (the `uploaded` list) calls `blobs.DeleteIfExistsAsync` unconditionally, unlike its
  `possiblyOrphaned` check, which is ownership-checked via `DeleteIfOwnedByRunAsync`. In the
  create-only-upload case this is safe today (a successful create-only PUT cannot belong to
  another run), but it is a related blob-lifecycle-under-overlap gap worth tightening for
  consistency. Follow-up, not an open production incident.
- An independent review of the transfer/lock state machine found one Blocker (B1: the web host
  defaulted to the dev Zendesk fake with nothing but QA config overriding it, so Production would
  have read the dev outbox table instead of real Zendesk decisions) and four Must-fixes (M1:
  transfer was not atomic on cancellation or a post-upload database failure, leaving a run stuck
  `Transferring` with files already in LDS; M2: re-running a `PreprocessingFailed` run bypassed
  its released lock; M3: a run whose approved set was empty could still transfer a header-only
  file and lock its pair forever; M4: every terminal status write was unconditional, so a
  concurrent Abandon could be silently overwritten) plus several should-fixes, all addressed in
  follow-up commits on this branch — see the PR description for the finding-by-finding record.
