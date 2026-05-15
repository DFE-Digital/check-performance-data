# CheckPerformanceData E2E Tests

End-to-end Playwright + xUnit suite that drives a real Chromium browser against a deployed Web app.

## Quick start

There are two ways to run the suite, depending on what you need:

| Flow | Command | What it runs | When to pick |
|------|---------|--------------|--------------|
| Container (canonical) | `make test-e2e` | Full suite incl. visual regression, inside the Linux Playwright container against the canonical Linux-Chromium baseline | Before commit/push, when touching anything that affects rendered output, before opening a PR |
| Host (fast) | `make test-e2e-fast` | E2E suite natively on the host SDK with `--filter Category!=VisualRegression`; visual regression skipped | TDD inner loop on functional tests; quick smoke after a non-visual code change |

Both targets `cd` into the repo from the repo root and assume Docker (for `make test-e2e`) or the .NET 10 host SDK (for `make test-e2e-fast`) is installed. `make help` from the repo root lists every target the Makefile exposes.

First run of `make test-e2e` is slow — it pulls the ~1.5GB Playwright image, builds the thin .NET 10 overlay (per `tests/DfE.CheckPerformanceData.E2ETests/Dockerfile`), and warms the named NuGet cache volume. Allow ~3-5 minutes. Subsequent runs reuse both the image layer cache and the NuGet volume → seconds-to-test-output.

Manual fallback (no Make / no Docker):

```bash
cd check-performance-data
docker compose up -d                      # starts Web + Postgres
pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium  # one-time
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "Category!=VisualRegression" --configuration Release
```

On Windows without `pwsh` (PowerShell Core), the install script runs via Windows PowerShell:

```powershell
powershell.exe -ExecutionPolicy Bypass -File tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium
```

> **Switching between flows:** if you alternate between `make test-e2e-fast` (native, Windows/macOS host) and `make test-e2e` (Linux container), the host's `bin/`/`obj/` may carry RID-mismatched runtime artefacts that cause `NETSDK1047 'project.assets.json' doesn't have a target for 'net10.0/win-x64'` (or `linux-x64`) on the next run. Run `make clean-test-bin` to clear them; the targets are idempotent.

## Configuration

| Env var | Default | Purpose |
|---------|---------|---------|
| `CPD_E2E_BASE_URL` | `http://localhost:8080` | URL the harness drives. |
| `CPD_E2E_READY_TIMEOUT_SECONDS` | `90` | How long the fixture polls `/healthcheck` before giving up. |

## Snapshot diffs (visual regression failures)

When `IPage.MatchSnapshotAsync` detects pixel divergence above the threshold, it writes a three-PNG trio to `tests/DfE.CheckPerformanceData.E2ETests/Snapshots/diffs/` next to the canonical `Snapshots/linux-chromium/` directory:

| File | What it is |
|------|-----------|
| `{name}.expected.png` | The committed canonical PNG from `Snapshots/linux-chromium/{name}.png` |
| `{name}.actual.png` | The screenshot the test just captured |
| `{name}.diff.png` | A copy of `actual.png` with pixels that differ above the per-channel tolerance tinted red (RGBA 255,0,0,255) |

The thrown `XunitException` message ends with the absolute path to the `Snapshots/diffs/` directory, so failure logs point you straight at the artefacts.

`Snapshots/diffs/` is gitignored at the project level (see `tests/DfE.CheckPerformanceData.E2ETests/.gitignore`) — never committed. CI's existing `e2e:` job uploads the entire `Snapshots/` tree as the `e2e-snapshots` artefact on failure, so the diff trio surfaces in the run's Artifacts panel automatically.

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
2. Delete the snapshot you want to refresh:
   ```bash
   rm tests/DfE.CheckPerformanceData.E2ETests/Snapshots/linux-chromium/{name}.png
   ```
