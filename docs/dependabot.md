# Dependabot

Dependabot tells the team about security advisories in our dependencies and opens pull requests
to update them, between image builds as well as at them (OWASP Top 10 2025, A03 software supply
chain failures; #616).

## Security alerts and security updates

These are repository settings, in the security section of **Settings**, and both are on:

- **Dependabot alerts** list each advisory that affects a dependency, under
  **Security > Dependabot**.
- **Dependabot security updates** open a pull request for an alert when a fixed version exists.
  They do not wait for the weekly run and are not held back by the cooldown below.

Changing either setting needs a repository admin.

## Version updates

`.github/dependabot.yml` asks for version updates every Monday at 07:00 UK time for:

| Ecosystem | Where | What it covers |
|-----------|-------|----------------|
| NuGet | `/src` | Every project in `src/DfE.CheckPerformanceData.slnx`, the test projects included: `src/Directory.Packages.props`, the test `.csproj` files and every `packages.lock.json` |
| Docker | the four Dockerfiles | Web, worker, E2E test runner and maintenance page base images |
| GitHub Actions | `/` and `/.github/actions/image-sbom` | The workflows and the SBOM composite action |

Dependabot reads this file from `main` only, so a change to it takes effect once it is merged.
Each run shows in **Actions** as **Dependabot Updates**, with its log.

### How updates are grouped

- Minor and patch updates come as one pull request per ecosystem (`minor-and-patch`).
- Each major update comes in a pull request of its own, because it may need code changes.
- `Microsoft.Playwright` and `Microsoft.Playwright.Xunit` come together in a pull request of their
  own (`playwright`). See [Playwright](#playwright).
- A new version is not proposed until it is 7 days old (`cooldown`), so a compromised or broken
  release has time to be found and withdrawn. This applies to version updates only.

### What is not proposed

| Dependency | Not proposed | Why |
|------------|--------------|-----|
| `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.*`, `System.Security.Cryptography.Xml`, `Npgsql*`, `Serilog.AspNetCore` | Major versions | They move with the .NET version. Moving to .NET 11 is an upgrade of its own. Patches still come. |
| `mcr.microsoft.com/dotnet/aspnet`, `mcr.microsoft.com/dotnet/sdk` | Major versions | The `10.0` tag already gives every build the latest .NET 10 patch. |
| `SixLabors.ImageSharp` | Major versions | Version 4 fails the build without a paid Six Labors licence key. |
| `mcr.microsoft.com/playwright/dotnet` | Any version | Its tag follows the NuGet package. See [Playwright](#playwright). |

To stop a dependency being proposed, add an `ignore` entry to `.github/dependabot.yml` with the
reason as a comment. Commenting `@dependabot ignore this major version` on a pull request also
works, but nobody can see it later.

## Playwright

The `playwright/dotnet` image in `tests/DfE.CheckPerformanceData.E2ETests/Dockerfile` bundles the
Chromium that the `Microsoft.Playwright` package drives, so the two must have the same version.
Dependabot cannot raise both in one pull request. It raises the package, and the **Build** check
runs `scripts/check-playwright-pin.sh`, which fails until they match.

To finish a `playwright` pull request, change the `FROM mcr.microsoft.com/playwright/dotnet:v…`
line to the new version and push to the Dependabot branch. The image tag is also listed in
[E2E-Playwright.md](E2E-Playwright.md).

## Reviewing a Dependabot pull request

Dependabot pull requests run with Dependabot's own secrets, and none are set. They get:

- **Build**: locked restore, build, and unit and integration tests
- **Build and Deploy > Build**: the web and worker images

They do not get a review app, so neither the smoke tests nor the full E2E suite run, even with
the `deploy` label. For an update that changes what users see or how the service talks to
something else (GOV.UK Frontend, DfE Sign-in, Npgsql, Azure Storage, Notify, Zendesk), run the
stack and the E2E tests locally on the branch before approving.

Read the release notes Dependabot includes, approve and merge as for any other pull request.
