# Feature: Admin establishment impersonation

Approved implementation plan (D-37). This document is the build handoff; no application code was written during planning.

## Scope and dependencies

Implement the approved spec in seven steps. The project platform follows 000-project/D-1 and D-6. Read the project baseline, intent, spec and full append-only decision log before building.

Paths use these prefixes:

- **Application:** `src/DfE.CheckPerformanceData.Application/`
- **Web:** `src/DfE.CheckPerformanceData.Web/`
- **Infrastructure:** `src/DfE.CheckPerformanceData.Infrastructure/`
- **Persistence:** `src/DfE.CheckPerformanceData.Persistence/`
- **Unit:** `tests/DfE.CheckPerformanceData.UnitTests/`
- **Integration:** `tests/DfE.CheckPerformanceData.IntegrationTests/`

New filenames below are proposed implementation files. Existing files are named explicitly. When an endpoint inventory identifies further consumers, record them under the relevant step before editing; do not silently alter requirements. Stop and return to the appropriate planning stage if implementation reveals a specification gap.

Steps 1–3 establish context and lifecycle. Steps 4–6 depend on those foundations. Step 7 completes production audit wiring and integrated verification. Use test audit doubles in earlier steps; do not consider start/exit production-ready until step 7 is complete. Do not expose partially implemented impersonation during deployment.

## Step 1 — Separate viewing context from login authority

**Files to create:** Application `Impersonation/IEstablishmentViewContext.cs`, `Impersonation/EstablishmentViewContext.cs`; Web `Services/EstablishmentViewContextService.cs`; Unit `Impersonation/EstablishmentViewContextTests.cs`.

**Files to change:** Web `Services/CurrentUserService.cs`, `Startup/JourneyAndCmsServicesExtensions.cs`; Application `CurrentUser/ICurrentUserService.cs` only if clarification of the original-context contract is needed.

**Work:** Keep `ICurrentUserService` as the original authenticated user and establishment authority. Introduce a separate effective viewing context carrying selected LAESTAB, URN and ages, without granting roles or changing the login ticket. Outside impersonation, the view context delegates to the normal establishment context. Preserve existing worker implementations. Classify consumers as reads or writes before adapting them (D-33).

**Requirements:** R4, R16, R18, R20.

**How to check:** Run the unit command below with `--filter FullyQualifiedName~EstablishmentViewContext`. Assert that selection changes only effective read identifiers and age eligibility; original user identifier, roles, establishment identifiers and submission authority remain unchanged. Normal and worker contexts retain their existing behaviour.

## Step 2 — Implement protected session state and lifecycle

**Files to create:** Application `Impersonation/IImpersonationSessionStore.cs`, `Impersonation/IImpersonationSessionLock.cs`; Web `Impersonation/ImpersonationSessionService.cs`, `Impersonation/AuthenticatedSessionBinding.cs`, `Middleware/ImpersonationContextMiddleware.cs`; Infrastructure `Impersonation/DistributedImpersonationSessionStore.cs`; Persistence `Locking/PostgresImpersonationSessionLock.cs`; Integration `Impersonation/ImpersonationSessionTests.cs`.

**Files to change:** Infrastructure `Authentication/DistributedCacheTicketStore.cs`; Web `Startup/RequestPipelineExtensions.cs`, `Startup/JourneyAndCmsServicesExtensions.cs`, `Session/SessionExtensions.cs`; registration files for new infrastructure and persistence services.

**Work:** Bind authoritative impersonation state to the validated authentication-ticket handle, original user and rotating CPD session identity. Keep the target record separate from ordinary MVC session snapshots and from the login ticket. Never expose or log credential handles. Reuse the existing distributed-cache protection and PostgreSQL advisory-lock patterns. Load context after session and authentication are available and before consumers act on it (D-34).

