# Playwright E2E suite

End-to-end tests for the Check Performance Data web app. Drives a real Chromium browser against a deployed instance — no in-process server, no mocked data layer. Lives at `tests/DfE.CheckPerformanceData.E2ETests/`.

This doc explains the *shape* of the suite: what it tests, the design choices behind it, and the gotchas that drove those choices. The test project's own [README](../tests/DfE.CheckPerformanceData.E2ETests/README.md) is the reference for *how to run it* (commands, env vars, snapshot update workflow). Read this first to orient, then the README when you need the dial settings.

## Versions

All E2E-facing versions in one place. Source of truth for NuGet pins is `src/Directory.Packages.props` (central package management, `ManagePackageVersionsCentrally=true`); the test project's `.csproj` only lists the package IDs.

### Required on the host (for `make test-e2e-fast` / native runs)

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0.x | Test project targets `net10.0`. |
| PowerShell (pwsh or Windows PowerShell) | 5.1+ / Core 7+ | Only needed once, to run `playwright.ps1 install chromium`. |
| Docker | any recent | Required for `make test-e2e` (canonical container path) and for the compose stack the host run hits. |

### Required for the container path (`make test-e2e`)

| Tool | Version | Notes |
|------|---------|-------|
| Docker + Compose v2 | any recent | Compose profile `e2e` builds and runs the test image. |
| Image: `mcr.microsoft.com/playwright/dotnet` | `v1.59.0-noble` | Pinned in `tests/DfE.CheckPerformanceData.E2ETests/Dockerfile`. **Must move in lockstep with the `Microsoft.Playwright` NuGet version.** Drift detection: `bash scripts/check-playwright-pin.sh`. |
| .NET SDK (inside image) | 10.0.x | Side-installed via `dotnet-install.sh` because the upstream Playwright image ships .NET 8 only as of v1.59.0. |

### NuGet packages

Pinned in `src/Directory.Packages.props`; the E2E `.csproj` references them by ID.

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Playwright` | 1.59.0 | Browser automation. **Must match the Docker image tag `v1.59.0-noble`** — Chromium binary protocol mismatch otherwise. |
| `Microsoft.Playwright.Xunit` | 1.59.0 | xUnit `PageTest` base class + browser/page lifecycle. |
| `xunit` | 2.9.3 | Test framework. |
| `Xunit.SkippableFact` | 1.5.23 | `Skip.IfNot(...)` for Linux-only visual regression tests. |
| `xunit.runner.visualstudio` | 3.1.5 | VS / `dotnet test` runner. |
| `Microsoft.NET.Test.Sdk` | 17.14.0 | Test SDK. |
| `SixLabors.ImageSharp` | 3.1.12 | Pure-managed image lib used by the snapshot diff helper for per-pixel comparison and red-tint diff PNG generation. |
| `xRetry` | 1.9.0 | `[RetryFact]` for the rare flaky-by-design test. |
| `coverlet.collector` | 6.0.4 | Coverage. |

### Runtime stack the suite drives

What the tests connect *to*, not what they're built with. Pinned in `docker-compose.yaml`.

| Component | Image | Version |
|-----------|-------|---------|
| Web app | `cypd_web:latest` (built locally from `src/DfE.CheckPerformanceData.Web/Dockerfile`) | matches branch |
| Postgres | `postgres` | `18.1-alpine` |
| Azure storage emulator | `mcr.microsoft.com/azure-storage/azurite` | `latest` |

## Container map

The compose file at the repo root wires up everything the suite needs. Profiles control which services start; the E2E flow uses the `e2e` profile, which is intentionally narrower than `all`.

```
┌─────────────────────────────────────────────────────────────────┐
│ docker network: cypd (bridge)                                   │
│                                                                 │
│  ┌──────────────────┐         ┌──────────────────┐              │
│  │ cypd_e2e_tests   │ HTTP    │ cypd_web         │              │
│  │ (profile: e2e)   ├────────►│ (profile: e2e)   │              │
│  │                  │  :8080  │                  │              │
│  │ playwright/      │         │ ASP.NET Core 10  │              │
│  │ dotnet:1.59.0    │         │ Razor + GDS      │              │
│  │ + .NET 10 SDK    │         └────────┬─────────┘              │
│  │                  │                  │                        │
│  │ runs:            │                  │ EF Core 10             │
│  │  dotnet test     │                  ▼                        │
│  └──────────────────┘         ┌──────────────────┐              │
│                               │ cypd_db          │              │
│                               │ (profile: e2e)   │              │
│                               │ postgres:18.1    │              │
│                               └──────────────────┘              │
│                                        ▲                        │
│                                        │ (web + rules-engine    │
│                                        │  share the DB)         │
│                               ┌────────┴─────────┐              │
│                               │ cypd_azurite     │              │
│                               │ (profile: e2e)   │              │
│                               │ azurite:latest   │              │
│                               │ blob :10000      │              │
│                               │ queue:10001      │              │
│                               └──────────────────┘              │
└─────────────────────────────────────────────────────────────────┘

