# Request Journey

This document describes the full user flow from the Check Your Pupil Data page through to submission or draft-save of a change request. It covers URLs, session state, blob storage, and the database at each stage.

---

## Overview

A change request is initiated when a school user decides they want to amend a pupil's record. The journey proceeds through five distinct phases:

```
Check Your Pupil Data
        ↓
What Would You Like to Change?
        ↓
Pupil Search
        ↓
Question Flow  (one or more pages, config-driven)
        ↓
Summary  →  Submit  →  Confirmation
              or
           Save Draft  →  Check Your Pupil Data
```

Throughout the journey, state is accumulated in an ASP.NET session keyed by `windowId`. No data is written to permanent storage until either submission or a draft save.

---

## Stage 1 — Check Your Pupil Data

**URL:** `GET /CheckYourPupilData/{windowId}`  
**Controller:** `CheckYourPupilDataController.Index`

This page lists the pupils in a checking window. Navigating here **clears any existing request state** for the window:

```csharp
HttpContext.Session.ClearRequestState(windowId);
```

This means navigating back to this page from mid-journey abandons any in-progress request. The `windowId` is a `Guid` matching the `CheckingWindow.Id` in the database.

From here, the user clicks a "Request change" or equivalent link for a specific pupil, which takes them to:

---

## Stage 2 — What Would You Like to Change?

**URL:** `GET /WhatToChange/{windowId}`  
**Controller:** `WhatToChangeController.Index` / `Confirm`

The user selects what they want to change (e.g. `Remove`). On POST:

```csharp
HttpContext.Session.SaveRequestState(windowId, s =>
{
    s.SelectedWhatToChange = vm.SelectedWhatToChange;
    s.CheckingWindow = window;          // CheckingWindowDto loaded from DB
});
```

**Session after this stage:**
| Field | Value |
|---|---|
| `SelectedWhatToChange` | e.g. `WhatToChange.Remove` |
| `CheckingWindow` | `CheckingWindowDto` (Id, Title, Type, StartDate, EndDate) |

**Redirects to:** `GET /PupilSearch/{windowId}`

---

## Stage 3 — Pupil Search

**URL:** `GET /PupilSearch/{windowId}`  
**Controller:** `PupilSearchController`

The user searches for and selects the pupil they want to raise the request about. The search uses an autocomplete (`GET /PupilSearch/{windowId}/suggestions?query=...`) that queries pupils from the PostgreSQL database via `ICheckYourPupilDataService`.

On POST (pupil selected):

```csharp
HttpContext.Session.SaveRequestState(windowId, s =>
{
    s.SelectedPupil = pupil;            // PupilDto from blob storage
    s.SelectedPupilId = ...;
    s.SelectedPupilLabel = ...;
    s.ReferenceNumber = reference;      // e.g. "CYPMD_KS4June_A3F8D12"
    s.QuestionAnswers = new();
    s.QuestionHistory = new();
});
```

The **reference number** is generated here and stays fixed for the life of the request:

```
CYPMD_{CheckingWindowType}_{7-char random uppercase hex}
e.g.  CYPMD_KS4June_A3F8D12
```

**Session after this stage:**
| Field | Value |
|---|---|
| `SelectedPupil` | `PupilDto` (name, DOB, UPN, Cypmd_Id, etc.) |
| `ReferenceNumber` | `"CYPMD_KS4June_A3F8D12"` |
| `QuestionAnswers` | `{}` (empty) |
| `QuestionHistory` | `[]` (empty) |

**Redirects to:** `GET /Journey/{windowId}/page/{firstPageId}` — the first page of the question flow.

---

## Stage 4 — Question Flow

**URLs:**
- `GET /Journey/{windowId}/page/{pageId}` — display a page
- `POST /Journey/{windowId}/page/{pageId}` — submit answers and advance
- `POST /Journey/{windowId}/page/{pageId}/question/{questionId}/upload` — upload a file
- `POST /Journey/{windowId}/page/{pageId}/question/{questionId}/remove` — remove a file

**Controller:** `JourneyController`

### Config

The question flow is defined by a JSON file in `Web/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json` (e.g. `Remove_KS4June.json`). This file is uploaded to Azure Blob Storage on startup in dev via `SeedQuestionFlows`, and fetched at runtime via `IQuestionFlowBlobClient`.

The config is cached in-process with `Priority = NeverRemove` — it is fetched from blob storage once per application lifetime per flow type.

Each page in the JSON has:
- `id` — unique string identifier
- `type` — `Question` (default), `Content`, or `EvidenceUpload`
- `questions` — list of questions with `type` (`Radio`, `TextArea`, `FreeText`, `Date`, `FileUpload`)
- `nextPageId` — default next page (overridden per radio option for branching)

### Navigation guard