Serialize start/exit and affected writes across replicas using the per-session lock; it must cover the authoritative state check through completion of a permitted write. Nested service checks reuse the request's existing lock rather than deadlocking. Rotate a context generation on mode changes. Clear selected-window, request journey, bulk selection and editing state on transitions without copying them into the new establishment. Commit invalidation before exit returns, and prevent a concurrent ordinary-session commit from resurrecting target state. Store a revoked/changed generation long enough to reject outstanding stale forms. Use existing session expiry; add no new user-facing timeout configuration.

Each request checks both roles on the original principal. Missing roles, invalid authentication binding or expired/replaced session identity invalidate target access. Storage or lock failure must deny impersonated access/writes rather than falling back to another establishment. The middleware must preserve anonymous/static-asset/authentication-callback behaviour and existing middleware ordering constraints.

**Requirements:** R10, R11, R12, R13, R14, R15.

**How to check:** Run the integration command with `--filter FullyQualifiedName~ImpersonationSession`. Use two clients sharing one session and another with a separate session. Verify shared selection, exit across subsequent requests, expiry, sign-out and re-login. Use deterministic concurrent requests on separate service instances to prove stale state cannot reactivate and mode changes cannot redirect an in-flight write into a different establishment. Assert no handles appear in captured logs.

## Step 3 — Add the admin option and manual setup form

**Files to create:** Web `Controllers/ImpersonationController.cs`, `Controllers/ViewModels/ImpersonationViewModel.cs`, `Views/Impersonation/Index.cshtml`, `Admin/Nav/ImpersonationNavEntry.cs`, `Impersonation/ImpersonationAccessPolicy.cs`; Unit `Web/Impersonation/ImpersonationControllerTests.cs`, `Web/Impersonation/ImpersonationAccessTests.cs`.

**Files to change:** Web `Controllers/AdminController.cs`, `Controllers/WikiConstants.cs`, `Admin/RequireAdminSectionAttribute.cs`, `Admin/Nav/AdminNavKeys.cs`, `Controllers/ViewModels/AdminNavNodeViewModel.cs`, `Extensions/AdminNavServiceCollectionExtensions.cs`; existing admin navigation tests where appropriate.

**Work:** Add `cypmd_impersonation` and require it together with `cypmd_admin` on original authenticated claims. Both roles automatically expose the option, even without section grants; grants alone never enable it. Allow these users to reach the admin landing entry without broadening access to other admin options. The form collects LAESTAB, URN, lowest age and highest age. Require numeric whole-year ages only; apply no identifier verification or extra age bounds/ordering checks. Avoid implicit model-binding required validation on the identifier fields. Protect start and exit with anti-forgery checks and local redirect destinations. To change target establishment, exit and use the setup form again.

**Requirements:** R1, R2, R3, R12.

**How to check:** Run the unit command with `--filter FullyQualifiedName~Impersonation`. Test no login, neither role, each role alone, both roles without grants, and unrelated grants. Only the conjunction permits access. Verify missing/invalid anti-forgery tokens cannot start/exit, numeric age errors prevent start, and arbitrary manual identifiers trigger no validation or directory lookup. Other admin section access remains unchanged.

## Step 4 — Build the establishment viewing experience

**Files to create:** Application `AmendmentRequests/IDraftRequestViewService.cs`, `AmendmentRequests/DraftRequestViewService.cs`; Web `Controllers/ViewModels/DraftRequestViewModel.cs`, `Views/AmendmentRequests/DraftDetails.cshtml`; Unit `Impersonation/ImpersonationLandingPageTests.cs`, `AmendmentRequests/DraftRequestViewServiceTests.cs`; Integration `Impersonation/ImpersonationViewingTests.cs`.

