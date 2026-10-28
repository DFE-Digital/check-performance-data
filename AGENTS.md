# AGENTS.md

Guidance for AI coding agents in this repository. This file is the single source of truth. Other
agent files (`CLAUDE.md`, `.github/copilot-instructions.md`) point here and do not copy it.

## Agent entry points

| Tool | File it reads | How it gets this file |
| --- | --- | --- |
| OpenAI Codex, Copilot coding agent | `AGENTS.md` | Reads this file directly |
| Claude Code | `CLAUDE.md` | Imports it with `@AGENTS.md`, then describes `.claude/` |
| GitHub Copilot chat | `.github/copilot-instructions.md` | Points to this file |

**Put project rules in this file only.** A tool's own file may describe that tool's
configuration, but never a project rule.

## Overview

Check performance data lets schools and colleges in England check the pupil and results data
that the Department for Education (DfE) holds for them. Schools ask for changes (add, remove,
include, merge pupils, and results enquiries). Admins set up checking windows, load supplier
files, and send the requests on to Zendesk.

Stack: .NET 10, ASP.NET Core MVC with Razor views, EF Core with PostgreSQL (Npgsql), Azure Blob
Storage and Storage Queues (Azurite locally), GOV.UK Frontend (`GovUk.Frontend.AspNetCore`),
DfE Sign-in (OIDC).

Two facts shape every rule below:

1. **This repository is public.** Anyone can read each line of code, comment, fixture, branch
   name and commit message, permanently.
2. **The service holds personal data about children.** Names, dates of birth, UPNs, ULNs and
   results pass through this code. A careless log line or fixture is a data incident.

## Non-negotiables

- **Never commit real pupil or school data.** Generate test and seed data. Sample supplier files
  must contain made-up records only.
- **Never commit secrets or internal-only URLs.** `.env` is local. Do not force-add an ignored
  file.
- **Do not log personal data.** Log ids and reference numbers, not names, dates of birth, UPNs
  or ULNs.
