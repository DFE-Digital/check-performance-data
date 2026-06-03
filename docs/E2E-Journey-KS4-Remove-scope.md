# E2E testing scope — KS4 Remove journey

Scoping notes for extending the Playwright suite to cover the KS4 Remove journey. The existing suite is wiki/CMS-only; this doc captures what already works, what needs building, and how to prioritise.

See [E2E-Playwright.md](E2E-Playwright.md) for the harness architecture and how to run the suite.

---

## What already works in the harness

- **Auth**: the `/dev/impersonate/*` endpoints bypass DfE SignIn entirely. Playwright already threads the cookie into the browser context via `SeedingPageTest`.
- **Dev data seeded on startup**: `DevDataSeeder` seeds pupils for Minehead Middle School (URN `136774`) into the open KS4June checking window at startup — 15 included and 15 non-included pupils. No extra per-test seeding API is needed for pupils.
- **Harness is solid**: fixture lifecycle, impersonation, antiforgery helpers, `SeedingPageTest` base class — all reusable without modification.

---

## The one structural blocker

**The `organisation_urn` claim is not set by dev impersonation.**

`CurrentUserService.OrganisationUrn` reads the `organisation_urn` claim, which `ClaimsEnrichmentService` populates from the DfE SignIn organisations API during a real OIDC login. The dev impersonation ticket (`DevImpersonationTicketBuilder`) never mints this claim, so it returns `""` for any impersonated principal.

Every journey entry point depends on a non-empty URN:

- `CheckYourPupilDataController` — filters pupil lists by `urn`
- `CheckYourPupilDataService` — all five methods filter on `currentUserService.OrganisationUrn`
- `RequestService.ConfirmRequestAsync` / `SaveDraftAsync` — writes the URN into the request document and DB row

Without the claim, the CYPD page shows no pupils and the journey can't start.

**Fix:** add a new dev impersonation endpoint (e.g. `/dev/impersonate/school-user`) that sets `organisation_urn = "136774"` (Minehead Middle School, seeded with included pupils) and optionally `organisation_type_id` (needed for the `SchoolIsIndependent` condition — see below). A dedicated persona is cleaner than overloading the existing `editor`/`user` ones, which have clear wiki/CMS semantics.

---

## What needs building

| Piece | Effort |
|---|---|
| `/dev/impersonate/school-user` endpoint with `organisation_urn` claim | Small (1–2 h) |
| `AuthHelpers.ImpersonateAsSchoolUserAsync` | Tiny |
| `JourneyHelpers` — navigate from landing → CYPD → WhatToChange → PupilSearch → select a pupil → first journey page | Medium (half day) |
| Minimal test PDF fixture embedded in the test project | Tiny |
| Test cases (see scope below) | Medium–large |

### `JourneyHelpers`

A static helper class (same pattern as `SeedHelpers`) that drives the browser through the fixed pre-journey steps shared by every Remove test:

1. Navigate to the landing page and click into the open KS4June window.
2. Select "Remove" on the WhatToChange page.
3. Search for and select a specific pupil (by name).
4. Return the `windowId` Guid and the current `Page`, ready for the first journey question.

This is the heaviest helper to write but also the most reusable — every journey test will call it.

---

## Test scope

The flow has 12 removal reasons. Full branch coverage is ambitious; a pragmatic two-phase split:

### Phase 1 — golden paths (~1 day)

Test 3–4 representative end-to-end paths from the first question page through to the confirmation page.

| Path | Why it's representative |
|---|---|
| **Pupil has died** → date → evidence upload → summary → confirm | Simplest linear path; exercises Date question type and the required `FileUpload`+`TextArea` EvidenceUpload page |
| **Permanently left England** → autocomplete country → date → evidence → confirm | Exercises the `Autocomplete` question type (country picker) |
| **Year group change** → higher/lower radio → sub-question → evidence → confirm | Exercises branching via option-level `nextPageId` |
| **Dual registered** → DfE number → evidence → confirm | Exercises `FreeText` question type |

Also confirm the **"Change" link from summary** works: clicking "Change" beside a row navigates back to that page, re-answers it, and returns to the summary.

### Phase 2 — validation and edge cases (~half day)

These can be thin — just enough to confirm the error summary and field errors render correctly.

| Scenario | What it exercises |
|---|---|
| Radio page — no option selected | `validationFailure` message appears in error summary |
| Date page — invalid date (e.g. 31/02/2025) | Date validation error |
| TextArea — exceeds `charLimit` | Character limit error |
| EvidenceUpload page (`requireAtLeastOne`) — continue with nothing uploaded and text empty | `RequireAtLeastOneResult` error summary lead-in |
| Duplicate request conflict | Final confirm returns the conflict error banner |

### Phase 3 — conditional visibility (optional, ~half day)

The `SchoolIsIndependent` condition gates the "Not on roll" removal reason. Testing it end-to-end requires:

1. A second impersonation persona with `organisation_type_id = "11"` (Other Independent School) — so the "Not on roll" option is visible.
2. Confirming that for the standard Minehead URN (non-independent), the option is absent from the reasons page.

---

## Parallelism and session isolation

The journey is entirely session-driven. ASP.NET Core sessions are keyed by the browser session cookie, so each Playwright browser context gets an independent session automatically — tests are safe to run in parallel.

The one constraint: if two tests select the same named pupil concurrently, `RequestService.ConfirmRequestAsync` will throw `DuplicateRequestException` on whichever commits second (there is a unique constraint on `(CheckingWindowId, PupilId)` in the `ChangeRequests` table). `JourneyHelpers` should either:

- select a pupil by index (test 1 picks pupil 0, test 2 picks pupil 1), or
- select by a fixed name per test class.

With 15 included pupils seeded for Minehead, there is headroom for concurrent tests without collision.

---

## What is not in scope

- Real DfE SignIn OIDC — same boundary as the rest of the suite.
- File download verification of uploaded evidence (the `/Journey/{windowId}/evidence/{storedFileName}` endpoint) — not on the critical path.
- Rules Engine processing of submitted requests — that is an async background concern and belongs in integration tests.

---

## Summary

| Work item | Effort |
|---|---|
| `organisation_urn` claim in a new `school-user` impersonation persona | 1–2 h |
| `JourneyHelpers` pre-journey navigation helper + test PDF fixture | Half day |
| Phase 1 golden paths (3–4 removal reasons end-to-end) | ~1 day |
| Phase 2 validation and edge cases | ~half day |
| Phase 3 `SchoolIsIndependent` conditional visibility | ~half day (optional) |
| **Total (Phase 1+2)** | **~2 days** |