**Files to change:** Application `LandingPage/LandingPageService.cs`, `LandingPage/ILandingPageService.cs`, `CheckYourPupilData/CheckYourPupilDataService.cs`, `AmendmentRequests/AmendmentRequestsService.cs`, `AmendmentRequests/SubmittedRequestService.cs`, `AmendmentRequests/BulkSubmissionService.cs` for its read/review operations only; Web `Controllers/LandingPageController.cs`, `Controllers/ViewModels/LandingPageViewModel.cs`, `Views/LandingPage/Index.cshtml`, `Controllers/AmendmentRequests/AmendmentRequestsController.cs`; Application `RequestSubmission/IRequestRepository.cs` and Persistence `Repositories/RequestRepository.cs` for scoped draft projections. Adapt additional establishment read consumers identified by the inventory.

**Work:** Use effective target context on reads; retain original context on writes. In impersonation, bypass the original-organisation lookup and construct landing eligibility from numeric ages using the existing overlap rules. List available windows with selected-LAESTAB data and matching key stages. Label establishment details using LAESTAB/URN, never original name/address/eligibility. Preserve checking-window dates and existing access restrictions; do not open closed journeys. Blank, malformed, unknown or mismatched manual identifiers must produce a controlled unavailable/empty response, never another establishment's data or an unhandled numeric parse error.

Provide a display-only view of existing drafts and evidence where the normal journey permits it. Do not call the editing/resume route or emit draft-editing events to inspect a draft (D-35). Scope request/pupil/file reads to the target context and keep downloads, filtering and pagination available only when they have no business-write side effects. A required target-only metadata dependency that cannot be satisfied must return an explanation rather than borrowing original metadata.

**Requirements:** R4, R6, R16, R21.

**How to check:** Run the unit command with `--filter 'FullyQualifiedName~ImpersonationLandingPage|FullyQualifiedName~DraftRequestViewService|FullyQualifiedName~LandingPage'` and the integration command with `--filter FullyQualifiedName~ImpersonationViewing`. Seed two establishments with distinct pupil/result/request/evidence data. Verify correct target scoping, the exact existing age-overlap boundaries, downloads, draft display, unavailable windows and empty states. Assert no original establishment API lookup or business mutations occur while viewing.

## Step 5 — Enforce read-only behaviour at request and write boundaries

**Files to create:** Web `Impersonation/ImpersonationEndpointPolicy.cs`, `Impersonation/ImpersonationReadOnlyFilter.cs`, `Impersonation/EstablishmentContextStamp.cs`; Application `Impersonation/IImpersonationWriteGuard.cs`; Web implementation of that guard; Unit `Impersonation/ImpersonationWriteGuardTests.cs`; Integration `Impersonation/ImpersonationWriteProtectionTests.cs`.

**Files to change:** Web `Startup/CoreWebExtensions.cs`, `Startup/RequestPipelineExtensions.cs`, `Session/SessionExtensions.cs`, `Controllers/Journey/JourneyController.cs`, `Controllers/AmendmentRequests/AmendmentRequestsController.cs`, `Controllers/CheckYourPupilData/CheckYourPupilDataController.cs`; Application `RequestSubmission/RequestService.cs`, `AmendmentRequests/BulkSubmissionService.cs`, `RequestSubmission/IRequestRepository.cs`; Persistence `Repositories/RequestRepository.cs`; writable forms and controllers identified by the endpoint inventory; existing request and bulk-submission tests.

**Work:** Inventory MVC and other user-reachable endpoints by actual effects, including GET actions that edit/resume/delete and routes that upload or enqueue work. While impersonating, deny unknown actions by default and permit only reviewed views, harmless navigation and necessary lifecycle/security operations (D-36). Do not assume every GET is safe or every POST changes business data. Block admin sections as required by step 6. Provide a clear read-only response.

Place write guards before persistence, file uploads, notifications, queueing and external submissions. Single, bulk, results-enquiry, confirmation, draft-save and withdrawal flows must use original authenticated establishment identifiers and scoped ownership checks. Check request, pupil and journey ownership using available authoritative application data; manual input is never submission authority. Retain worker behaviour for legitimate background operations rather than treating workers as impersonating.

