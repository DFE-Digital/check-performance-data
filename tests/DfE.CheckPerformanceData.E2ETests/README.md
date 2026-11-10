# CheckPerformanceData E2E Tests

End-to-end Playwright + xUnit suite that drives a real Chromium browser against a deployed Web app.

## Quick start

There are two ways to run the suite, depending on what you need:

| Flow | Command | What it runs | When to pick |
|------|---------|--------------|--------------|
| Container (canonical) | `make test-e2e` | Full functional suite inside the Linux Playwright container. | Before commit/push, before opening a PR |
| Host (fast) | `make test-e2e-fast` | Same suite natively on the host SDK | TDD inner loop; quick smoke after a code change |

These targets `cd` into the repo from the repo root and assume Docker (for `make test-e2e`) or the .NET SDK that the root `global.json` names (for `make test-e2e-fast`) is installed. `make help` from the repo root lists every target the Makefile exposes.

First run of `make test-e2e` is slow — it pulls the ~1.5GB Playwright image, builds the thin overlay (per `tests/DfE.CheckPerformanceData.E2ETests/Dockerfile`), and warms the named NuGet cache volume. Allow ~3-5 minutes. Subsequent runs reuse both the image layer cache and the NuGet volume → seconds-to-test-output.

Manual fallback (no Make / no Docker):