Every `Page` GET runs `IQuestionFlowService.GetNavigationGuard` before rendering. This prevents users from:
- Jumping ahead to an unvisited page (redirected to the correct next page)
- Revisiting a page after completing the journey (redirected to Summary)

Pages already in `QuestionHistory` are always allowed.

### Answering questions

On each `PagePost`:
1. Answers are validated (`IJourneyValidationService.ValidateAnswer`)
2. If valid, answers are written to `QuestionAnswers` and the page ID is appended to `QuestionHistory`
3. The next page is determined from the config (radio answers can branch)

**Session after each page:**
| Field | Updated to |
|---|---|
| `QuestionAnswers` | `{ "reason": { TextValue: "social-care-involvement" }, ... }` |
| `QuestionHistory` | `["reason", "social-care", ...]` — ordered list of visited page IDs |

### File uploads (EvidenceUpload page type)

The evidence page has `type: "EvidenceUpload"` in the JSON and renders a distinct Razor view (`EvidenceUpload.cshtml`). File uploads are handled separately to answer submission:

- Each upload is a separate POST to `/Journey/{windowId}/page/{pageId}/question/{questionId}/upload`
- The file is read as bytes, validated (must be PDF, ≤ 10 MB, ≤ 6 total pages across all uploads)
- If valid, the bytes are stored via `IFileStorageService` (`EvidenceBlobStorageService`) to:
  ```
  Container: {windowId}
  Blob:      evidence-uploads/{newGuid}
  ```
  The GUID blob name is returned and stored in session as `FileAnswer.StoredFileName`. The original filename and page count are also stored in session but the bytes themselves live only in blob storage.
- Removing a file (`/remove`) deletes the blob and removes the `FileAnswer` entry from session

The text area on the evidence page is submitted via the normal `PagePost` route.

### Content pages

Pages with `type: "Content"` display CMS-managed content (via `EditableContent` view components) and have no questions. The user clicks Continue, which adds the page to history and moves to the next page.

---

## Stage 5 — Summary

**URL:** `GET /Journey/{windowId}/summary`  
**Controller:** `JourneyController.Summary`

The summary page renders a GOV.UK summary list of all answers. It can only be reached when `QuestionHistory` is complete (i.e. `GetNextPageId` after the last visited page returns null). Incomplete journeys are redirected back to the next unanswered page.

Each row shows the resolved question title and display answer. Radio answers show their label (not raw value). File uploads show filename and page count. Dates show as DD/MM/YYYY.

The user can click "Change" on any row to return to that page (`fromSummary=true`). When they submit from a summary-edit page, only pages up to and including the edited page are kept in history — any later pages that depended on a changed radio branch are trimmed.

From the Summary, the user can:
- **Submit the request** → `POST /Journey/{windowId}/summary`
- **Save and continue later** → `POST /Journey/{windowId}/draft`

---

## Stage 6a — Submission

**URL:** `POST /Journey/{windowId}/summary`  
**Controller:** `JourneyController.SummaryConfirm`

### What happens

1. **Idempotency check** — `IRequestRepository.IsSubmittedAsync(referenceNumber)` — if a `ChangeRequest` row with this reference number already exists with `Status = Submitted`, return silently. This handles double-taps and back-button resubmits.

2. **Build `RequestDocument`** — a structured document containing:
   - Reference number, submitted-at timestamp
   - Submitted-by (user ID and display name from `ICurrentUserService`)
   - School details (URN and name from `ICurrentUserService`)
   - Pupil details (from session `SelectedPupil`)
   - All answers in submission order (radio answers resolved to labels, dates formatted DD/MM/YYYY, files listed by filename)

3. **Save request blob** — `IRequestBlobClient.SaveRequestAsync(windowId, document)` writes a JSON file to:
   ```
   Container: {windowId}
   Blob:      request_{referenceNumber}.json
   ```

4. **Upsert DB row** — `IRequestRepository.UpsertAsync(data)` with `Status = Submitted`. If a Draft row already exists for this reference number it is updated to Submitted; otherwise a new row is inserted into `ChangeRequests`.

5. **Partial session wipe** — Journey-specific fields are cleared so that back-button resubmit hits `IsSessionReady → false`:
   ```csharp
   s.SelectedWhatToChange = null;
   s.SelectedPupil = null;
   s.QuestionAnswers = new();
   s.QuestionHistory = new();
   // ReferenceNumber and CheckingWindow preserved for Confirmation page
   ```

6. **Redirect** → `GET /Journey/{windowId}/confirmation`

### ChangeRequests database row (submitted)