3. Choose either path:
   - **Local container path (preferred when Docker is available):** `make test-e2e` (first run writes the new `.png` and fails the test with "did not exist — written, run again to verify"). Inspect the generated PNG, then `make test-e2e` again to confirm the comparison now passes. Commit the regenerated PNG.
   - **CI path (fallback for devs without Docker):** Push to a branch labelled `deploy` so the CI `e2e:` job runs on Linux. The first CI run writes the new `.png` and fails. Download the `e2e-snapshots` artefact from that run; the regenerated PNG is under `linux-chromium/{name}.png`. Commit it back. Push again — second CI run passes; PR diff surfaces the regenerated PNG for review.

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
├── .gitignore                      # /Snapshots/diffs/ — never commit failure artefacts
├── Dockerfile                      # thin overlay: playwright/dotnet:v1.59.0-noble + .NET 10 SDK
├── Fixtures/
│   ├── PlaywrightFixture.cs         # IAsyncLifetime; readiness probe; antiforgery scrape; seed HttpClient
│   └── PlaywrightCollection.cs      # [CollectionDefinition("E2E")] + ICollectionFixture<>
├── Helpers/
│   ├── SeedHelpers.cs               # SeedWikiPageAsync / SeedContentBlockAsync / SoftDeleteWikiPageAsync
│   ├── AntiforgeryHelpers.cs        # static ScrapeAsync(HttpClient, formPath) -> (Token, Cookie)
│   ├── PageStabilisationExtensions.cs   # IPage.StabiliseAsync() — animations off + fonts.ready + NetworkIdle
│   ├── PageSnapshotExtensions.cs    # IPage.MatchSnapshotAsync(name, maxDiffPixelRatio) + BuildDiffArtefactsAsync
│   └── PageSnapshotExtensionsTests.cs   # pure unit tests for the diff-PNG-emission helper
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
│   ├── linux-chromium/              # canonical .png artefacts (committed)
│   └── diffs/                       # gitignored; populated only on threshold breach
└── HarnessSmokeTests.cs
```

## Adding a new test

1. Pick the appropriate folder (`Wiki/` for browser-driven wiki tests, `Web/` for HTTP and chrome/layout browser tests, `Visual/` for snapshots).
2. Inherit `PageTest` for browser tests; omit inheritance for HTTP-only tests.
3. Add `[Collection("E2E")]` and `[Trait("Category", "W{N}")]`.
4. If the test creates wiki pages or content blocks, implement `IAsyncLifetime` with cleanup in `DisposeAsync`.
5. Use the `e2e-{Guid:N}-` prefix on every slug/key.
6. Use `_fixture.SeedClient` + `SeedHelpers.*` for HTTP seeding.
7. Use Playwright `Expect(Locator).ToBeVisibleAsync()` / `ToHaveTextAsync(...)` for browser assertions; use `HttpClient` directly for HTTP assertions.

### Worked example: cookie banner accept/reject

The cookie consent flow — `/cookies` GET/POST + the `_CookieBanner` partial in `_Layout.cshtml` + `wwwroot/js/cookies.js` — is currently uncovered end-to-end. The example below shows the patterns above applied to that flow: anonymous navigation, locator scoping, cookie assertion, and a page reload to confirm consent persistence. Save as `Web/CookieBannerTests.cs`:

```csharp
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class CookieBannerTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task Accept_PersistsConsent_AndSuppressesBannerOnReload()
    {
        // Fresh anonymous context. Without the consent cookie, the banner JS
        // removes the `hidden` attribute on the outer wrapper.
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{_fixture.BaseUrl}/");

        var banner = Page.Locator("[data-module='govuk-cookie-banner']");
        await Expect(banner).ToBeVisibleAsync();

        // Accept analytics. The initial message hides; the accepted message shows.
        await banner.Locator("[data-accept-cookies]").ClickAsync();
        await Expect(banner.Locator("[data-cookie-banner-message]")).ToBeHiddenAsync();
        await Expect(banner.Locator("[data-cookie-banner-accepted]")).ToBeVisibleAsync();

        // Consent cookie set with analytics: true.
        var cookies = await Page.Context.CookiesAsync();
        var consent = cookies.SingleOrDefault(c => c.Name == "cookies_policy");
        Assert.NotNull(consent);
        Assert.Contains("\"analytics\":true", Uri.UnescapeDataString(consent!.Value));

        // Reload: banner JS sees the consent cookie and leaves `hidden` in place.
        await Page.ReloadAsync();
        await Expect(banner).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Reject_PersistsConsent_AndSuppressesBannerOnReload()
    {
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{_fixture.BaseUrl}/");

        var banner = Page.Locator("[data-module='govuk-cookie-banner']");
        await Expect(banner).ToBeVisibleAsync();

        await banner.Locator("[data-reject-cookies]").ClickAsync();
        await Expect(banner.Locator("[data-cookie-banner-rejected]")).ToBeVisibleAsync();

        var cookies = await Page.Context.CookiesAsync();
        var consent = cookies.SingleOrDefault(c => c.Name == "cookies_policy");
        Assert.NotNull(consent);
        Assert.Contains("\"analytics\":false", Uri.UnescapeDataString(consent!.Value));

        await Page.ReloadAsync();
        await Expect(banner).ToBeHiddenAsync();
    }
}
```

What each line is doing — read alongside the seven-step checklist above:

| Pattern | Why |
|---|---|
| `: PageTest` + primary-ctor `(PlaywrightFixture fixture)` | `PageTest` (from `Microsoft.Playwright.Xunit`) gives you `Page` and `Expect(...)` for free. The fixture comes from the collection (one Postgres + one running Web container shared by every test in `[Collection("E2E")]`). |
| `[Trait("Category", "W1")]` | Categorises the test for the filter table in this README. `W1` = read-path browse. Pick the trait that matches what your test exercises so it runs (or skips) cleanly with the existing Make targets. |
| `await Page.Context.ClearCookiesAsync()` *before* `GotoAsync` | The collection-shared `Page` carries cookies from earlier tests. Anonymous-state tests must clear first or risk a false-pass because a previous test left the consent cookie behind. |
| `Page.Locator("[data-module=...]")` scoping + `banner.Locator("[data-accept-cookies]")` | Scope a parent locator once, then resolve child locators from it. Lazier than `Page.Locator(...)` everywhere, and reads as "click the accept button *inside this banner*", which mirrors the page structure. |
| `Expect(...).ToBeVisibleAsync()` / `ToBeHiddenAsync()` | Both auto-wait up to 5s. `ToBeHiddenAsync()` is satisfied by the `hidden` HTML attribute, `display: none`, or `visibility: hidden` — so the banner's Razor `hidden` attribute counts as hidden without extra CSS assertions. |
| `Page.Context.CookiesAsync()` + `Uri.UnescapeDataString(...)` | The cookie value is URL-encoded JSON. Unescape before string-matching to avoid false negatives from `%22` / `%3A` slipping in. |
| `await Page.ReloadAsync()` | Re-runs the banner JS. The persistence assertion is "the JS now sees the cookie and *doesn't* remove `hidden` from the wrapper" — which is what `ToBeHiddenAsync()` on the wrapper after reload confirms. |
| No `IAsyncLifetime` / no `SeedHelpers` | This test creates no DB rows. When you write a test that *does* (a wiki page, a content block) — implement `IAsyncLifetime`, seed in `InitializeAsync`, clean up in `DisposeAsync`, and prefix every slug/key with `e2e-{Guid:N}-` so a half-cleaned fixture still leaves the rest of the suite green. |

When you stop writing test scaffolding and need to seed a real wiki page or content block, the helpers in `Helpers/SeedHelpers.cs` do the antiforgery-scrape + POST handshake for you — call `await SeedHelpers.SeedWikiPageAsync(_fixture.SeedClient, ...)` and assert against the page that comes back.
