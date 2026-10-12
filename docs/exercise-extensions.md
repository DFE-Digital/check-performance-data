# Extending a checking exercise for named schools

*GitHub #567 / AB#307773. Implemented on branch `feature/307773-extending_exercises`, on top of
`466-exercise-refactor-2`.*

## Why

A school sometimes needs more time than the exercise gives everyone. An admin names those schools
and gives them a later end date. Every other school sees the exercise close on its own date, as it
did before.

The ticket decides to **extend in place**. An extension is a child of the exercise: a list of
LAESTABs and an end date. Nothing is copied. We did not make a second exercise for the named
schools. A copy would need its own data, its own release and its own tab. It would also give a
school's requests a second `CheckingExerciseId`, and egress, the close sweep and the audit log
would each have to join the two.

With an extension, a named school keeps the same tab, the same current release and every later
release. Its requests stay stamped with the one exercise id.

## The model

```text
CheckingWindow
  CheckingExercise (pupil data or results enquiry; a data share has no kind and no extensions)
    Extensions ──► CheckingExerciseExtension (Id, EndDate, CreatedAt, CreatedBy, RequestsSentAt)
                     Schools ──► CheckingExerciseExtensionSchool (Id, CheckingExerciseId, Laestab)
```

| Table | Purpose |
|---|---|
| `CheckingExerciseExtensions` | One row for each extension. `EndDate` is a UK wall-clock value, like every exercise date. `CreatedAt` is a UTC instant with an Unspecified kind, like `ChangeRequest.Timestamp`. `CreatedBy` is the admin's display name. `RequestsSentAt` is null until the extension's requests have been sent (see "The sweeps"). |
| `CheckingExerciseExtensionSchools` | One row for each named school. `Laestab` is digits only (`9334290`). `CheckingExerciseId` is copied from the parent when the row is written and never updated. |

- The unique index is on (`CheckingExerciseId`, `Laestab`). It makes a school belong to one
  extension of an exercise. The service checks first, so the admin reads a sentence. The index is
  the backstop for two admins saving at once.
- `CheckingExerciseId` on the school row is there for that index. A unique index cannot span two
  tables.
- Both foreign keys cascade. Deleting an exercise or a window deletes its extensions and their
  schools. `ExerciseDeletionRepository` and `WindowDeletionRepository` need no new statement. The
  delete page counts the extensions.
- The migration is `AddCheckingExerciseExtensions`. It changes no existing row.

## The rules, as built

1. **An extension never shortens a school's time.** A school's effective end is
   `max(exercise.EndDate, extension.EndDate)` (`CheckingExerciseDto.EndDateFor(laestab)`). The form
   refuses an extension that ends on or before the exercise's end, so `max` only matters when an
   extension is closed early before the exercise has ended. The school then falls back to the
   exercise's own end.
2. **An extension has no start of its own and two states.** It is *Open* while `EndDate >= now` and
   *Closed* once `EndDate < now`. The clock is the one `ICheckingExerciseService` reads
   (`IsOpen(extension)`, `HasClosed(extension)`).
3. **Add before or after the exercise ends.** An admin can add an extension at any time: while the
   exercise is planned, open or closed.
4. **Edit until closed.** Edit and Close early are offered only while the extension is Open. Send
   requests is offered only once it is Closed. A closed extension is read only.
5. **A school is in one extension of an exercise.** Two extensions of one exercise cannot name the
   same school, open or closed. The error names the school and the other extension's end.
6. **LAESTAB forms.** The normal form is **digits only** (`9334290`), as `LaestabNormaliser` and
   every blob name already use. DfE Sign-In gives `LA/ESTAB` with a slash, and
   `ChangeRequest.OrganisationLaestab` stores that claim with the slash. So the form accepts
   `933/4290` or `9334290`, one on each line, and shows the slashed form back. The sweeps compare
   `OrganisationLaestab.Replace("/", "")` against the stored digits. `LaestabList.Parse` reads the
   text: it skips blank lines, keeps each school once in input order, and gives one error for each
   bad line with its line number (blank lines count).