| Column | Value |
|---|---|
| `Id` | New `Guid` (or existing Draft row's Id) |
| `WindowId` | The checking window GUID |
| `ReferenceNumber` | e.g. `CYPMD_KS4June_A3F8D12` |
| `Status` | `Submitted` |
| `OrganisationUrn` | School URN (long) |
| `PupilUpn` | Pupil's UPN |
| `PupilFirstname` / `PupilSurname` | From session |
| `Submitted` | UTC timestamp (`timestamp without time zone`) |
| `SubmittedById` | DfE Sign-In user GUID |
| `SubmittedByName` | User's display name |

---

## Stage 6b — Save Draft

**URL:** `POST /Journey/{windowId}/draft`  
**Controller:** `JourneyController.SaveDraft`

Available from:
- The **EvidenceUpload page** — button uses `formaction` to override the form's action, so the text area value is included in the POST. The `pageId` is passed so the controller can capture the unsaved text area answer into session before saving.
- The **Summary page** — no `pageId`, answers are already in session.

### What happens

1. If `pageId` is provided: any non-file answers on that page are read from the form and saved to session. This handles the case where the text area has been filled in but not yet submitted.

2. **Save draft blob** — `IDraftBlobClient.SaveDraftAsync(windowId, referenceNumber, journey)` writes the full `RequestState` as JSON to:
   ```
   Container: {windowId}
   Blob:      draft_requests/{referenceNumber}.json
   ```
   This preserves everything needed to resume the journey — answers, history, pupil selection, reference number.

3. **Upsert DB row** — `IRequestRepository.UpsertAsync(data)` with `Status = Draft`. Creates the row if it doesn't exist, or updates it (e.g. if the user saves multiple times mid-journey).

4. **Redirect** → `GET /CheckYourPupilData/{windowId}`

### ChangeRequests database row (draft)

Same structure as submitted, but `Status = Draft`. The `Submitted` column holds the time the draft was last saved.

---

## Stage 7 — Confirmation

**URL:** `GET /Journey/{windowId}/confirmation`  
**Controller:** `JourneyController.Confirmation`

Reads `ReferenceNumber` and `CheckingWindow.EndDate` from the remaining session state. If either is absent, redirects to Check Your Pupil Data. Otherwise displays the reference number and the window close deadline.

---

## Session structure (`RequestState`)

`RequestState` (`Application/Journey/RequestState.cs`) is serialised to JSON and stored in the ASP.NET distributed session under the key `request_{windowId}`.

| Property | Type | Set at |
|---|---|---|
| `SelectedWhatToChange` | `WhatToChange?` | Stage 2 |
| `CheckingWindow` | `CheckingWindowDto?` | Stage 2 |
| `SelectedPupil` | `PupilDto?` | Stage 3 |
| `SelectedPupilId` | `string?` | Stage 3 |
| `SelectedPupilLabel` | `string?` | Stage 3 |
| `ReferenceNumber` | `string?` | Stage 3 |
| `QuestionAnswers` | `Dictionary<string, QuestionAnswer>` | Stage 4 |
| `QuestionHistory` | `List<string>` | Stage 4 |
| `SelectedNextStep` | `NextSteps?` | Not used in journey (future) |

`IsSessionReady` checks that `SelectedWhatToChange`, `CheckingWindow`, and `SelectedPupil` are all non-null. Any action that requires a valid journey redirects to Check Your Pupil Data if this check fails.

---

## Blob storage layout

All blobs for a checking window share a container named by the window's GUID:

```
Container: f34d285b-8660-4d12-9c30-787328deaa0a   ← windowId
│
├── request_{referenceNumber}.json                  ← Submitted RequestDocument
│
├── draft_requests/
│   └── {referenceNumber}.json                      ← Serialised RequestState (draft)
│
└── evidence-uploads/
    └── {guid}                                      ← Uploaded PDF bytes (no extension)
```

The `question-flows` container is separate and global:

```
Container: question-flows
└── Remove_KS4June.json                             ← Question flow config
```

---

## Guard rails and failure modes

**Navigation guards** — `GetNavigationGuard` prevents URL manipulation to skip pages or jump branches. Attempts to access an out-of-order page redirect to the correct next page.

**Double-submit on confirmation** — the idempotency check (`IsSubmittedAsync`) means re-firing the confirmation POST (back button, double click, network retry) is silent and safe. The blob write is also idempotent (`overwrite: true`). The session partial wipe means `IsSessionReady` returns false for subsequent attempts after the first, providing a second layer of defence.

**Draft before submission** — if a Draft DB row exists when submission occurs, `UpsertAsync` updates it to Submitted rather than inserting a duplicate. A Draft row therefore never co-exists with a Submitted row for the same reference number.

**Session expiry** — sessions have a 30-minute sliding expiry. If a session expires mid-journey, `IsSessionReady` returns false and the user is redirected to Check Your Pupil Data. Any uploaded files remain in blob storage (orphaned) and any saved draft remains accessible via the draft blob and DB row.