```bash
cd check-performance-data
docker compose up -d                      # starts Web + Postgres
pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium  # one-time
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release
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
| `CPD_E2E_RETRY_ATTEMPTS` | `1` | Maximum attempts per test for `[RetryFact]`/`[RetryTheory]` (1 = no retries). Read at test discovery, so set it for the whole run. See `Retrying/RetrySettings.cs`. |

## Test categories

| Trait | Filter | Use |
|-------|--------|-----|
| `Smoke` | `--filter "Category=Smoke"` | A few minutes to show that a deployed app is up and that each area and each journey still works. Runs as a check on every pull request that has a review app. See "What belongs in the smoke set" below. |
| `FullRegression` | `--filter "Category=FullRegression"` | Marks a class whose tests are not all smoke tests. Not used to choose what CI runs: the whole suite runs every test, with or without a category. |
| `Slow` | `--filter "Category!=Slow"` | Tests that wait on real timeouts or polling; exclude them for a quicker sweep. |

### What CI runs

A pull request with the `deploy` label gets a review app, and two E2E runs against it:

| Run | Where | Filter | Takes | Result |
|-----|-------|--------|-------|--------|
| Smoke | `E2E smoke tests` job in `build-and-deploy.yml` | `Category=Smoke` | A few minutes | One of the pull request's checks |
| Whole suite | `e2e.yml`, started when `build-and-deploy.yml` finishes | None (every test) | About 15 minutes | A comment on the pull request |

The whole-suite run is not one of the pull request's checks. Nothing waits for it and it
cannot stop a merge; it puts one comment on the pull request, "E2E tests running", and
replaces it with the counts and the names of any failed tests.

When either run fails, its comment mentions whoever opened the pull request, and GitHub
notifies them: by email, unless they have turned email off in their GitHub notification
settings. A pass sends nothing. If you are told a run failed, fix it, or reply on the pull
request to say why it was not the change.

A pull request can be merged while its whole-suite run is still going. Deleting the review
app waits for the run to finish, for up to 45 minutes. If the app goes anyway, or a newer
commit is deployed to it during the run, the comment says the run was interrupted and why,
and mentions nobody: those failures are not the change's.

A run is never cancelled because a newer one started. Cancel one by hand from the Actions
tab if it is no longer wanted. To run the suite again, or to try a change to `e2e.yml`
(which GitHub reads from `main`, so a change does nothing until merged), use
**Actions > E2E Tests > Run workflow** and give the pull request number.

### What belongs in the smoke set

The smoke set answers one question quickly: is this deployment working? It is wide and
shallow.

- One test for each area of the service that shows the area is reachable and renders:
  each admin section, the content pages, search, the public pages.
- The happy path of each journey a school can take, end to end.
- Anything that depends on the hosting rather than the code: static assets, sign-in,
  file uploads through the ingress.

Left out: validation messages, edge cases, variants of a path already covered, and
anything slow. Those run in the whole suite.

Put `[Trait("Category", "Smoke")]` on the test method, or on the class when every test in
it qualifies. When you add an area or a journey, add its happy path to the smoke set.

Tests are otherwise grouped by folder rather than by trait — `Wiki/`, `Web/`,
`Admin/` — so scope a local run with `--filter "FullyQualifiedName~Admin"`
rather than reaching for a category.

The smoke set (the pull request check):

```bash
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release --filter "Category=Smoke"
```

The whole suite (the run that comments on the pull request):

```bash
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --configuration Release
```

### Narrowing in & TDD inner loop

After a prior `dotnet build --configuration Release` of the solution, skip the rebuild on subsequent runs with `--no-build` — saves ~20s per iteration:

```bash
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "Category=Smoke" --configuration Release --no-build
```

Just one test class — `~` is a contains-match against `FullyQualifiedName`:

```bash
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "FullyQualifiedName~SoftDeleteWikiPageTests" --configuration Release --no-build
```

Exactly one test — `=` is an exact match against the full FQN, so you need the full `Namespace.Class.Method`:

```bash
dotnet test tests/DfE.CheckPerformanceData.E2ETests/ --filter "FullyQualifiedName=DfE.CheckPerformanceData.E2ETests.Wiki.SoftDeleteWikiPageTests.SoftDeletedPage_DoesNotAppearInSearch" --configuration Release --no-build
```

Watch mode — re-runs the matched tests whenever a source file under the test project (or its referenced projects) changes:

```bash
dotnet watch test --project tests/DfE.CheckPerformanceData.E2ETests/ -- --filter "FullyQualifiedName~WarningTextRenderTests"
```

The `--` separator passes everything after it through to `dotnet test` rather than `dotnet watch`. Watch mode assumes the compose stack (`docker compose --profile e2e up -d web db azurite`) is already up; if it isn't, the fixture's readiness probe will fail every iteration.

## Debugging a red CI run

The smoke run is the `E2E smoke tests` job on the pull request. The whole-suite run is
linked from its comment on the pull request, and is listed under **Actions > E2E Tests**.
The whole-suite run always uploads `e2e-results`, the `.trx` results file.

On failure either run uploads two artifacts (both 14-day retention):
- `e2e-snapshots` — the `Snapshots/` tree. It holds the screenshots that some tests save when they fail, in `Snapshots/failures/`.
- `e2e-traces` — a Playwright trace for each failing test, captured by the harness only when the test failed (a green run writes nothing).

Replay a trace locally with:

```bash
pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Release/net10.0/playwright.ps1 show-trace <path-to-trace.zip>
```

Traces are named `{ClassName}.{ordinal}.zip`; open one to see which test it belongs to (its title is the class name). To capture traces in a local run, set `CPD_E2E_TRACES_DIR` to a directory before running the suite — only failing tests write files there.

## Project boundaries

- **Black-box harness only** — no `WebApplicationFactory`, no in-process Postgres, no `TestAuthenticationHandler`.
- **Anonymous-only test corpus** — seeds via direct HTTP POST against the controller surface; auth-gated tests are out of scope for this iteration.
- **Test data isolation** — every wiki page / content block created uses an `e2e-{Guid:N}-` prefix. Cleanup is `IAsyncLifetime.DisposeAsync` per class; content-block leak is accepted (no DELETE route) but harmless via the UUID prefix.
- **5-minute runtime budget** — the full suite must complete within 5 min wall-clock on `ubuntu-latest` (2-4 vCPU). If a single test exceeds 30s, investigate (usually a `WaitForLoadState.NetworkIdle` waiting on an unrelated background fetch).

## Layout

```
tests/DfE.CheckPerformanceData.E2ETests/
├── .gitignore                      # /Snapshots/failures/ — never commit failure screenshots
├── Dockerfile                      # thin overlay on playwright/dotnet, which brings Chromium and the .NET 10 SDK
├── Fixtures/
│   ├── PlaywrightFixture.cs         # IAsyncLifetime; readiness probe; antiforgery scrape; owns SeedClient (impersonation) + AnonymousClient (no-cookie) TestHttpClients
│   └── PlaywrightCollection.cs      # [CollectionDefinition("E2E")] + [CollectionDefinition("Http")], both over PlaywrightFixture
├── Helpers/
│   ├── SeedHelpers.cs               # SeedWikiPageAsync / SeedContentBlockAsync / SoftDeleteWikiPageAsync
│   ├── AntiforgeryHelpers.cs        # ScrapeAsync(TestHttpClient, formPath) / (HttpClient, formPath, impersonation) -> (Token, Cookie)
│   ├── PageStabilisationExtensions.cs   # IPage.StabiliseAsync() — animations off + fonts.ready + NetworkIdle
│   └── Snapshots.cs                 # Snapshots.FailuresDir — where a test saves a screenshot when it fails
├── Retrying/
│   ├── RetrySettings.cs             # read CPD_E2E_RETRY_ATTEMPTS (default 1) into RetrySettings.MaxRetries
│   ├── RetryFactAttribute.cs + RetryFactDiscoverer.cs       # settings-driven [RetryFact]; reuses xRetry's RetryTestCase
│   └── RetryTheoryAttribute.cs + RetryTheoryDiscoverer.cs   # settings-driven [RetryTheory]
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
├── Snapshots/
│   ├── search-ux/                   # screenshots kept as review evidence (committed)
│   └── failures/                    # gitignored; screenshots that tests save when they fail
└── HarnessSmokeTests.cs
```

## Adding a new test

1. Pick the appropriate folder (`Wiki/` for browser-driven wiki tests, `Web/` for HTTP and chrome/layout browser tests, `Admin/` for admin pages).
2. Inherit `PageTest` for browser tests; omit inheritance for HTTP-only tests.
3. Add `[Collection("E2E")]`. Only add a `[Trait("Category", ...)]` if the test needs one of the traits in the table above. A test with no category runs in the whole suite; see "What belongs in the smoke set" for the ones that should also be smoke tests.
4. If the test creates wiki pages or content blocks, implement `IAsyncLifetime` with cleanup in `DisposeAsync`.
5. Use the `e2e-{Guid:N}-` prefix on every slug/key.
6. Use `_fixture.SeedClient` + `SeedHelpers.*` for HTTP seeding. For requests that must be genuinely anonymous (no impersonation cookie — sign-in redirects, bad-share-token 404s), use `_fixture.AnonymousClient`, which never carries the impersonation cookie.
7. Use Playwright `Expect(Locator).ToBeVisibleAsync()` / `ToHaveTextAsync(...)` for browser assertions; use `HttpClient` directly for HTTP assertions.

### Worked example: cookie banner accept/reject

The cookie consent flow — `/cookies` GET/POST + the `_CookieBanner` partial in `_Layout.cshtml` + `wwwroot/js/cookies.js` — is currently uncovered end-to-end. The example below shows the patterns above applied to that flow: anonymous navigation, locator scoping, cookie assertion, and a page reload to confirm consent persistence. Save as `Web/CookieBannerTests.cs`:

```csharp
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
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
| `await Page.Context.ClearCookiesAsync()` *before* `GotoAsync` | The collection-shared `Page` carries cookies from earlier tests. Anonymous-state tests must clear first or risk a false-pass because a previous test left the consent cookie behind. |
| `Page.Locator("[data-module=...]")` scoping + `banner.Locator("[data-accept-cookies]")` | Scope a parent locator once, then resolve child locators from it. Lazier than `Page.Locator(...)` everywhere, and reads as "click the accept button *inside this banner*", which mirrors the page structure. |
| `Expect(...).ToBeVisibleAsync()` / `ToBeHiddenAsync()` | Both auto-wait up to 5s. `ToBeHiddenAsync()` is satisfied by the `hidden` HTML attribute, `display: none`, or `visibility: hidden` — so the banner's Razor `hidden` attribute counts as hidden without extra CSS assertions. |
| `Page.Context.CookiesAsync()` + `Uri.UnescapeDataString(...)` | The cookie value is URL-encoded JSON. Unescape before string-matching to avoid false negatives from `%22` / `%3A` slipping in. |
| `await Page.ReloadAsync()` | Re-runs the banner JS. The persistence assertion is "the JS now sees the cookie and *doesn't* remove `hidden` from the wrapper" — which is what `ToBeHiddenAsync()` on the wrapper after reload confirms. |
| No `IAsyncLifetime` / no `SeedHelpers` | This test creates no DB rows. When you write a test that *does* (a wiki page, a content block) — implement `IAsyncLifetime`, seed in `InitializeAsync`, clean up in `DisposeAsync`, and prefix every slug/key with `e2e-{Guid:N}-` so a half-cleaned fixture still leaves the rest of the suite green. |