7. **A warning for a school with no file.** Before it saves, the page checks each LAESTAB with
   `ICheckingDataReader.HasSchoolDataAsync` (the landing page's own check) against the exercise's
   current release. A school with no file is listed with a warning, and the admin has to tick
   "Add these schools anyway" to save. A school may get its file in a later release, so this is a
   warning and not an error. With **no release yet**, every school is listed, the warning says
   "This exercise has no release yet", and the reader is not called.
8. **The warning is a confirm-on-resubmit step.** It uses the pattern `AddExerciseData` uses. The
   form comes back with the warning and the checkbox. The POST saves only when the box is ticked or
   nothing needs warning. No JavaScript is involved. Posting again without the tick gives an error
   on the checkbox ("Confirm that you want to add these schools, or remove them from the list"),
   in the error summary and with the "Error:" title prefix, so a screen-reader user hears that
   nothing was saved.
9. **The exercise's close leaves out a school that is still held.** See "The sweeps".
10. **Removing a school after the exercise closed.** An admin can edit an Open extension after the
    exercise's own end. A school taken out is no longer in the held set, so anything of its that is
    left goes with the next sweep, exercise or extension. Both are past the deadline by then. The
    school's own deadline is the exercise's end again, so it sees the exercise as closed on its
    next click: the journey refreshes its snapshot before every action.
11. **`WindowEndDate` and the window's outer end.** The window's `EndDate` is the latest of every
    exercise end and every extension end, in the database as well as in the DTO
    (`CheckingWindowDto.DeriveDatesFromExercises`). Every writer that re-derives it in SQL calls
    `WindowEndDate.RederiveAsync`: the four extension writes, `WindowRepository.UpdateAsync`,
    `WindowRepository.CloseExerciseEarlyAsync` and `ExerciseDeletionRepository.DeleteAsync`.
    Without this, the landing page's date bracket would hide the window from a named school once
    the exercise's own end had passed.

### What a named school sees

No page has new markup. Each page already prints what `EndDateFor` and `IsOpen` return, and the
controllers now pass the school's LAESTAB (`ICurrentUserService.OrganisationLaestab`). A named
school sees only the new date, with no mention that it is an extension.

| Page or gate | Effect for a named school |
|---|---|
| Landing page card and closed banner (`LandingPageController`) | The extension's end, and no closed banner while it runs. |
| `LandingPageService.IsWindowShown` | The window stays shown until the school's own end. A blank LAESTAB means no school. |
| Check your pupil data (`CheckYourPupilDataController`) | The extension's end, and "Request a change" is offered. |
| Amendment requests (`AmendmentRequestsService`, `AmendmentRequestsController`) | Both deadline rows and the bulk-confirmation banner quote the extension's end. |
| Journey gates (`JourneyController`, `WhatToChangeController`, `ConfirmCorrectController`, `ResultIssueController`, `BulkSubmissionService`) | The exercise is open until the school's end. |
| Next steps (`NextStepsService.GetAvailableSteps(exercises, laestab)`) | Steps follow the school's end. |

`ClosedExerciseGuard` has no date logic and does not change.

## The sweeps

Closing an exercise sends its submitted requests for processing and cancels drafts that were not
submitted. An extension adds a second, later sweep for its own schools. Both run through one private
sweep in `CloseExerciseService`, over a `SchoolFilter`:

```text
SchoolFilter(Laestabs, Mode)
  None          adds nothing: every school                              (today's behaviour)
  Exclude(set)  r.OrganisationLaestab == null || !set.Contains(digits)   (the exercise sweep)
  Only(set)     r.OrganisationLaestab != null && set.Contains(digits)    (an extension sweep)
```

`digits` is `r.OrganisationLaestab.Replace("/", "")`. A request with no LAESTAB always goes with the
exercise. `Exclude` of an empty set is the `None` instance.

| Sweep | Entry points | Filter |
|---|---|---|
| Exercise | `PreviewAsync`, `CloseAsync` | `Exclude` the schools of every extension that is **Open or unsent** (`IsOpen`, or `RequestsSentAt` is null) |
| Extension | `PreviewExtensionAsync`, `CloseExtensionAsync` | `Only` that extension's schools |

- **"Unsent" is a stamp.** `RequestsSentAt` is set the first time the extension's sweep runs after
  it closes, by the manual Send or by the automatic hand-over, whatever the counts.
  `CloseExtensionAsync` calls `IExerciseExtensionRepository.MarkRequestsSentAsync`, an
  `ExecuteUpdate` where `RequestsSentAt IS NULL`, so the stamp is set once. A sweep that throws
  leaves the extension unsent.
- **Why the stamp.** Without it, the exercise's sweep could not tell a closed extension whose
  schools were sent from one whose schools had not been. It would send a named school's requests
  the moment the exercise's own end passed, or never.
- **After the extension's sweep has run,** anything of that school's that is left over (a request
  the Rules Engine decided late) goes with whichever sweep runs next. Both are past the deadline.
- **The clock stays in `ICheckingExerciseService`.** `CloseExerciseService` takes the exclude set
  from it and does not compare dates. It takes a `TimeProvider` only to stamp `RequestsSentAt`,
  never to compare.
- **The preview and the sweep share one predicate.** The four `IAdminRequestsRepository` methods
  (`GetDecidedRequestsForExerciseAsync`, `CountWaitingRequestsForExerciseAsync`,
  `CountDraftsForExerciseAsync`, `MarkDraftsNotSubmittedForExerciseAsync`) each take a
  `SchoolFilter`, applied inside `SubmittedAmendmentsForExercise` and `DraftsForExercise`.
- **The caller gates `CloseExtensionAsync`.** The service does not check that the extension has
  closed. The admin Send page offers it only once Closed, and the hand-over runs only after the
  extension's end.
- An extension that does not exist, or whose exercise is a data share, throws
  `InvalidOperationException`. The controllers 404 before they call it.

## The automatic hand-over

`AutomaticExerciseHandOver.FindDueAsync` yields one due item for the exercise and one for each
extension. Each is judged on its own end by `ExerciseHandOverSchedule.IsDue`: due from two hours
after that end (`ExerciseHandOver__DelayAfterEnd`), for the catch-up window that follows
(`ExerciseHandOver__CatchUpWindow`, 24 hours by default).

- `DueExercise.ExtensionId` is null for the exercise's own item. `ExerciseEnd` is the item's own
  end: the extension's for an extension item.
- `HandOverAsync` calls `CloseAsync`, or `CloseExtensionAsync` when `ExtensionId` is set. The log
  lines name the extension id.
- The audit row is `RequestsSentAutomatically`, with `extensionId` in the payload (null for the
  exercise). It is written after the sweep, only for a run that sent or cancelled something.
- The extension item stamps `RequestsSentAt` through the same sweep, so an extension needs no
  manual Send.

## Audit rows

Four admin actions each write one `WindowAdmin` row from `ExerciseExtensionRepository`. `EntityId`
is the window id, `UserId` the admin's subject, and `Timestamp` the UTC instant. The payload is
camelCase JSON. Every payload carries `windowId`, `windowTitle`, `exerciseId`, `exerciseType`,
`extensionId`, `endDate` and `actorName`, plus:

| Action | Further fields | `endDate` is |
|---|---|---|
| `ExtensionAdded` | `schoolCount`, `schools` | the extension's end |
| `ExtensionEdited` | `previousEndDate`, `newEndDate`, `schoolCount`, `schoolsAdded`, `schoolsRemoved` | the new end |
| `ExtensionClosedEarly` | `scheduledEnd`, `newEndDate`, `closedEarly`, `closedBy`, `closedAtUtc` | the new end |
| `ExtensionRequestsSent` | `extensionEnd`, `requestsSent`, `draftsCancelled` | `extensionEnd` |

- **Added, edited and closed early** are written in the same transaction as the change, so a change
  that lost its compare-and-set leaves no row.
- **Requests sent** is written by `RecordRequestsSentAsync` after the manual Send's sweep, in a
  transaction of its own. The controller calls it with `CancellationToken.None` inside a try/catch.
  If the write fails the requests have still gone, and the banner still says so.
- The automatic hand-over row is written by `WindowAdminAuditWriter`.
- The extension has no separate early-closure stamp. The exercise has none either:
  `ExerciseEarlyClosure` is the write record and the `ClosedEarly` audit row is the stamp. The
  extension does the same.

`docs/audit-log.md` describes how the screen shows these rows.

## The admin screens

All three controllers carry `[RequireAdminSection(AdminNavKeys.ManageWindow)]`. Every route is under
`admin/windows/{id:guid}/exercises/{exerciseId:guid}/extensions/`. Each returns 404 for an unknown
window, exercise or extension, and for a data share. Each state check runs again on the POST.

| Verb | Route | Action |
|---|---|---|
| GET, POST | `new` | `ExerciseExtensionController.New`, `Create` |
| GET, POST | `{extensionId:guid}/edit` | `ExerciseExtensionController.Edit`, `Update` |
| GET, POST | `{extensionId:guid}/close` | `CloseExtensionController.Confirm`, `Close` |
| GET, POST | `{extensionId:guid}/send-requests` | `SendExtensionRequestsController.Confirm`, `Send` |

### The summary card

Under the data-files table, an exercise with a kind shows a table captioned "Extensions", or the
sentence "No extensions.". A data share shows neither.

| Column | Content |
|---|---|
| End date | The extension's end, UK time |
| Schools | The number of schools |
| Status | **Open** (green) or **Closed** (grey) |
| Actions | Open: **Edit** and **Close early**. Closed: **Send requests for processing**. Each link has a visually hidden suffix naming the end date. |

An **Add extension** secondary button sits in the card's button group. Success banners come from
`TempData["ExtensionOutcome"]` ("Extension added for 2 schools, ending 12/11/2026 17:00." and its
edit and close-early equivalents).

### Add and edit

The page has the end date and time as `govuk-date-input` and a "Schools" textarea ("One LAESTAB on
each line, as 933/4290 or 9334290."). On edit the textarea is pre-filled in slashed form. The POST
checks in this order: the extension is found, an edit is refused if it has closed, the LAESTAB
lines are read, the warning step runs (rule 8), then `AddAsync` or `EditAsync` checks `NotFound`,
`Closed`, `EndDateNotAfterExercise`, `NoSchools` and `SchoolInAnotherExtension`, and writes. A
failed compare-and-set is `Stale`, shown as "This extension was changed by someone else. Check it
and try again."

### The text guard on edit

Edit saves against the end date the admin was shown: `ExerciseExtensionRepository.UpdateAsync`
moves `EndDate` only where it still equals `PreviousEndDate`. The page carries that value in a
hidden field. It travels as **text** (`PreviousEnd`, written and read by
`ExerciseExtensionItem.GuardFor` and `TryReadPreviousEnd`, round-trip `O` format), not as a
`DateTime?`.

The reason: GovUk.Frontend.AspNetCore 5 takes over every `DateTime?` on a model and looks for
`.Day`, `.Month` and `.Year` parts. A hidden field has none, so the guard bound as null and every
edit came back as a 404. The unit tests missed it because they never run model binding. The E2E
class found it. A guard that is missing or does not parse is a `BadRequest`. A value with an offset
is refused, because exercise dates are wall-clock values.

### Close early

The page follows `Close.cshtml`: a warning with the scheduled end and the school count, type the
window name, and a **Close extension** button. It moves the end date to one second before now and
writes `ExtensionClosedEarly`. It runs **no sweep**. The extension's requests go out by Send
requests, or by the hand-over two hours later, as the ticket specifies.

### Send requests

Offered once the extension has closed. The page shows the counts as `SendRequests.cshtml` does. The
POST runs `CloseExtensionAsync`, then `RecordRequestsSentAsync`, and quotes the result. A sweep that
throws shows a refused banner and records nothing.

## The related fix

Since #466 a window can hold several rows of one kind. Two answers had to agree on which row a
school's journey uses:

- **`CheckingExerciseService.RowFor`.** `IdFor`, `EndDateFor` and `StartDateFor` use one private
  rule. Among the rows of the kind, prefer those that are live now (`IsLiveAt`) and not display
  only. If exactly one is left, it wins. Otherwise the first of the kind in tab order wins, which
  is what they returned before. This is the `CheckingExerciseStorageResolver` rule with a fallback
  in place of a null. It stays date-blind for a closed exercise, as the `IdFor` remark requires.
- **`RequestService`.** The exercise id a request is stamped with comes from the same resolver as
  the release id, and falls back to `IdFor`. See `docs/checking-exercise-releases.md`.

## Not in this change

- **Egress.** It reads by window and `CrmId`. A named school's requests reach it after its
  extension has been sent.
- **Blobs and releases.** An extension adds no blob and no release.
- **The confirm-correct page and the Notify email deadlines.** They still quote the outer window
  end, as they did before (#320). With an extension, that end is the latest extension's end. This
  is a follow-up.
- **The establishment-wide amendment page.** It is unlinked and unchanged.
- **`StatusOf`.** It keeps its meaning: the exercise's own dates, for the admin tag. The
  school-aware answers are new overloads on `ICheckingExerciseService`.
- **The admin user guide PDF.** The source gains a section. The PDF is rebuilt at release.

## Tests

**Unit**

| Class | Pins |
|---|---|
| `CheckingExerciseServiceTests` | School-aware overloads, the `max` rule, extension Open/Closed, `IsWindowShown` past the last exercise end, a blank LAESTAB as no school, `RowFor` |
| `LaestabListTests` | Both forms, blank lines, duplicates, the line-numbered error |
| `ExerciseExtensionServiceTests` | Every `ExtensionSaveStatus`, the warnings and the no-release case, close early |
| `CloseExerciseServiceTests` | The exclude set (open or unsent), `Only` for an extension, the stamp set once and not on failure |
| `AutomaticExerciseHandOverTests` | Due items for extensions, the audit with the extension id |
| `ExerciseExtensionControllerTests` | Add and edit, the warning step and its error, the text guard |
| `CloseExtensionControllerTests`, `SendExtensionRequestsControllerTests` | Gates, banners, the sweep before the audit write, a failed audit write |
| `SummaryControllerCloseActionsTests`, `DeleteExerciseControllerTests` | The card and the delete count |
| `LandingPageControllerTests`, `LandingPageServiceTests`, `CheckYourPupilDataResultsEnquiryOptionTests`, `AmendmentRequestsControllerTests`, `AmendmentRequestsServiceTests`, `BulkSubmissionServiceTests`, `WhatToChangeControllerTests`, `ConfirmCorrectControllerTests`, `ResultIssueControllerTests`, `JourneyControllerTests` | A named school sees its extension's end and another school does not |
| `ChangeRequestReleaseStampTests` | The exercise id a request is stamped with is the resolver's |
| `AuditActivitiesTests`, `WindowAdminAuditPayloadTests`, `AuditLogControllerTests` | The four actions, their payloads and their screen rows |
| `AdminRouteGateTests` | The new routes sit behind the section grant |

**Integration (Postgres)**

| Class | Pins |
|---|---|
| `AdminRequestsRepositorySchoolFilterTests` | `Exclude`, `Only` and `None`, a null LAESTAB, slashed and digit forms |
| `ExerciseExtensionRepositoryTests` | The unique index, the audit row in the same transaction, compare-and-set, the window end re-derived, the stamp, cascade |
| `ExerciseDeletionRepositoryTests`, `WindowRepositoryEarlyClosureTests` | The window end stays at the extension end |
| `CheckingWindowExerciseProjectionTests` | The landing page read carries `DisplayOnly` |

**E2E** (`WindowAdmin/ExerciseExtensionTests`, built window as `CloseExerciseEarlyTests` does)

- `An_admin_adds_an_extension_and_sees_it_on_the_summary` (Smoke): a bad line, the warning and the
  tick, the card, the audit row.
- `An_admin_edits_closes_early_and_sends_requests_for_an_extension` (FullRegression).
- `A_data_share_offers_no_extension` (FullRegression).