Carry a server-protected establishment/mode generation on writable forms and saved journey state. Validate it under the session lock before writing, including after exit; a form loaded under an earlier context cannot become a write against the restored original establishment. Do not introduce a live auth-server call. Ordinary browser requests cannot supply role claims or activate target context via query/form/cookie/header values.

**Requirements:** R8, R9, R10, R11, R15, R18, R20.

**How to check:** Run the unit command with `--filter 'FullyQualifiedName~ImpersonationWriteGuard|FullyQualifiedName~RequestService|FullyQualifiedName~BulkSubmissionService'` and the integration command with `--filter FullyQualifiedName~ImpersonationWriteProtection`. Directly call every inventoried mutation category while active. Assert no business rows, files, messages, notifications or external calls result. Exercise stale forms after exit, forged identifiers, cross-establishment request/pupil references, forged context stamps and deterministic concurrency across instances. Legitimate non-impersonated writes must still succeed using original login context. Known safe navigation POSTs remain usable; unclassified endpoints are denied.

## Step 6 — Add the red banner and remove confusing controls

**Files to create:** Web `Views/Shared/_ImpersonationBanner.cshtml`, `Views/Impersonation/ReadOnly.cshtml`; Unit `Web/Impersonation/ImpersonationLayoutRenderTests.cs`, `Web/Impersonation/ImpersonationViewRenderTests.cs`.

**Files to change:** Web `Views/Shared/_Layout.cshtml`, `Views/Shared/_AdminLayout.cshtml`, `Views/Shared/_AdminWideLayout.cshtml`, `Views/Shared/_ShareLayout.cshtml` where session-authenticated output is relevant, `wwwroot/css/site.css`, `Admin/RequireAdminSectionAttribute.cs`, `Views/AmendmentRequests/Index.cshtml`, `Views/CheckYourPupilData/Index.cshtml`, `Views/Journey/Summary.cshtml`; remaining rendered controls/layouts identified by the inventory.

**Work:** Put the prominent red banner at the top of every rendered application page during impersonation, including supported error responses. Include textual mode identification, selected identifiers, read-only explanation and keyboard-accessible anti-forgery-protected Exit impersonation button. Use safe HTML encoding for manually entered identifiers. Hide all admin navigation, tiles, sections and links, as well as business editing/submission controls; retain viewing, downloads and sign-out. Direct admin URLs return an explanation with the banner/exit action instead of rendering admin content. Do not render admin sections through a shared layout before checking mode. Exit restores normal context and permitted navigation. Already-rendered tabs need not refresh automatically; subsequent requests are authoritative.

**Requirements:** R5, R7, R13, R19.

**How to check:** Run the unit command with `--filter 'FullyQualifiedName~ImpersonationLayoutRender|FullyQualifiedName~ImpersonationViewRender|FullyQualifiedName~AdminNav|FullyQualifiedName~LayoutRender'`. In a browser, inspect landing, pupils/results, drafts, direct admin URLs and representative error pages. Verify contrast, non-colour mode text, keyboard operation, escaped input, hidden admin/edit controls and normal restoration after exit. Confirm another tab's next request observes exit and its stale form is rejected by step 5.

## Step 7 — Add auditing and complete regression verification

**Files to create:** Application `Audit/IImpersonationAuditWriter.cs`, `Audit/ImpersonationAuditPayload.cs`; Persistence `Repositories/ImpersonationAuditWriter.cs`; Unit `Audit/ImpersonationAuditPayloadTests.cs`; Integration `Impersonation/ImpersonationAuditTests.cs`.

**Files to change:** Application `Audit/AuditActivities.cs`, `Audit/IAuditLogRepository.cs`; Persistence `Repositories/AuditLogRepository.cs`; Web `Controllers/AuditLog/AuditLogViewModels.cs`, `Controllers/AuditLog/AuditLogCsv.cs`, `Views/AuditLog/Index.cshtml`, impersonation lifecycle service and read-only/write guards; audit and login/submission metric producers identified by the inventory. Update existing audit tests and relevant service registrations.

