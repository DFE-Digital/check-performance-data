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

`Abandoned` is reachable from any non-terminal status. A run is persisted the moment the pull
succeeds (`EgressRunService.StartAsync` → `IEgressRunRepository.CreateRunAsync`), so "Save and
exit" on any screen is just leaving the page — there is no separate save action. Resume
(`EgressController.Resume`) maps status to screen: `Pulled` → Results, `Preprocessing` →
Preprocessing, `PreprocessingFailed` → Failed, `Preprocessed`/`TransferFailed`/`Transferring` →
Summary, `Transferred` → Complete; `Abandoned` (or an unknown run) sends the user home with a
banner. Preprocessing and a failed transfer can both be re-run from the same screen.

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
   file names are fixed for the run, and the run becomes `Preprocessed`.

Leaving the page mid-run (closing the SSE connection, or the no-JS POST being interrupted) puts the
run back to its status before preprocessing started — nothing durable happens until step 8 commits.

## 6. Files

Both LDS files use the same writer (`EgressCsvWriter`): RFC 4180 quoting, `\r\n` between lines, no
line terminator after the final data row, every value trimmed, UTF-8 without a byte-order mark.

**Remove learners** columns (`EgressColumnSets.RemoveLearners`, the spike's list verbatim):

```
Correction_ID, Correction_Type, Correction_Reason, Key_Stage, Establishment_Number, Surname,
Forename, Sex, Date_of_Birth, Cycle_Year, Cycle_Month, Local_Authority, Learner_ID
```

**New learners** columns (`EgressColumnSets.NewLearners`) — **FLAGGED**: the LDS
`LDS_CYPMD_Data specification v2.4` workbook was not available when this shipped, so this set is
derived from the tactical Zendesk "New Learner Output" report plus AB#292610's rules (LA and
establishment split from the 7-digit number, UPN placed between ULN and the matched LDS reference,
SEN status included because the Add journey captures it as an LDS-bound value):

```
Correction_ID, Correction_Type, Key_Stage, Local_Authority, Establishment_Number, Surname,
Middle_Name, Forename, Sex, Date_of_Birth, Admission_Date, Postcode, Cycle_Year, Cycle_Month,
School_URN, ULN, UPN, Learner_ID, Year_Group, SEN_Status
```

File names are fixed when preprocessing completes: `CYPMD_LDS_{stage}_{type}_{yyyy_MM_dd}.csv`,
where `{stage}` is `KS2`/`KS4`/`KS5` (`EgressOutputTypes.StageToken`) and the date is the **London**
calendar date at the moment preprocessing finishes (`EgressOutputTypes.FileName`).

## 7. Transfer and audit

`EgressTransferService.TransferAsync` builds each file's bytes from the **persisted rows**, never
from the pulled payload, and uploads with `IfNoneMatch: *` (create-only — an existing blob is never
overwritten). If any file in the run fails to upload, every file already written by that transfer
attempt is deleted (`IEgressBlobClient.DeleteIfExistsAsync`) and the run becomes `TransferFailed`
with the reason recorded; a retry re-activates the run's outputs first and is refused if another
run has since claimed the pair. Success and failure each write an `AuditEntry` in the same
transaction as the run's state change (`EgressRunRepository.MarkTransferredAsync` /
`MarkTransferFailedAsync`): `EntityType` `"EgressRun"`, `Action` `"Transfer"` or `"TransferFailed"`,
`NewValues` a JSON object with the outcome, window id, output types, file names, record counts,
SHA-256 hashes, target container and who/when. No audit row ever claims success for a failed
transfer.

## 8. Configuration

| Setting | Purpose |
|---|---|
| `ConnectionStrings:EgressStorage` | The LDS storage account. Absent → the app refuses to transfer with a clear "not configured" message rather than failing at startup. Must be added to each deployed environment's Key Vault/Terraform secrets; **not done in this PR** — see gaps below. |
| `EgressStorage:Container` (default `cypmd`) / `EgressStorage:Prefix` (default `extracts_input/`) | Where in the account files land. Bindable only so a test can point at a scratch container — never user-editable. |
| `Zendesk:UseFake` (default `true`) | Selects the ticket source: the dev outbox (`DevOutboxEgressTicketSource`, no Zendesk settings needed) when true, the real Zendesk client (`ZendeskEgressTicketSource`, via the same `AddZendeskApiClient` the worker uses) when false. |
| `ZendeskTicketFields:DecisionStatusId` | The real ticket source's required field id; `0` in production today, so it refuses to pull until configured. |
| Admin grant `egress` | `DefaultAdminAccessSeeder.AllSections` — without it a fresh database 404s on `/admin/egress` even for an admin. |

## 9. Local development and E2E

Locally the web container gets a third Azurite account (`docker-compose.yaml`,
`ConnectionStrings__EgressStorage`, alongside the app and ingress accounts).

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
`appsettings.json`/the compose file), container `cypmd`, prefix `extracts_input/`.

## 10. Known gaps and follow-ups

- **Merged learners** (and the KS4 June code `20`→`21` correction-code rule, AB#292610) is
  deliberately out of scope — its own column set and rule, tracked as a follow-up ticket.
- **Runs history with filters and pagination** is AB#294590; the Pull page carries only a minimal
  saved/completed list.
- **New learners column set is unverified against the spec.** Correct `EgressColumnSets`,
  `EgressColumnSetsTests` and `LdsSpecValidator` together once `LDS_CYPMD_Data specification v2.4`
  arrives — nowhere else defines the file's shape.
- **Post16 (KS5) gaps**: no Add flow exists; only two Remove reasons (`student-died`,
  `not-on-roll`) have a mapped correction code — any other Post16 reason fails validation with an
  explicit reason rather than being guessed; Post16 pupils have no MATCHREF.
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