Other containers in compose, NOT started by the e2e profile:
  cypd_rules_engine     profile: rules_engine, all
  cypd_pgadmin          profile: database, all   (browser DB UI on :5050)
```

### What each container does in the E2E flow

| Container | Image | Role in E2E |
|-----------|-------|-------------|
| `cypd_web` | `cypd_web:latest` (built from `src/DfE.CheckPerformanceData.Web/Dockerfile`) | The system under test. Listens on `:8080`. The PlaywrightFixture polls `/healthcheck` here for readiness before any test runs. |
| `cypd_db` | `postgres:18.1-alpine` | Real Postgres on `:5432`. Web app's `ConnectionStrings__Postgres` points here. Persistent volume `postgres_data`. |
| `cypd_azurite` | `mcr.microsoft.com/azure-storage/azurite:latest` | Azure Blob + Queue emulator on `:10000` / `:10001`. Web app's `ConnectionStrings__AzureStorage` points here. Persistent volume `azurite_data`. |
| `cypd_e2e_tests` | `cypd_e2e_tests:latest` (built from `tests/DfE.CheckPerformanceData.E2ETests/Dockerfile`) | The runner. Mounts the repo at `/work`, restores into the named volume `cpd-nuget-cache`, executes `dotnet test`. Chromium ships pre-installed in the upstream Playwright base image. |

### Volumes

| Volume | Purpose | Persists across runs |
|--------|---------|----------------------|
| `postgres_data` | Postgres data dir | yes |
| `azurite_data` | Azurite blob/queue state | yes |
| `cpd-nuget-cache` | NuGet package cache for the test runner. First run is ~3-5 min cold; subsequent runs are seconds-to-output because this volume survives `--rm`. | yes |
| `pgadmin_data` | Only present when `pgadmin` profile is up. Not used by E2E. | yes |

### Readiness gating

`depends_on` in compose is the default `service_started` condition — that means "the container started", not "the app is up". The authoritative readiness gate is `PlaywrightFixture.WaitForDeploymentReadyAsync`, which polls `${CPD_E2E_BASE_URL}/healthcheck` every 2s for up to 90s (configurable via `CPD_E2E_READY_TIMEOUT_SECONDS`). The web service has no compose-level healthcheck today; if you add one, the fixture's poll becomes redundant but harmless.

## Running the tests

Three host-side flows: PowerShell (no IDE), VS Code, Visual Studio. They differ in *how you trigger the run*; the underlying `dotnet test` invocation is the same and the harness contract is identical.

Before any of them, the compose stack (web + db + azurite) must be reachable on `http://localhost:8080` — that's what `PlaywrightFixture` polls for readiness. The two exceptions are `make test-e2e` (which runs *inside* the compose network and uses `http://web:8080`) and any flow where you've set `CPD_E2E_BASE_URL` to point at a different deployment.

> **Solution file note:** the .NET solution is at `src/DfE.CheckPerformanceData.slnx`, not at repo root. Open that one in IDEs.

### PowerShell

