# CheckPerformanceData E2E Tests

End-to-end Playwright + xUnit suite that drives a real Chromium browser against a deployed Web app.

## Quick start

```bash
cd check-performance-data
docker compose up -d                      # starts Web + Postgres on http://localhost:8080
pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium  # one-time
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release
```

On Windows without `pwsh` (PowerShell Core), the install script runs via Windows PowerShell:

```powershell
powershell.exe -ExecutionPolicy Bypass -File tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium
```

## Configuration

| Env var | Default | Purpose |
|---------|---------|---------|
| `CPD_E2E_BASE_URL` | `http://localhost:8080` | URL the harness drives. |
| `CPD_E2E_READY_TIMEOUT_SECONDS` | `90` | How long the fixture polls `/healthcheck` before giving up. |

## Test categories

| Trait | Filter | Use |
|-------|--------|-----|
| `W0` | `--filter "Category=W0"` | Harness smoke. |
| `W1` | `--filter "Category=W1"` | Read-path browse. |
| `W2` | `--filter "Category=W2"` | Soft-delete + warning-text + search sidebar. |
| `W4` | `--filter "Category=W4"` | REST CRUD + visual regression. |
| `VisualRegression` | `--filter "Category=VisualRegression"` | Snapshot-diff tests only — Linux-only, skipped on Windows/macOS. |

Quick functional sweep (excludes visual regression):

```bash
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release --filter "Category!=VisualRegression"
```

## Visual regression

Visual regression tests live under `Visual/` and capture full-page Chromium screenshots which are pixel-diffed against committed `.png` artefacts under `Snapshots/linux-chromium/`. The diff helper is `IPage.MatchSnapshotAsync(name, maxDiffPixelRatio: 0.005)` in `Helpers/PageSnapshotExtensions.cs` — it uses `SixLabors.ImageSharp` for the per-pixel comparison with a small per-channel tolerance so anti-aliasing jitter does not cause false positives below the 0.5% diff threshold.

Snapshots are **Linux-only**. Each test calls `Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), ...)` so on Windows/macOS the tests skip cleanly. Linux-Chromium and Windows-Chromium produce different rendered pixels — never commit a snapshot generated locally on Windows or macOS. The CI Linux runner is the canonical source.

### Update workflow

To intentionally regenerate a snapshot:

1. Make the code change locally.
2. Delete the snapshot you want to refresh: `rm tests/DfE.CheckPerformanceData.E2ETests/Snapshots/linux-chromium/{name}.png`
3. Push to a branch labelled `deploy` so the CI `e2e:` job runs on Linux.
4. **First CI run:** the visual-regression test fails with `Snapshot {name} did not exist — written, run again to verify.` The helper has just written the new `.png` to disk in the test runner's working tree.
5. Download the `e2e-snapshots` artefact from the failed CI run's Artifacts panel; the regenerated PNG is under `linux-chromium/{name}.png`. Commit it back into `Snapshots/linux-chromium/`.
6. Push again — **second CI run:** the comparison passes, the PR file diff surfaces the regenerated `.png` for review.

No env var, no `--update-snapshots` flag plumbing — delete the file and run twice.

## Debugging a red CI run

On failure the `e2e:` job uploads the `e2e-snapshots` artefact (the entire `Snapshots/` tree, 14-day retention). For visual-regression failures this lets you inspect the divergent PNG directly. For non-visual failures the primary debugging signal is the test output in the failed run's logs — Playwright tracing is not currently wired into the harness.

To enable trace replay (`playwright show-trace`) for a specific failing test, hook `Context.Tracing.StartAsync` / `StopAsync` around the test body locally, reproduce against `docker compose up`, and inspect the resulting `.zip` with:

```bash
pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 show-trace <path-to-trace.zip>
```

## Project boundaries

- **Black-box harness only** — no `WebApplicationFactory`, no in-process Postgres, no `TestAuthenticationHandler`.
- **Anonymous-only test corpus** — seeds via direct HTTP POST against the controller surface; auth-gated tests are out of scope for this iteration.
- **Test data isolation** — every wiki page / content block created uses an `e2e-{Guid:N}-` prefix. Cleanup is `IAsyncLifetime.DisposeAsync` per class; content-block leak is accepted (no DELETE route) but harmless via the UUID prefix.
- **5-minute runtime budget** — the full suite must complete within 5 min wall-clock on `ubuntu-latest` (2-4 vCPU). If a single test exceeds 30s, investigate (usually a `WaitForLoadState.NetworkIdle` waiting on an unrelated background fetch).

## Layout

```
tests/DfE.CheckPerformanceData.E2ETests/
├── Fixtures/
│   ├── PlaywrightFixture.cs         # IAsyncLifetime; readiness probe; antiforgery scrape; seed HttpClient
│   └── PlaywrightCollection.cs      # [CollectionDefinition("E2E")] + ICollectionFixture<>
├── Helpers/
│   ├── SeedHelpers.cs               # SeedWikiPageAsync / SeedContentBlockAsync / SoftDeleteWikiPageAsync
│   ├── AntiforgeryHelpers.cs        # static ScrapeAsync(HttpClient, formPath) -> (Token, Cookie)
│   ├── PageStabilisationExtensions.cs   # IPage.StabiliseAsync() — animations off + fonts.ready + NetworkIdle
│   └── PageSnapshotExtensions.cs    # IPage.MatchSnapshotAsync(name, maxDiffPixelRatio) — pixel diff
├── Wiki/
│   ├── WikiNavigationTests.cs
│   ├── HealthcheckTests.cs
│   ├── ContentBlockRenderTests.cs
│   ├── GovUkAssetsTests.cs
│   ├── SoftDeleteWikiPageTests.cs
│   ├── WarningTextRenderTests.cs
│   └── SearchSidebarBackLinkTests.cs
├── Web/
│   ├── NotFoundTests.cs
│   ├── WikiCrudTests.cs
│   └── ContentBlockCrudTests.cs
├── Visual/
│   └── VisualRegressionTests.cs     # Linux-only via [SkippableFact]
├── Snapshots/
│   └── linux-chromium/              # canonical .png artefacts (CI-generated)
└── HarnessSmokeTests.cs
```

## Adding a new test

1. Pick the appropriate folder (`Wiki/` for browser-driven wiki tests, `Web/` for HTTP-only tests, `Visual/` for snapshots).
2. Inherit `PageTest` for browser tests; omit inheritance for HTTP-only tests.
3. Add `[Collection("E2E")]` and `[Trait("Category", "W{N}")]`.
4. If the test creates wiki pages or content blocks, implement `IAsyncLifetime` with cleanup in `DisposeAsync`.
5. Use the `e2e-{Guid:N}-` prefix on every slug/key.
6. Use `_fixture.SeedClient` + `SeedHelpers.*` for HTTP seeding.
7. Use Playwright `Expect(Locator).ToBeVisibleAsync()` / `ToHaveTextAsync(...)` for browser assertions; use `HttpClient` directly for HTTP assertions.
