# Admin establishment impersonation implementation

Implemented on 2026-10-10. Status: implemented; browser verification remains pending because this session has no connected browser. Do not mark the increment `built` until the browser checks in plan step 6 are complete.

## Result

The admin landing page exposes **View as establishment** only to an authenticated user with both `cypmd_admin` and `cypmd_impersonation`. The setup form accepts manual LAESTAB, URN and numeric lowest/highest ages without directory or identifier validation.

The separate viewing context changes establishment reads without rewriting login claims. A protected distributed record binds the selection to the validated login ticket, original actor and rotating CPD session identity. PostgreSQL advisory locking serializes transitions and affected requests across instances. Protected form and AJAX context stamps reject forms from a different session or mode generation. Ordinary session expiry, sign-out and re-login prevent selection reuse.

An explicit reviewed endpoint inventory permits viewing and downloads during impersonation; unreviewed routes and business changes are denied, including changes made through GET routes. The submission service independently checks original establishment authority, journey window and pupil ownership before writes. Existing request references cannot transfer between windows, URNs or LAESTABs. Workers retain their original context.

The red banner identifies read-only impersonation, safely encodes the target identifiers and offers an anti-forgery-protected exit button. Admin navigation, edit/delete/submission controls and CMS editing links are hidden. Shared CMS rendering reads existing blocks without provisioning or updating records during impersonation. Pupil-page labels and download names use the target identifiers, and original-claim diagnostics are suppressed.

Audit events record start, exit and blocked changes with the original actor, target identifiers, UTC timestamp and outcome. Existing audit display/export paths show the target identifiers. Impersonated requests and custom events are excluded from establishment analytics; automatic browser beacons are suppressed.

## Implementation choices

- Context interfaces and small records are grouped in the Application impersonation contract file. The scoped Web session service implements both the viewing context and write guard; `ICurrentUserService` remains unchanged.
- The authenticated-ticket binding helper lives in Infrastructure beside the ticket store, rather than Web, so Infrastructure does not depend on Web.
- Existing `SubmittedRequestService` already projects draft details. The new display-only draft route reuses that projection without resuming an editing journey.
- No migration, new secrets or configuration values are required. The existing audit entity, distributed cache, data protection and PostgreSQL locking are reused.
- Readable window routes require selected-establishment data and the existing age-overlap eligibility. Unknown identifiers produce empty/unavailable results; numeric URN parsing does not fall back to original identifiers.
- The endpoint inventory is deliberately explicit. New read endpoints require review before they become available during impersonation.

## Verification

- Complete unit suite: 6,574 passed.
- Focused HTTP, actual Razor rendering, PostgreSQL ownership, lock and audit checks passed. These cover role combinations, cross-tab exit, separate sessions, expired session data, re-login, anti-forgery, stale forms, encoded identifiers, hidden editing, original authority and deterministic concurrent transition/write behaviour.
- Complete integration suite: 1,025 passed. One preceding run failed the existing search p95 latency threshold while other checks were running; the complete rerun passed.
- Web build passed.
- Final focused regression checks cover the no-session static-asset guard, pupil-page labels/downloads and diagnostic behaviour.
- Patch whitespace check passed.
- Browser inspection was attempted; the browser inventory returned no available browsers. Automated Razor tests render the actual shared layout and banner, but do not replace visual, keyboard or authenticated application browser acceptance checks.
- A broad directory scan reported findings in ignored prior scan output and generated Playwright dependencies. The repository's staged-change scanner passed with no leaks found; no scanning rules or ignore files were changed.

## File inventory

The inventory below records the actual files used in place of the proposed filenames in the plan.