**Work:** Reuse existing AuditEntry persistence and immutable-audit rules to record start, exit and blocked changes with original actor, selected LAESTAB/URN, UTC timestamp and outcome. Store/display a dedicated minimal payload; never reuse arbitrary submitted form data, raw request bodies, pupil information, auth tokens or session handles. Show target identifiers and outcomes in the existing audit UI/export. Avoid duplicate events when both request and service guards reject the same attempt. Do not count impersonated views as establishment logins, edits or submissions. Preserve normal login recording and audit behaviour. No new configuration secrets or identifier allow-list entries are needed.

**Requirements:** R17, R18; integrated regression verification of R1–R21.

**How to check:** Run the integration command with `--filter FullyQualifiedName~ImpersonationAudit`. Verify start/exit/blocked events, original actor identity, selected identifiers, timestamps and outcomes in persistence, UI and CSV. Capture logs and audit payloads to prove credentials/session handles are absent. Verify viewing does not emit establishment login/submission metrics. Then run both complete test commands and the Web build below, with the repository's existing PostgreSQL/Azurite fixtures where required. Existing authentication, ticket-store, admin navigation, landing-page, journey, request/bulk submission, repository and audit tests must pass. Report unavailable prerequisites rather than claiming a check passed.

## Verification commands

All commands run from the repository root. Append the step's `--filter` to the relevant test command during focused work. Run the complete suites once at the integrated verification stage; repeat only when later changes or failures warrant it.

```sh
dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.Application.UnitTests.csproj
dotnet test tests/DfE.CheckPerformanceData.IntegrationTests/DfE.CheckPerformanceData.IntegrationTests.csproj
dotnet build src/DfE.CheckPerformanceData.Web/DfE.CheckPerformanceData.Web.csproj
git diff --check
```

These are implementation checks, not checks already run during planning. If implementation affects configuration or credentials, inspect staged changes and run the repository secret scanner before completion. Do not weaken existing hooks, scans or ignore rules.

## Requirement → step coverage

| Requirement | Steps |
|---|---|
| R1 | 3 |
| R2 | 3 |
| R3 | 3, 4 |
| R4 | 1, 4 |
| R5 | 6 |
| R6 | 4 |
| R7 | 6 |
| R8 | 5 |
| R9 | 5 |
| R10 | 2, 5 |
| R11 | 2, 5 |
| R12 | 2, 3 |
| R13 | 2, 6 |
| R14 | 2 |
| R15 | 2, 5 |
| R16 | 1, 4 |
| R17 | 7 |
| R18 | 1, 5, 7 |
| R19 | 6 |
| R20 | 1, 5 |
| R21 | 4 |

Step 7 runs regression verification across every requirement. There are no earlier feature increments to supersede; regression scope includes the existing establishment and admin journeys.

## Definition of done

- All seven steps and their checks are complete, with evidence for R1–R21.
- Both roles are required; no ordinary user can alter their establishment or activate impersonation.
- Manual identifiers and ages drive target viewing without directory verification or original metadata leakage.
- All business changes are blocked during impersonation at server boundaries; direct, stale and concurrent requests cannot bypass protection.
- Exit, sign-out and expiry end the session-bound selection; stale context cannot be restored or reused for writes.
- Downloads and existing drafts remain readable under normal journey restrictions; no editing or submission is enabled.
- Red banner and exit are visible and accessible; admin sections are hidden and direct admin access is blocked while active.
- Required audit events are attributable and contain no credentials or session handles; genuine establishment metrics remain accurate.
- Appropriate focused tests, complete regression suites, Web build and browser verification pass. Material limitations are reported explicitly.
- Existing authentication, ordinary establishment submissions, worker behaviour and admin section grants remain correct.
- Review staged changes and satisfy the repository secret-handling instructions; no protections are weakened.
- Mark the increment `built` only after these conditions are satisfied. Do not mark it built merely because implementation files exist.
