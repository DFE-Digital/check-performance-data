# Audit log (AB#294592)

The admin **Audit log** (`GET /admin/audit-log`, root tile "Audit log", grant `audit-log`) lists every
row of `AuditEntries` newest first, with **data egress transfers** distinguishable among them, three
cumulative filters, 20 rows a page, and a CSV export of the filtered set.

## What is audited

- **Generic capture.** `PortalDbContext.SaveChangesAsync` writes one `Insert`/`Update`/`Delete` row
  per tracked entity change: `EntityType` is the CLR type name, `EntityId` the primary key,
  `UserId` the DfE Sign-In subject (`system` outside a request, `rules-engine-worker` in the
  worker). Excluded as telemetry or bulk-derived data: queue metrics, search events and hit rows,
  organisation logins, the egress learner rows and `EgressRunOutput`. Not excluded: every `AppLog`
  row the database log sink persists, so "App log / Insert" rows make up most of an unfiltered log
  (about 70% in a dev database). Whether to exclude them is an open product question.
- **Deliberate rows** written by hand: `ContentBundle/Import`, `DlqMessage/…` (redrive, purge,
  full-payload view), `SearchSession/SearchSessionDelete`, `SearchAnalyticsSink/…`, rules-config
  actions, and the two egress rows below.
- **Egress pulls.** The run row itself is not excluded from the generic capture, so every pull is
  also an `EgressRun`/`Insert` row (PascalCase payload with the window id and the starter's name).
  The log shows it as **Data egress · Run started**, naming the person who pulled, with no status;
  the status filter never matches it.
- **Egress transfers.** `EgressRunRepository.MarkTransferredAsync` and `MarkTransferFailedAsync`
  each write one `EgressRun` row — `Transfer` or `TransferFailed` — **inside the same transaction as
  the guarded status flip**, so a run that reached transfer always has exactly one row per attempt
  and a write that lost its race has none. Both payloads (`NewValues`, camelCase JSON) carry
  `outcome`, `windowId`, `outputTypes` and `transferredBy`; success adds the files, hashes,
  container and time; failure adds the `reason`. A run that failed in preprocessing or was
  abandoned never left the service and is not an audit row — the runs history shows those.
- **Early closure of a checking exercise (AB#301022).** `WindowRepository.CloseExerciseEarlyAsync`
  writes one `WindowAdmin`/`ClosedEarly` row **inside the same transaction as the guarded end-date
  move**, so an exercise closed early always has exactly one row and a close that lost its race has
  none. `EntityId` is the window id, `UserId` the admin's subject, `Timestamp` the UTC instant of
  the close. The payload (`NewValues`, camelCase JSON) carries `windowId`, `windowTitle`,
  `exerciseType`, `scheduledEnd` (the end date it had), `newEndDate`, `closedEarly`, `closedBy` and
  `closedAtUtc`. The end-date move itself is an `ExecuteUpdate`, so the generic capture writes no
  `CheckingExercise`/`Update` row for it — this row is the record.
- **Automatic hand-over of a checking exercise's requests (AB#302158).** `WindowAdminAuditWriter`
  writes one `WindowAdmin`/`RequestsSentAutomatically` row when the service hands an exercise's
  requests over by itself, two hours after the exercise ends — but only for a run that sent or
  cancelled something, so the day of repeat runs that follows leaves no rows. `EntityId` is the
  window id, `UserId` is null (nobody did it; the screen says "System"), `Timestamp` the UTC
  instant of the run. The payload carries `windowId`, `windowTitle`, `exerciseType`,
  `exerciseEnd`, `requestsSent`, `draftsCancelled`, `automatic` and `ranAtUtc`. It is written
  after the sweep rather than in a transaction with it, so a failed write loses the row, not the
  hand-over.

## The screen

| Column | Egress row | Window admin row | Any other row |
|---|---|---|---|
| User | `transferredBy` from the payload (the subject id for a pull row) | `closedBy` from the payload | the `UserId` subject (there is no user directory) |
| Activity | turquoise **Data egress** tag ("Run started" beneath for a pull row) | orange **Window admin** tag | grey tag with the entity type's label, the action beneath |
| Checking window | the run's window title, output types beneath | the window title, "{exercise} closed early, before scheduled end" beneath | the window title for a `CheckingWindow` row; otherwise empty |
| Time | `d MMM yyyy` and `HH:mm:ss UTC` | same | same |
| Status | green **Success** / red **Failed** | green **Success** | none |

An automatic hand-over is a window-admin row as well: **System** as the user, the orange
**Window admin** tag, the window title with "{exercise} requests sent for processing
automatically: N requests sent, M drafts cancelled" beneath, and green **Success**.

Filters (a plain GET form, no script): **activity** = the row's `EntityType` (options are the
distinct types present, plus `EgressRun` always; `?activity=EgressRun` isolates egress); **checking
window** = egress rows whose run belongs to the window (resolved through `egress_runs`, so it never
parses JSON in SQL) or `CheckingWindow` and `WindowAdmin` rows whose id is the window; **status** =
egress rows whose action is `Transfer` (Success) or `TransferFailed` (Failed), plus
`WindowAdmin`/`ClosedEarly` and `WindowAdmin`/`RequestsSentAutomatically` rows (Success only).
Filters AND together; an unknown value is no filter. Page links and the export carry every filter.

## Export

`GET /admin/audit-log/export?activity=&windowId=&status=` streams every matching row as
`text/csv` (`audit-log-{yyyyMMdd-HHmmss}.csv`): `Timestamp (UTC),User,Activity,Action,Entity type,
Entity id,Checking window,Output types,Status`. No cap; rows stream straight to the response.

## Immutability, retention, privacy

- A `BEFORE UPDATE OR DELETE` trigger on `AuditEntries` raises, so nothing — including the app —
  can amend or delete a row; the page has no such control. Pinned by
  `AuditEntryActionLengthTests.AuditEntries_AreImmutable_UpdateAndDeleteRaise`.
- Nothing purges `AuditEntries`; there is no retention job for them.
- `OldValues`/`NewValues`/`ChangedColumns` are **never rendered or exported**: the generic capture
  stores pupil-bearing entities (`ChangeRequest`) in them. The query projects `NewValues` only for
  `EgressRun` and `WindowAdmin` rows (two hand-written payloads with no pupil data), and
  `AuditLogRow` has no payload member. The export shows a window-admin row's exercise in the
  Action column, as `ClosedEarly (PupilData)` or `RequestsSentAutomatically (PupilData)`.

## Known limits

- Non-egress rows show the sign-in subject id, not a name.
- Egress failure rows written before AB#294592 carry no `windowId`/`outputTypes`/`transferredBy`
  (dev and review databases only): they show "Unknown window", no files and the subject id, but
  still filter by window through their run.
- A dev "Reset seed data" or `/dev/egress/cleanup` deletes the runs; their audit rows remain and
  still show the window's title (the payload carries the window id), but they no longer match the
  checking-window filter, which resolves through `egress_runs`. Production never deletes runs.