- `src/DfE.CheckPerformanceData.Application/AmendmentRequests/AmendmentRequestsService.cs`
- `src/DfE.CheckPerformanceData.Application/AmendmentRequests/BulkSubmissionService.cs`
- `src/DfE.CheckPerformanceData.Application/AmendmentRequests/SubmittedRequestService.cs`
- `src/DfE.CheckPerformanceData.Application/AmendmentRequests/UrnAmendmentRequestsService.cs`
- `src/DfE.CheckPerformanceData.Application/Audit/AuditActivities.cs`
- `src/DfE.CheckPerformanceData.Application/Audit/IImpersonationAuditWriter.cs`
- `src/DfE.CheckPerformanceData.Application/CheckYourPupilData/CheckYourPupilDataService.cs`
- `src/DfE.CheckPerformanceData.Application/Impersonation/IEstablishmentViewContext.cs`
- `src/DfE.CheckPerformanceData.Application/Impersonation/IImpersonationWriteGuard.cs`
- `src/DfE.CheckPerformanceData.Application/LandingPage/LandingPageService.cs`
- `src/DfE.CheckPerformanceData.Application/RequestSubmission/RequestService.cs`
- `src/DfE.CheckPerformanceData.Infrastructure/Analytics/DfeAnalyticsService.cs`
- `src/DfE.CheckPerformanceData.Infrastructure/Authentication/AuthenticatedSessionBinding.cs`
- `src/DfE.CheckPerformanceData.Infrastructure/Authentication/DistributedCacheTicketStore.cs`
- `src/DfE.CheckPerformanceData.Infrastructure/Impersonation/DistributedImpersonationSessionStore.cs`
- `src/DfE.CheckPerformanceData.Persistence/Locking/PostgresImpersonationSessionLock.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/AuditLogRepository.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/ImpersonationAuditWriter.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/RequestRepository.cs`
- `src/DfE.CheckPerformanceData.Web/Admin/Nav/ImpersonationNavEntry.cs`
- `src/DfE.CheckPerformanceData.Web/Admin/RequireAdminSectionAttribute.cs`
- `src/DfE.CheckPerformanceData.Web/Analytics/AnalyticsRequestFilter.cs`
- `src/DfE.CheckPerformanceData.Web/Controllers/AdminController.cs`
- `src/DfE.CheckPerformanceData.Web/Controllers/AuditLog/AuditLogViewModels.cs`
- `src/DfE.CheckPerformanceData.Web/Controllers/CheckYourPupilData/CheckYourPupilDataController.cs`
- `src/DfE.CheckPerformanceData.Web/Controllers/ImpersonationController.cs`
- `src/DfE.CheckPerformanceData.Web/Controllers/SubmittedRequest/SubmittedRequestController.cs`
- `src/DfE.CheckPerformanceData.Web/Diagnostics/DiagnosticFooterMiddleware.cs`
- `src/DfE.CheckPerformanceData.Web/Extensions/AdminNavServiceCollectionExtensions.cs`
- `src/DfE.CheckPerformanceData.Web/Impersonation/ImpersonationAccessPolicy.cs`
- `src/DfE.CheckPerformanceData.Web/Impersonation/ImpersonationEndpointPolicy.cs`
- `src/DfE.CheckPerformanceData.Web/Impersonation/ImpersonationSessionService.cs`
- `src/DfE.CheckPerformanceData.Web/Middleware/ImpersonationContextMiddleware.cs`
- `src/DfE.CheckPerformanceData.Web/Startup/ImpersonationExtensions.cs`
- `src/DfE.CheckPerformanceData.Web/Startup/JourneyAndCmsServicesExtensions.cs`
- `src/DfE.CheckPerformanceData.Web/Startup/RequestPipelineExtensions.cs`
- `src/DfE.CheckPerformanceData.Web/TagHelpers/EstablishmentContextFormTagHelper.cs`
- `src/DfE.CheckPerformanceData.Web/ViewComponents/EditableContentViewComponent.cs`
- `src/DfE.CheckPerformanceData.Web/ViewComponents/EditableTitleViewComponent.cs`
- `src/DfE.CheckPerformanceData.Web/Views/AmendmentRequests/Index.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/CheckYourPupilData/Index.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Impersonation/Index.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Impersonation/ReadOnly.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Impersonation/Unavailable.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/Components/EditableContent/Default.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/Components/EditableTitle/Default.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/_AdminLayout.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/_EstablishmentContextScript.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/_ImpersonationBanner.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/_Layout.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/Shared/_ShareLayout.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/SubmittedRequest/View.cshtml`
- `src/DfE.CheckPerformanceData.Web/Views/SubmittedRequest/ViewConfirmation.cshtml`
- `src/DfE.CheckPerformanceData.Web/wwwroot/css/site.css`
- `src/DfE.CheckPerformanceData.Web/wwwroot/js/analytics-events.js`
- `src/DfE.CheckPerformanceData.Web/wwwroot/js/establishment-context.js`
- `tests/DfE.CheckPerformanceData.IntegrationTests/Impersonation/ImpersonationAuditTests.cs`
- `tests/DfE.CheckPerformanceData.IntegrationTests/Impersonation/ImpersonationLockTests.cs`
- `tests/DfE.CheckPerformanceData.IntegrationTests/Impersonation/ImpersonationSessionTests.cs`
- `tests/DfE.CheckPerformanceData.IntegrationTests/RequestSubmission/RequestRepositoryUpsertTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/AmendmentRequests/AmendmentRequestsIndexViewSourceTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Analytics/AnalyticsRequestFilterTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Impersonation/EstablishmentViewContextTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Infrastructure/Analytics/DfeAnalyticsServiceTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/LandingPage/LandingPageServiceTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/AdminNavRegistryGroupingTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Web/Admin/AdminNavRegistryTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Web/CheckYourPupilDataControllerAnalyticsTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Web/CheckYourPupilDataViewRenderTests.cs`
