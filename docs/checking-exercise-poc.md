# Independent Checking Exercises POC (first attempt, superseded)

## Admin model update (September 2026)

Checking exercises no longer store a separate stage or output data type. Include the stage in the exercise or tab name. The existing exercise type determines the pupil/results output filename; CSV content continues to be validated and transformed using the selected JSON schema.

`20260915175109_RemoveCheckingExerciseStageAndDataType` removes the two columns. The web host applies migrations on startup. Restart the updated application to apply this change. Exercise IDs, names, dates, visibility, replacement relationships and CSV/schema definitions are preserved.

If an exercise previously used a custom output type (different from its exercise type's pupil/results default), the migration clears its validation stamp. Use **Validate data** on its edit page to regenerate output from the existing CSV/schema pairs. The migration does not delete blobs. Default-output exercises retain their validation stamps.

The original POC notes below describe its earlier design.

This document records the first attempt. The retry replaces its separate-Window storage approach. See [current-state trace](checking-exercise-current-state.md) and [normalised implementation](checking-exercise-normalised-poc.md) for the current architecture and demo.

## Problem

Revised imports must not change Provisional data. Visibility, permission to act, and replacement are separate decisions.

## Current behaviour (inspection before implementation)

The solution uses ASP.NET Core MVC, application interfaces, EF Core/PostgreSQL repositories and Azure Blob Storage. CheckingWindow owns CheckingExercises; each exercise owns CSV/schema dataset slots and a validation stamp. A unique index permits one exercise of each type per window. Exercise dates determine available request actions; the outer window dates are derived from those dates by the admin wizard. Existing request entry and submission paths already check exercise dates.

The existing pupil page is `/CheckYourPupilData/{windowId}` (there is no literal `/check-data` route). It groups pupil tables by inclusion, and 16–19 has a single Students tab. Landing cards require an open outer window. Readers use the authenticated school's LAESTAB. Pupil data and results use different output shapes. Ingress validates CSV/schema pairs and combines each exercise's input files into per-school JSON. Its optional clear operation is scoped to window plus exercise type. Provisional, Revised and Retention results currently occupy input slots of one results exercise: that conflicts with independent releases.

## Proposed model and minimum implementation plan

1. Add optional descriptive metadata, an output-type enum, explicit visibility and a nullable self-reference to CheckingExercise, with an additive EF migration.
2. Retain the unique `(WindowId, ExerciseType)` identity and storage layout for this POC. Configure separate Provisional and Revised windows, each containing Students and Results exercises. This keeps all existing readers, caches, request stamps and journeys bound to the correct release without a historic data migration.
3. Add a `/check-data` exercise catalogue with configured tabs and school-scoped data/downloads. Query visibility independently of dates for action availability. Reuse existing request journeys and their server guards.
4. Add a small development-only seed/config lifecycle mechanism, including fallback, using normal EF writes and the existing ingress processor.
5. Test visibility boundaries, ordering, closed reads, action guards, lineage and preservation of Provisional output across Revised ingress; run repository checks.

## Significant decisions and model conflicts

An exercise owns its dataset through its unique window/type storage address. A new stage gets a new window and new exercise IDs; it must never reuse the old window/type pair. Keeping this existing convention is the smallest coherent POC, but does not support repeated exercise types in the same window. Production may instead address blobs directly by exercise ID and permit multiple releases in one parent grouping.

Keep the existing `{WindowGuid}/data/{LAESTAB}_pupils.json` and `{WindowGuid}/results-enquiry/data/{LAESTAB}_results.json` layout. Their containers differ between stages. No ingress-run entity is needed to prove release separation. Re-ingesting the *same* exercise retains existing rewrite behaviour; this POC does not promise atomic publication or immutable versions within an exercise.

The existing model places action dates on the exercise and derives the outer window's bounds. Reuse that scheduling convention, with the outer window also required to be open for POC actions. Do not make visibility depend on either date range. Existing unconfigured exercises keep their legacy page behaviour; explicit tab configuration opts an exercise into `/check-data`.

## Checking Exercise vs Window vs Visibility

Checking Exercise identifies a release and its inputs/output. Window and exercise action dates permit interactive checking. `IsEnabled && (VisibleFrom == null || VisibleFrom <= now) && (VisibleUntil == null || VisibleUntil > now)` controls the catalogue. Dates use the existing local wall-clock/TimeProvider convention.

## Replacement/lineage behaviour

Only `ReplacesCheckingExerciseId` is stored. It is descriptive, has a restrictive foreign key, and does not trigger deletion, copying, visibility changes or ingress. Fallback changes visibility only.

## Lifecycle

```text
Provisional dataset A: visible, window open
    |
    v
Window closes: dataset A remains visible and downloadable
    |
    v
Revised dataset B: visible, window closed, replaces Provisional
Provisional hidden; dataset A untouched
    |
    v
Revised window opens
    |
    v
Fallback: hide Revised, show Provisional; no data rebuild
```

## POC limitations and production work

No historic data migration, new administration UI, storage replacement or ingress-run history. Separate windows per release are a deliberate compatibility constraint. Production needs a decision on release grouping, publication atomicity, concurrent re-ingress, cache invalidation, lifecycle administration, lineage cycle validation, duplicate tab labels, and retention policies. The legacy admin removes deselected exercises; configured POC exercises now become hidden instead, preserving their rows and data.


## Running and demonstrating the POC

Use the existing local PostgreSQL, Azurite and sign-in setup described in the root README. Keep credentials in the existing local secret mechanisms. The web host applies EF migrations on startup. The additive migration is `20260911134852_AddCheckingExerciseCatalogue`; it adds metadata/visibility columns and an indexed restrictive self-reference, with no historic row or blob changes.

In a Development shell, run:

```sh
ASPNETCORE_ENVIRONMENT=Development CheckingExercisePoc__State=ProvisionalOpen dotnet run --no-launch-profile --project src/DfE.CheckPerformanceData.Web
```

Sign in as the existing demo school with LAESTAB `860/4070`, then open `/check-data`. This explicit POC setting **bypasses the legacy destructive development seeder**. Leave it set during every POC restart. Do not use the admin “Reset seed data” action during the demo. No new credentials or configuration files are required.

Stop and restart the same command, changing only `CheckingExercisePoc__State`, in this order:

| State value | Visible release | Checking actions | Expected Students data |
| --- | --- | --- | --- |
| `ProvisionalOpen` | Provisional | Enabled | A, B, C, D |
| `ProvisionalClosed` | Provisional | Disabled | A, B, C, D |
| `RevisedClosed` | Revised | Disabled | A, B, C |
| `RevisedOpen` | Revised | Enabled | A, B, C |
| `Fallback` | Provisional | Disabled (its window remains closed) | A, B, C, D |

Both Students and Results tabs remain available in each state. Each tab names the backing exercise. Downloads work in every visible state. In an open state, “Request a change” enters the existing stage-specific pupil or results journey. Use `ProvisionalOpen` again if fallback should also reopen checking. State changes apply visibility and scheduling only. Inputs and outputs are seeded only when a school's output is absent; an existing output is not rewritten by restarting or changing state.

Provisional uses window `10000000-0000-4000-8000-000000000001`; Revised uses `10000000-0000-4000-8000-000000000002`. Inspect these containers in the existing Storage Admin browser to verify that both datasets remain present. Exercise IDs are assigned once and retained; Revised points to its corresponding Provisional exercise. The POC seed uploads a CSV/schema pair named by exercise ID and invokes the existing ingress processor with that persisted exercise's unique window/type address. Student files combine into `_pupils.json`; results use `_results.json` with qualification/grade fields and a source tag, without pupil inclusion flags.

## `/check-data` behaviour and limits

The catalogue queries all explicitly configured, enabled exercises within their visibility dates, ordered by `TabOrder` then ID. It does not require an open Window. The page loads only the authenticated school's output and renders configured tab names. Unknown/hidden IDs cannot be downloaded or used to start an action through this route. Missing school files omit that exercise from the page. A closed exercise renders the data/download but no request form; the POST repeats the guard and returns 403. Existing request journeys also enforce the POC visibility and outer-window dates on their normal exercise guard.

The POC intentionally displays a generic table of supplier columns and downloads JSON. It does not add polished column labels, pagination, CSV export for Results, or new request workflows. Existing pupil CSV downloads remain on the legacy page. Only Pupil and Results output/read behaviours are demonstrated; the other enum values reserve descriptive categories, not implemented importers. One window per stage and one exercise per type within that window are required. Results and Students should be released together because existing result journeys search the Students dataset in that stage's window. Existing request/session/cache keys still use window identity.

## Validation

`CheckingDataPocTests` in UnitTests checks visibility date boundaries, view/download availability while closed, open/closed action POSTs, unknown downloads, and legacy-journey guards. The PostgreSQL/Azurite integration test applies the actual migrations and seed through all five states, verifies ordering and visibility query translation, verifies results output shape, checks lineage, and proves that Revised re-ingress with `clearExistingFiles: true` leaves Provisional bytes untouched. It also verifies that deselecting a configured exercise preserves the stored release.

```sh
dotnet test tests/DfE.CheckPerformanceData.UnitTests/DfE.CheckPerformanceData.Application.UnitTests.csproj --filter FullyQualifiedName~CheckingDataPocTests
dotnet test tests/DfE.CheckPerformanceData.IntegrationTests/DfE.CheckPerformanceData.IntegrationTests.csproj --filter FullyQualifiedName~CheckingDataPocTests
```

The integration test uses disposable PostgreSQL and Azurite Testcontainers; Docker must be available.

Verified on this branch: Release solution build passed (117 warnings, no errors); all 5,160 unit tests and 789 integration tests passed. The changed C# files pass `dotnet format whitespace --verify-no-changes`; `git diff --check` passes. Gitleaks scans of changed files and staged changes pass (nothing is staged). Browser/E2E tests were excluded using the repository CI filter; no interactive browser demo was run.

## Changed files

- `src/DfE.CheckPerformanceData.Application/WindowManagement/CheckingDataCatalogue.cs`
- `src/DfE.CheckPerformanceData.Application/WindowManagement/ICheckingDataReader.cs`
- `src/DfE.CheckPerformanceData.Domain/Enums/CheckingDataType.cs`
- `src/DfE.CheckPerformanceData.Infrastructure/BlobStorage/CheckingDataReader.cs`
- `src/DfE.CheckPerformanceData.Persistence/Migrations/20260911134852_AddCheckingExerciseCatalogue.Designer.cs`
- `src/DfE.CheckPerformanceData.Persistence/Migrations/20260911134852_AddCheckingExerciseCatalogue.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/CheckingDataCatalogue.cs`
- `src/DfE.CheckPerformanceData.Web/Controllers/CheckingDataController.cs`
- `src/DfE.CheckPerformanceData.Web/Seeding/CheckingExercisePocSeed.cs`
- `src/DfE.CheckPerformanceData.Web/Views/CheckingData/Index.cshtml`
- `tests/DfE.CheckPerformanceData.IntegrationTests/Persistence/CheckingDataPocTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/WindowManagement/CheckingDataPocTests.cs`
- `src/DfE.CheckPerformanceData.Application/WindowManagement/ICheckingExerciseService.cs`
- `src/DfE.CheckPerformanceData.Application/WindowManagement/IWindowService.cs`
- `src/DfE.CheckPerformanceData.Persistence/DependencyManager.cs`
- `src/DfE.CheckPerformanceData.Persistence/Entities/CheckingExercise.cs`
- `src/DfE.CheckPerformanceData.Persistence/Migrations/PortalDbContextModelSnapshot.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/CheckYourPupilDataRepository.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/LandingPageRepository.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/WindowRepository.cs`
- `src/DfE.CheckPerformanceData.Web/Startup/BlobStorageExtensions.cs`
- `src/DfE.CheckPerformanceData.Web/Startup/StartupTasksExtensions.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/Persistence/CheckingExerciseMappingTests.cs`
