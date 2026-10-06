# E2E Suite Robustness & Speed

Findings and fixes for the Playwright/xUnit suite in
`tests/DfE.CheckPerformanceData.E2ETests/`. The suite runs against a deployed
review app in CI (`.github/workflows/build-and-deploy.yml`, e2e job) and
against `docker compose up` locally. This document captures *why* the suite wins
and loses its raw time, the flakiness root causes, and where the fixes landed.

## Suite shape

- ~237 tests, all in the single `[Collection("E2E")]` → fully serial execution.
  One shared `PlaywrightFixture` (readiness probe, impersonation, sample-page
  seed) lives for the whole collection.
- Breakdown: ~148 `[Fact]`, ~84 `[RetryFact]`, ~5 `[Theory]/[RetryTheory]`
  (all `[RetryFact(3)]`/`[RetryTheory(3)]` today).
- 21 files reference the xRetry attributes.
- Host budget (README): full suite must complete within 5 min wall-clock on
  `ubuntu-latest`.

Serial execution + the shared fixture + 30-second default Playwright timeouts
mean any single flaky test costs the whole run: its own time, then (today) two
retries at 3x, on a single thread.

## Root causes of flakiness / slow runtime

1. **Sample-page seed failures at fixture start are swallowed.**
   `PlaywrightFixture.EnsureSamplePagesSeededAsync` wraps the POST in a
   `try/catch (HttpRequestException) { }` (PlaywrightFixture.cs:62-70). Of course
   transport errors are already surfaced by the readiness probe — but a
   *non-transport* failure (e.g. an antiforgery scrape that succeeded and a page
   seed POST returning 500/redirect-to-login) goes unobserved, and the first test
   that depends on a sample page fails later with a specific-missing-row error
   that is much harder to diagnose.
2. **The antiforgery token scrape is one-shot.** `AntiforgeryHelpers.ScrapeAsync`
   does a single GET + parse, then `EnsureSuccessStatusCode`. Immediately after
   deployment readiness the app can take a moment to be truly request-hardened;
   a one-off 500 on this GET fails the whole fixture.
3. **Retries are hard-coded to 3.** `[RetryFact(3)]`/`[RetryTheory(3)]`
   triple every genuine failure's wall-clock time. Worse, for the
   *non-idempotent* journeys (submit enquiry → ChangeRequests row; conflict
   tests; stateful admin flows) a retry re-runs the POST and can move the system
   under test — turning a deterministic failure into an intermittent one and
   hiding the original error behind a second, different failure.
4. **Default 30s Playwright timeouts vs a cold review pod.** A freshly-deployed
   review app can take tens of seconds to warm up (JIT, feature flag store,
   first EF query). Tests with `Page.GotoAsync`/`Expect` defaulting to 30s either
   fail spuriously or are awaited at 30s × N locators before the app is warm.
5. **Fixed-count poll loops.** Several admin tests poll in fixed iteration
   counts (`for (int i = 0; i < N; i++)`) with fixed sleeps. On a fast run they
   busy-wait beyond the point of readiness; on a slow one they exhaust `N` and
   fail even though the condition was about to become true. Examples:
   `Admin/TestDataAdminTests.cs`, `Admin/SearchAnalyticsDashboardTests.cs`,
   `ContentPages/InstantSearchReportingE2ETests.cs`.
6. **No parallelism.** Everything is one serial collection. The suite is ~all
   HTTP/browser against one shared app instance, so full parallelization isn't
   free, but a second collection for the pure-HTTP tests is.
7. **No CI trace/video.** Playwright tracing is not wired into the harness
   (see README "Debugging a red CI run") — a failed test gives you logs but no
   `show-trace` replay, which makes a red CI run hard to diagnose quickly.

## Fix plan & status

| # | Fix | Landing | Status |
|---|-----|---------|--------|
| P1 | Make sample-page seeding a hard gate: retry the seed POST a short bounded number of times, then throw a descriptive `InvalidOperationException` identifying the seed endpoint. | `Fixtures/PlaywrightFixture.cs` | ✅ |
| P2 | Retry the antiforgery scrape a short bounded number of times before giving up. | `Helpers/AntiforgeryHelpers.cs` | ✅ |
| P3 | Replace hard-coded `[RetryFact(3)]`/`[RetryTheory(3)]` with settings-driven retries. New `CPD_E2E_RETRY_ATTEMPTS` env var (default **1** = no retries) is the single knob for the whole suite's failure budget; retries become the default-OFF exception rather than the norm. | `Retrying/` folder (custom attributes + discoverers reusing xRetry's `RetryTestCase`) + 21 test files | ✅ |
| P4 | Raise default navigation/expect timeouts and warm the app before the first test (e.g. an initial no-op request after the readiness probe). | `Fixtures/SeedingPageTest.cs`, fixture | ✅ |
| P5 | Convert fixed-count poll loops to time-bounded polling helpers. | `ContentPages/InstantSearchReportingE2ETests.cs`, `Admin/OnPageSearchSectionTests.cs` | ✅ |
| P6 | Split a second collection for the pure-HTTP tests so browser tests and HTTP tests overlap. Depends on making `Helpers/TestHttpClients.cs` impersonation-cookie state non-global. | `Fixtures/PlaywrightCollection.cs`, helpers |
| P7 | Wire Playwright tracing into the harness and upload trace artifacts from CI (extend the e2e job's artifact step, currently only `e2e-snapshots`). | harness + `.github/workflows/build-and-deploy.yml` |

Anything not checked in the Status column is still planned.