When you stop writing test scaffolding and need to seed a real wiki page or content block, the helpers in `Helpers/SeedHelpers.cs` do the antiforgery-scrape + POST handshake for you — call `await SeedHelpers.SeedWikiPageAsync(_fixture.SeedClient, ...)` and assert against the page that comes back.


# How do I run all the tests manually? / CHEATSHEET

● Bash syntax — works in PowerShell too for the simple commands:

  One-by-one, layered

  ### 1. Build everything in Release (~20s) — surfaces compile errors before tests
  dotnet build src/DfE.CheckPerformanceData.slnx -c Release --nologo

  ### 2. Unit tests only (~60s) — fast loop, no Docker needed
  dotnet test tests/DfE.CheckPerformanceData.UnitTests/ -c Release --no-build --nologo

  ### 3. Integration tests only (~15s) — needs Docker running (Testcontainers Postgres)
  dotnet test tests/DfE.CheckPerformanceData.IntegrationTests/ -c Release --no-build --nologo

  ### 4. E2E on the host (~30s) — fast Playwright sweep
  make test-e2e-fast
  #### Or, equivalently, without make:
  dotnet test tests/DfE.CheckPerformanceData.E2ETests/ -c Release --no-build --nologo

  ### 5. E2E in the Linux Playwright container (~10-15min) — Linux Chromium, as in CI
  make test-e2e
  #### Or, equivalently, without make:
  docker compose --profile e2e run --rm e2e-tests

  One-shot equivalents

  ### Everything except E2E (unit + integration in one command)
  dotnet test src/DfE.CheckPerformanceData.slnx --filter "FullyQualifiedName!~E2ETests" -c Release --nologo

  ### Literally everything via dotnet, including E2E (assumes browsers are installed)
  dotnet test src/DfE.CheckPerformanceData.slnx -c Release --nologo

  ## Useful filters when narrowing in

  ### Just one test class:
  dotnet test tests/DfE.CheckPerformanceData.UnitTests/ --filter "FullyQualifiedName~AdminControllerTests" -c Release --no-build

  ### Just one named test:
  dotnet test tests/DfE.CheckPerformanceData.UnitTests/ --filter
  "FullyQualifiedName=DfE.CheckPerformanceData.UnitTests.Wiki.WikiServiceTests.MovePageAsync_Reorder" -c Release --no-build

  ## Watch mode (TDD inner loop)

  ### Re-runs the matched tests whenever source changes
  dotnet watch test --project tests/DfE.CheckPerformanceData.UnitTests/ -- --filter "FullyQualifiedName~WikiServiceTests"

  Cleanup when switching between native and container E2E

  make clean-test-bin
  #### Or, without make:
  rm -rf tests/DfE.CheckPerformanceData.E2ETests/bin tests/DfE.CheckPerformanceData.E2ETests/obj

  Needed because the native run and the container run produce mutually-incompatible bin/ artefacts (different platform IDs); skipping this       
  between modes is the #1 source of "but it worked yesterday" on this project.

  Order of operations I'd suggest for a "full sweep" before a PR:

  1. dotnet build src/DfE.CheckPerformanceData.slnx -c Release --nologo — fast smoke for compile errors
  2. dotnet test src/DfE.CheckPerformanceData.slnx --filter "FullyQualifiedName!~E2ETests" -c Release --no-build --nologo — unit + integration in
  one shot
  3. make test-e2e — full E2E in the container

  That's the pattern I'd run automatically given more autonomy on this repo. The first two together are ~90 seconds; the third is the long pole.