Run from the repo root (`check-performance-data\`). Native host SDK, no Docker for the test runner itself.

```powershell
# 1. One-time: bring up the app stack (web + db + azurite).
docker compose --profile e2e up -d web db azurite

# 2. One-time per machine: install Chromium for Playwright.
#    Build first so playwright.ps1 exists.
dotnet build tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release
powershell.exe -ExecutionPolicy Bypass `
  -File tests\DfE.CheckPerformanceData.E2ETests\bin\Release\net10.0\playwright.ps1 `
  install chromium

# 3. Run the suite. Skip visual regression on Windows — baselines are Linux-only.
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ `
  --configuration Release `
  --filter "Category!=VisualRegression"
```

Useful filter variants:

```powershell
# Single category
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "Category=W2"

# Single test by name
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "FullyQualifiedName~SoftDeleteWikiPageTests"

# Including visual regression (only meaningful on Linux or via make test-e2e)
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release
```

To run the canonical Linux-baseline path from PowerShell — same as `make test-e2e`:

```powershell
docker compose --profile e2e run --rm e2e-tests
```

That builds the runner image (first time only), brings up dependencies, executes the full suite *including* visual regression against the Linux-Chromium baselines, and tears down.

If you alternated between native and container runs and hit `NETSDK1047 'project.assets.json' doesn't have a target for 'net10.0/win-x64'`:

```powershell
Remove-Item -Recurse -Force tests\DfE.CheckPerformanceData.E2ETests\bin, tests\DfE.CheckPerformanceData.E2ETests\obj
```

### VS Code

Requires the **C# Dev Kit** extension (`ms-dotnettools.csdevkit`) — it bundles Test Explorer integration for `dotnet test`. The base **C#** extension alone (`ms-dotnettools.csharp`) gives you OmniSharp + IntelliSense but no Test Explorer.

1. Open the repo folder in VS Code. The Solution Explorer panel (added by C# Dev Kit) will detect `src/DfE.CheckPerformanceData.slnx`.
2. Bring the stack up in the integrated terminal:
   ```powershell
   docker compose --profile e2e up -d web db azurite
   ```
3. One-time Chromium install (same as the PowerShell flow above).
4. Open the **Testing** view (flask icon in the sidebar). The `DfE.CheckPerformanceData.E2ETests` tree appears. Click ▶ next to a test, class, or the whole project to run.
5. To filter by category, run from the integrated terminal instead — Test Explorer doesn't expose `[Trait]` filtering directly:
   ```powershell
   dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "Category!=VisualRegression"
   ```

**Debug a single test:** right-click the test in Test Explorer → "Debug Test". Set breakpoints in the test method or in production code; the debugger will attach to the test runner process. Note: it does *not* attach to the web app under test — that's still the `cypd_web` container. To debug into the web app code, attach a separate debugger to the container or run the web app directly from VS (see below).

### Visual Studio (2022 17.12+ or 2026)

VS speaks `.slnx` natively in 17.12+ (LTSC) / 2026.

1. Open `src/DfE.CheckPerformanceData.slnx`. VS restores packages.
2. Bring the stack up. Easiest from the **Developer PowerShell** inside VS (View → Terminal):
   ```powershell
   docker compose --profile e2e up -d web db azurite
   ```
   Or use the **Containers** tool window (View → Other Windows → Containers) to start individual services.
3. **Test Explorer** (Test menu → Test Explorer or `Ctrl+E, T`) lists every test grouped by project / namespace / class. Right-click a node → "Run" or "Debug".
4. To exclude visual regression by default on Windows, use the search box at the top of Test Explorer:
   ```
   -Trait:"VisualRegression"
   ```
   (the `-` prefix excludes; this is a VS Test Explorer filter syntax, not a `dotnet test` filter.)
5. **One-time Chromium install** is still required — VS doesn't run `playwright.ps1 install` for you. Build the test project once (Build → Build Selection on the E2ETests project), then in Developer PowerShell:
   ```powershell
   powershell.exe -ExecutionPolicy Bypass `
     -File tests\DfE.CheckPerformanceData.E2ETests\bin\Release\net10.0\playwright.ps1 `
     install chromium
   ```
   (Or `bin\Debug\net10.0\` if you've only built Debug.)

**Debug a single test:** right-click in Test Explorer → "Debug". VS attaches to the test process. As with VS Code, this debugs the *test code*, not the web app — the web app under test is still the container. To step through Razor + controller code while a test drives it, set the `Web` project as startup, F5 to launch it locally on `:8080` (Postgres + Azurite still need to be running), then run the test against that local instance instead of the container.

### Choosing which flow

| You want to… | Use |
|--------------|-----|
| Tight inner loop, functional tests only | PowerShell or VS/VSC Test Explorer with `Category!=VisualRegression` |
| Validate visual regression before push / PR | `make test-e2e` (or `docker compose --profile e2e run --rm e2e-tests` from PowerShell) |
| Step through a single failing test in a debugger | VS Code or VS, "Debug Test" |
| Reproduce a CI failure exactly | `make test-e2e` — same image, same Linux-Chromium, same baseline |

## What it tests

Three layers, all behind the same fixture:

- **Browser-driven UI tests** (`Wiki/`, `Web/`) — Playwright loads pages, clicks things, asserts visible state. Covers the help CMS read path, search sidebar, soft-delete flow, warning-text rendering, GOV.UK assets, the wiki/content-block CRUD round trips, and the 404 surface.
- **HTTP-only tests** (`Web/`) — `HttpClient` without a browser. Faster, used where the assertion is "controller redirected to X" or "endpoint returned status Y" and the rendered page isn't the point.
- **Visual regression** (`Visual/`) — full-page Chromium screenshots pixel-diffed against committed PNG baselines under `Snapshots/linux-chromium/`. Linux-only (see below).

Smoke tests (`HarnessSmokeTests.cs`) prove the harness itself can reach the deployed app and scrape an antiforgery token before any real test runs.

## Why a black-box harness, not WebApplicationFactory

The suite drives the *deployed* app over HTTP — `docker compose up` brings up Web + Postgres + Azurite, and the tests hit `http://localhost:8080`. No `WebApplicationFactory`, no in-process Postgres, no `TestAuthenticationHandler`. That choice is deliberate.

- **Realistic auth + cookies + CSRF.** Antiforgery tokens come from the real Razor-rendered form (scraped via `PlaywrightFixture.ScrapeAntiforgeryTokenAsync`); cookies flow through the real ASP.NET pipeline. An in-process harness would short-circuit half the middleware and miss configuration drift.
- **Stack matches production.** Postgres is the real `postgres:18.1-alpine` image, not a SQLite shim. EF query translation, Postgres-specific operators (FTS, etc.), and migrations are exercised end to end.
- **Same artefact across local + CI.** The container path (`make test-e2e`) builds the test runner against the same compose stack CI uses. No "works on my machine but breaks in CI" failure mode for harness setup.

The trade-off is speed: cold start (compose up + readiness probe + first Playwright launch) is ~30-40s. We accept that. Tight inner-loop runs use `make test-e2e-fast` natively on the host, skipping visual regression.

## Test categories (`W0`-`W4`)

Every test is tagged with a `[Trait("Category", "W{N}")]`. The labels come from the original work-package breakdown but the practical use is `dotnet test --filter`:

| Trait | Scope |
|-------|-------|
| `W0` | Harness smoke (deployment ready, antiforgery scrape) |
| `W1` | Read-path browse |
| `W2` | Soft-delete, warning text, search sidebar |
| `W4` | REST CRUD + visual regression |
| `VisualRegression` | Cross-cutting trait on snapshot tests, Linux-only |

`Category!=VisualRegression` is the most-used filter — it gives you the full functional sweep on any host without needing the Linux container for pixel-stable rendering.

## Visual regression — homegrown, not Playwright's

Playwright ships its own `ToHaveScreenshotAsync()` in newer versions, but we don't use it. The C# binding's snapshot story is patchier than the Node story, and we want full control over the diff format. So `Helpers/PageSnapshotExtensions.cs` is hand-rolled:

- **Comparison** uses `SixLabors.ImageSharp` per-pixel with a per-channel tolerance of 3 (anti-aliasing jitter is below this; real visual changes are above it). `maxDiffPixelRatio` defaults to 0.005 (0.5% of pixels allowed to differ).
- **First run writes the baseline.** Delete a snapshot PNG, run the test once — it writes the file and throws "did not exist — written, run again to verify". Run again — the comparison passes. No `--update-snapshots` flag, no env var to forget, no `update_snapshots: missing` config to misread.
- **Multi-viewport bootstrapping.** Tests that capture several snapshots (e.g. desktop + tablet + mobile) pass an accumulator into `MatchSnapshotAsync`. First run writes *all* viewport baselines in a single test invocation instead of failing on viewport 1 and never reaching viewports 2 and 3. The test fails at the end if anything was bootstrapped, so CI still gates on "you regenerated something".
- **Diff PNG trio on failure.** When a comparison exceeds the threshold, the helper writes three PNGs to `Snapshots/diffs/`: `{name}.expected.png` (the committed baseline), `{name}.actual.png` (what we just captured), and `{name}.diff.png` (the actual frame with diverging pixels tinted red). The `XunitException` message includes the absolute path so failure logs point straight at the artefacts.
- **`Snapshots/diffs/` is gitignored.** It's a failure surface, not a source artefact. CI uploads the entire `Snapshots/` tree on failure as the `e2e-snapshots` artefact (14-day retention) so the trio surfaces in the run's Artifacts panel automatically.

### Linux-only baselines

Linux-Chromium and Windows/macOS-Chromium produce different pixels for the same page — text shaping, sub-pixel positioning, and font hinting all diverge. So `Snapshots/linux-chromium/` is the **single canonical baseline**, and snapshot tests guard themselves with:

```csharp
Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Linux-only snapshot");
```

(via `Xunit.SkippableFact`). On Windows or macOS the visual tests skip cleanly. **Never commit a snapshot generated locally on Windows or macOS** — it'll be wrong on the CI Linux runner the moment another visual test triggers.

The container path (`make test-e2e`) gives every dev a way to regenerate snapshots that *match* the CI Linux baseline without needing a Linux box. The CI path (push to a `deploy`-labelled branch) is the fallback for devs without Docker.

## Page stabilisation

`Helpers/PageStabilisationExtensions.cs` exposes `IPage.StabiliseAsync()` — three things, in order:

1. Inject CSS that disables every animation and transition (`* { animation: none !important; transition: none !important; }`). GOV.UK frontend has subtle hover/focus transitions that produce intermittent diff noise without this.
2. `await document.fonts.ready` — fonts load asynchronously, and a screenshot taken before fonts are ready captures fallback metrics that diverge once the real fonts arrive.
3. `WaitForLoadStateAsync(NetworkIdle)` — last-line defence for late-arriving images or background fetches.

Visual tests call this before every screenshot. Functional tests call it when assertions depend on a settled DOM.

## The container test runner

`make test-e2e` is the canonical path. It runs the suite inside a thin .NET 10 overlay on top of the official `mcr.microsoft.com/playwright/dotnet:v1.59.0-noble` image, against the compose stack:

- The overlay (`tests/DfE.CheckPerformanceData.E2ETests/Dockerfile`) layers .NET 10 SDK on top of the Playwright base image, which already has Chromium + system deps installed. No `pwsh playwright.ps1 install` step needed at runtime.
- A named NuGet cache volume keeps `dotnet restore` warm across runs. Cold first run is ~3-5 min (image pull + cache warm); subsequent runs are seconds-to-output.
- Compose profile `e2e` makes the runner opt-in — `docker compose up` won't start it, only `docker compose --profile e2e run --rm e2e-tests` does (which is what the Make target does).

`make test-e2e-fast` is the host path: same `dotnet test` command, RID-native, with `--filter Category!=VisualRegression`. Use it when you're on a non-visual code change and want the inner loop tight.

> Switching between the two flows can leave RID-mismatched bin/obj artefacts and produce `NETSDK1047 'project.assets.json' doesn't have a target for 'net10.0/win-x64'` (or `linux-x64`) on the next run. `make clean-test-bin` clears them; the targets are idempotent.

## Test data isolation

The suite seeds wiki pages and content blocks via direct HTTP POST against the controller surface (`SeedHelpers.SeedWikiPageAsync` / `SeedContentBlockAsync`). No fixture seeds are committed; every test owns its data.

- Each seeded entity uses an `e2e-{Guid:N}-` prefix on its slug or key. UUID prefix means tests across runs and across parallel CI shards never collide.
- Cleanup is `IAsyncLifetime.DisposeAsync` per test class — wiki pages get soft-deleted; content blocks leak (no DELETE route exists for them, by design). The leak is harmless: the UUID prefix prevents test-run cross-contamination, and content-block volume is small.
- Auth-gated tests are out of scope for this iteration. The suite is anonymous-only.

## What's not covered

- **Authenticated flows.** No DfE Sign-In integration in the harness; auth-gated controller surfaces have unit + integration coverage but no browser coverage.
- **Cross-browser.** Chromium only. Firefox/WebKit aren't part of the budget.
- **Mobile gesture interactions.** Viewport sizes are set, but no touch/swipe simulation.
- **Trace replay in CI.** Playwright's `Tracing.StartAsync` isn't wired into the harness — failure debugging in CI is by log + snapshot artefact. To enable trace replay locally, hook `Context.Tracing.StartAsync`/`StopAsync` around the test body and inspect the resulting `.zip` with `playwright.ps1 show-trace`.

## Five-minute runtime budget

Hard cap: the full suite must complete within 5 min wall-clock on `ubuntu-latest` (2-4 vCPU). If a single test exceeds 30s, investigate before merging — almost always a `WaitForLoadState.NetworkIdle` waiting on an unrelated background fetch, fixable by switching to `DOMContentLoaded` plus an explicit `Expect(...).ToBeVisibleAsync()` on the element you actually care about.