- **Do not weaken an authorisation gate to make something work.** See
  [Admin pages](#admin-pages).

## Security

This service follows the government's
[Secure by Design](https://www.security.gov.uk/policy-and-guidance/secure-by-design/) principles
and the [OWASP Top 10 (2025)](https://owasp.org/Top10/). Each rule below applies one of them. Keep the controls that exist, and use them in new code.

### Access control (OWASP A01)

- **Scope every read to the signed-in school.** Get the school from `ICurrentUserService`
  (`OrganisationLaestab`, `OrganisationUrn`), never from a route, query or form value. A reference number or
  id from a request is a lookup key, not a permission.
- **Log a refusal with `AccessRefusalLog`.** This applies to a refused admin section and to a
  school that asks for another school's request. Log ids, not personal data.
- Every `admin/` controller has `[RequireAdminSection(...)]` (see [Admin pages](#admin-pages)).
- Every POST action that a form calls has `[ValidateAntiForgeryToken]`. There is no global
  filter, so a new action without the attribute has no CSRF protection.

### Injection and output (OWASP A05)

- Use EF Core LINQ or parameters for SQL. Do not build SQL from strings.
- Render markdown and content-managed HTML only through `HtmlRenderingService`, which sanitises
  it. Do not use `Html.Raw` on text that a user or an admin typed.
- **A CSV that contains text someone typed must neutralise formula prefixes** (`=`, `+`, `-`,
  `@`, tab, CR), as `AuditLogCsv.Field` does.

### Security headers and the browser (OWASP A02)

- `SecurityHeadersMiddleware` sets the headers, including a Content-Security-Policy with a
  per-request nonce and no `'unsafe-inline'` or `'unsafe-eval'`. Do not loosen the policy.
- Put scripts in files or give them the nonce (`CspNonceTagHelper` does this). Do not add inline
  event handlers such as `onclick`.
- Mark a response that contains personal data `no-store` with `[RequiresNoStore]` or
  `Response.NoStore()` (OWASP A04).

### Supply chain (OWASP A03, Secure by Design: source secure technology)

- Add a package only when the framework cannot do the job. Choose a maintained package, and
  add its version in `src/Directory.Packages.props`.
- Commit the lock files. Do not turn off a Dependabot, Snyk, SBOM or ZAP check to make a build
  pass. See `docs/dependabot.md`, `docs/sbom.md`, `docs/zap-baseline.md`.

### Logging and monitoring (OWASP A09, Secure by Design: build in detect and respond)

- Record admin actions that change data in the audit log (`docs/audit-log.md`).
- Log security events with enough to investigate (who, what, where), but no personal data and
  no secrets or tokens.

### Secure by Design in each change

- **Minimise the attack surface.** Do not add an endpoint, a parameter or a dependency that the
  change does not need. A `/dev/*` page checks `Dev:ToolsEnabled` and the production guard,
  like the other `Dev*Controller` classes.
- **Defend in depth.** Keep a check in the service even when the controller checks too.
- **Fail closed.** When the code cannot decide, refuse (see [Fail closed](#fail-closed)).
- **Design usable security.** A refusal tells the user what to do next, and does not show a
  stack trace or internal detail.
- **Make changes securely.** If a change touches authentication, authorisation, personal data,
  file upload or a security control, say so in the pull request description.

## Commands

```sh
dotnet build src/DfE.CheckPerformanceData.slnx
dotnet test src/DfE.CheckPerformanceData.slnx --filter "FullyQualifiedName!~E2ETests"   # what CI runs
dotnet test tests/DfE.CheckPerformanceData.UnitTests/ --filter "FullyQualifiedName~MyTestName"
docker compose up --build -d                       # PostgreSQL + Azurite
dotnet run --project src/DfE.CheckPerformanceData.Web
make test-e2e                                      # E2E suite in the Playwright container
make test-e2e-fast                                 # E2E suite on the host SDK
```

`make test-e2e` does not rebuild the web image. Rebuild it first, or a stale image fails the
whole suite with "deployment not ready". See `docs/E2E-Playwright.md`.

## Architecture

Clean (onion) architecture, six projects:

| Project | Contents |
| --- | --- |
| Domain | Domain models and enums. No dependencies. |
| Application | Business logic, service interfaces and implementations, DTOs |
| Persistence | EF Core `PortalDbContext`, repositories, entity configurations, seeders |
| Infrastructure | Authentication (DfE Sign-in or fake dev auth), HTTP clients, blob clients |
| Web | Controllers and Razor views |
| RulesEngineWorker | Background worker that reads Azure Storage Queues and creates Zendesk tickets |

Each layer registers its dependencies in its own `DependencyManager.cs`, called from
`Program.cs`.

### Layer rules — always enforce these

- **Domain** depends on nothing.
- **Application** depends only on Domain. It defines interfaces. Infrastructure and Persistence
  implement them.
- **Web** depends on Application and Domain. It contains no business logic.
- **Controllers and views are thin.** An action guards the request, calls a service, and returns
  a view or a redirect. Branching over Application types (for example `QuestionFlowConfig` or
  `RequestState`) belongs in an Application service, not in a controller or a private
  controller helper.

## Domain model

Read the linked doc before you change one of these areas. The full reference is
`docs/domain-model.md`.

- **CheckingWindow**: a period in which schools check data, with a window type and key stage.
  Its start and end dates are **derived** from its exercises. Do not set them directly.
- **CheckingExercise**: one activity inside a window, on its own dates. It is identified by its
  `Id`. Its kind (`ExerciseType`) is `PupilData`, `ResultsEnquiry`, or null for a display-only
  data share. A window holds at most one exercise of each kind.
- **Dataset slot** (`CheckingWindowDataset`): one supplier file that an exercise ingests. A slot
  can be optional or retired.
- **Release**: each clean validation run of an exercise publishes a release. Schools see the
  current release only. See `docs/checking-exercise-releases.md`.
- **ChangeRequest**: a school's request. `Status` is what the school did. `ProcessingStatus` is
  where the request is on its way to a Zendesk ticket. Only the documented code paths change
  `ProcessingStatus`. See `docs/rules-engine.md`.
- **RequestState**: the session state of a request in progress. See `docs/request-journey.md`.
- **PupilRecord**: the read model for supplier pupil JSON. Pupils are not in the database. They
  are one JSON file per school in blob storage.

Results enquiries and 16-19 windows: `docs/results-enquiry.md`, `docs/16-19-window-model.md`.

## Rules that are easy to break

### Data and storage

- **Never edit a migration that may have run anywhere.** EF tracks migrations by id, not by
  content. Add a new migration. To repair a schema that has drifted, make the new migration
  idempotent (for example `ADD COLUMN IF NOT EXISTS`).
- **Get every blob path from `CheckingExerciseBlobPaths`.** Do not build a path by hand. A
  prefix is a kebab-case slug, never an enum's `ToString()`. There is no default case: a new
  exercise type with no prefix throws.
- **Supplier field names stay at the edge.** Map them with `[JsonPropertyName]` or a schema's
  `x-ingress.source`. Never put a supplier column name in a C# property name.
- **Inclusion is the `INCLUDED` stamp.** Ingress decides it once. Code that reads pupils must
  not know a key stage's inclusion codes.
- **Who may be included is the `INCLUDABLE` stamp.** Ingress writes it from the schema's
  `x-ingress.includable` rule (#711). The Include journey reads the stamp, never a code.
- **The service is not live yet.** Do not write fallbacks for data written before a change.
  Treat missing new-format data as an error. (This does not change the migration rule above:
  that rule protects deployed databases.)

### Time

- **Only `ICheckingExerciseService` compares an exercise's dates with the clock.** It gets time
  from an injected `TimeProvider`. Controllers do not take a `TimeProvider`.
- **Store instants as UTC.** Show them in Europe/London time on school-facing pages only, through
  `LondonTime` (`Web/Extensions/LondonTime.cs`). Admin and operations pages show UTC.
- **Exception:** exercise and window start and end dates are UK wall-clock deadlines, not UTC
  instants. Do not convert them with `LondonTime`.

### Fail closed

When the code cannot decide, it shows nothing and allows no action:

- The storage resolver returns null when two exercises match equally.
- A `visibleWhen` that names an unknown condition hides the option.
- An exercise that is closed removes actions, never content.
- A rejected journey entry redirects to Check your pupil data with a message
  (`ClosedExerciseGuard`). It never returns 404.

### Admin pages

- **Every controller routed under `admin/` has a class-level `[RequireAdminSection(...)]`.**
  The fallback policy only requires a signed-in user, and every school user is signed in.
  `AdminRouteGateTests` fails for a controller without a gate.
- Create-flow controllers gate on `NewWindow`. Controllers that read or edit an existing window
  gate on `ManageWindow`.

### Content and configuration

- **A content block is written once per key.** `IContentBlockService.EnsureAsync` writes
  `defaultHtml` only when no block exists. To change content that is already seeded, use a new
  key.
- **Question flow files ship in the image.** They are
  `Web/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json`. Nothing writes them at run
  time. A change type appears for a window type only when its flow file exists.
- **Register GOV.UK Frontend with `AddCpdGovUkFrontend()`**, never `AddGovUkFrontend()`.
- **Package versions go in `src/Directory.Packages.props`.** Commit the `packages.lock.json`
  files that `dotnet restore` changes. CI restores in locked mode.

## Accessibility

WCAG 2.2 AA and the Public Sector Bodies Accessibility Regulations 2018 apply. Read
`docs/accessibility.md` before you add or change a view. The main rules:

- Each page sets `ViewData["Title"]` to the same words as its one `<h1>`. Do not add an
  "Error: " prefix in a view. The layouts do it.
- Each form control has a real `<label for>`. `aria-describedby` names only ids that the view
  renders.
- A link styled as a button has `role="button" draggable="false" data-module="govuk-button"`.
- No `href="#"` links. Use `PaginationWindow.Build` for pagination.
- A link or button whose text repeats on a page has a visually hidden suffix that names its row.
- Breadcrumbs render before `<main>`.

Put a test in `AccessibilityAuditViewTests` for each rule that lives in markup.

## Testing

- xUnit and NSubstitute. Unit tests mock at the service boundary. Test navigation and domain
  logic through service tests, not controller tests.
- Code changes get unit tests.
- **Do not test dev seed data.** Dev seeders (`DevDataSeeder`, `SeedCheckingWindows`,
  `Web/Seeding/*`) are data. Put `[ExcludeFromCodeCoverage]` on each new dev-only seeder.
  Seeders that run in production keep their tests.
- **Test that config files are valid, not what one journey contains.** Do not pin one flow's
  pages, wording or rule dates. Generic tests check every flow file and `rules.json`
  (`QuestionFlowValidatorAlignmentTests`, `SeedRulesValidationTests`). Check journey content by
  hand in the browser.

## Writing style

Write docs, code comments, commit messages and PR descriptions in ASD-STE100 Simplified
Technical English:

- Short sentences: at most 20 words for a description, 25 for a procedure.
- Active voice and simple present tense. One instruction in each sentence.
- Words with one meaning. No noun clusters of more than three words. Define jargon.

## Keeping documentation current

Update documentation in the **same pull request** as the change that makes it true.

| If you change | Update |
| --- | --- |
| A project, a layer rule, or a command | This file |
| An entity, a feature or an Application service | `docs/domain-model.md` |
| Exercises, slots or releases | `docs/checking-exercise-releases.md` |
| Blob paths | `docs/blob-storage.md` |
| Question flow format | `docs/question-flows.md` |
| Dev seed data | `docs/LocalDevelopement/dev-seed-data.md` |
| A view's accessibility | `docs/accessibility.md` |

A significant architecture decision gets an ADR in `docs/adrs/`, with the next number.

If this file is wrong or out of date, fix it in the same pull request.

## Before you say a task is done

1. Run `dotnet build src/DfE.CheckPerformanceData.slnx`.
2. Run `dotnet test src/DfE.CheckPerformanceData.slnx --filter "FullyQualifiedName!~E2ETests"`.
3. If you changed a page or a journey, run the E2E suite or check the page in a browser.
4. Check the table in [Keeping documentation current](#keeping-documentation-current).

## Git and pull requests

- Branch off `main`. Name the branch `{issue-number}-{short-description}`.
- Use short, sentence-case commit subjects.
