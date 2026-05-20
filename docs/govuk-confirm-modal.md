# GOV.UK confirm modal (`<govuk-confirm-modal>`)

A reusable, focus-trapped, GOV.UK Design System-styled confirmation dialog used for destructive actions in the admin/editor surfaces (delete page, revert version). Replaces inline `confirm()` JavaScript dialogs.

API reference (attributes, DOM order, JS contract): see [`src/DfE.CheckPerformanceData.Web/TagHelpers/README.md`](../src/DfE.CheckPerformanceData.Web/TagHelpers/README.md). This document covers the design rationale and how to use it across the codebase.

## What it is

A Razor TagHelper that wraps a real HTML form in a native `<dialog>` element opened via `.showModal()`. The dialog body uses canonical GDS components throughout — heading phrased as a question, `govuk-warning-text` for the irreversible-action warning, link-style Cancel per GDS button-page guidance, and the red `govuk-button--warning` for the confirm action.

Rendered example (delete-page modal on a wiki node):

> **Are you sure you want to delete 'About us' and all child pages?**
>
> ⓘ Warning &nbsp; This action cannot be undone.
>
> [ **Yes, delete page** ] &nbsp; Cancel

The trigger button lives in the parent view; the dialog markup lives inside the TagHelper invocation; the JS module (`wwwroot/js/confirm-modal.js`) auto-binds them via `[data-confirm-trigger]`.

## Why we built it

The official GOV.UK Design System ships **no** modal/dialog component. Its canonical destructive-action pattern is a server-rendered interruption page ("Are you sure you want to delete X? [Yes, delete] [Cancel]"). We deliberately diverge from this guidance — and **only** in the modal-vs-confirm-page choice. Inside the modal, the rendered markup conforms to canonical GDS patterns.

Three reasons drove the deviation:

1. **Audience is admin/editor only.** The destructive-confirm modal is reached only by authenticated authors editing the help/wiki content or content-block versions. School readers — the public service audience for GOV.UK Service Standard purposes — never see it.
2. **Bulk operations are common.** Cleaning up old wiki pages and reverting versions during editorial maintenance are bulk activities. A full-page interruption + redirect cycle for every confirmation slows that work and trains admins to click through interruption pages without reading them.
3. **In-page modal keeps context.** The modal surfaces the confirmation next to the originating row, letting the admin verify they clicked the right thing without losing place.

The deviation is logged for the GOV.UK Service Standard audit. Inner-markup conformance narrows the audit scrutiny target to the component-level decision, not the markup.

## Where it's used today

Three sites, all admin-only (`?edit` mode or admin version-history pages):

| View | Action | Modal id pattern |
|------|--------|------------------|
| `Views/Help/_WikiTree.cshtml` | Delete a wiki page (cascading to children) | `confirm-delete-{nodeId}` |
| `Views/Help/Versions.cshtml` | Revert a wiki page to a previous version | `confirm-revert-wiki-{versionId}` |
| `Views/ContentBlock/Versions.cshtml` | Revert a content block to a previous version | `confirm-revert-cb-{versionId}` |

Per-site copy is consistent:

| Site | Heading | Warning | Confirm label |
|------|---------|---------|---------------|
| Delete page | `Are you sure you want to delete '{title}' and all child pages?` | `This action cannot be undone.` | `Yes, delete page` |
| Revert wiki version | `Are you sure you want to revert to version {N}?` | `This will replace the current published content.` | `Yes, revert` |
| Revert content-block version | `Are you sure you want to revert to version {N}?` | `This will replace the current published content.` | `Yes, revert` |

A regression-guard test (`tests/.../Web/TagHelpers/ConfirmSweepTests.cs`) keeps inline `confirm()` from creeping back in: three `Assert.DoesNotContain("onclick=\"return confirm(")` checks, three `Assert.Contains("<govuk-confirm-modal")` checks. Project-wide ripgrep for `onclick="return confirm(` should always return zero matches under `Views/`.

## When to use it

Adopt `<govuk-confirm-modal>` for any new destructive action behind admin authentication. The reusable contract is intentionally narrow — it expects:

- A trigger button somewhere in the parent view
- A real form action on the destination controller (the modal owns the form, not the trigger)
- An `@Html.AntiForgeryToken()` and any hidden inputs the action needs, passed as TagHelper child content
- A heading phrased as a question
- A one-sentence warning describing what the user is about to do irreversibly

If your action is **not** behind admin authentication — i.e. anonymous or low-privilege users can reach it — prefer the canonical GDS server-rendered confirm page instead. The deviation rationale only holds for admin-only audiences.

If your action is **not** destructive (a non-warning confirm), the TagHelper supports `destructive="false"` which omits the `govuk-warning-text` component and uses a standard primary button. This variant has no shipped callsite today but is wired in.

## Security considerations

| Concern | How it's handled |
|---------|------------------|
| XSS via TagHelper attribute output | Every string attribute (Title, WarningText, Body, ConfirmLabel, Id, FormAction, FormMethod) routes through `WebUtility.HtmlEncode` inside `GovukConfirmModalTagHelper.ProcessAsync`. Unit-test guard: `ProcessAsync_TitleAndBody_AreHtmlEncoded` — `Title="<evil>"` produces `&lt;evil&gt;` literally. |
| CSRF on the destructive POST | Modal owns the form. Always include `@Html.AntiForgeryToken()` as TagHelper child content. Destination controller actions carry `[ValidateAntiForgeryToken]`. |
| Open redirect via `form-action` | `form-action` is server-set in the parent Razor view from trusted model data — never user-influenced. The TagHelper does not validate `form-action`. Don't pass user input to it. |
| Clickjacking / cross-window overlay | App-level `X-Frame-Options` / `Content-Security-Policy` are the correct mitigation. Native `<dialog>` top-layer rendering doesn't change the picture. |

## Browser support

Native `<dialog>` + `.showModal()` + `::backdrop` is "Baseline Widely available" since March 2022 (Chrome 37+, Firefox 98+, Safari 15.4+). The component is admin-tooling-only — JavaScript is a hard requirement, no progressive enhancement fallback ships.

## How to test it

### Unit (TagHelper rendering, sweep guard)

```sh
cd check-performance-data
dotnet test tests/DfE.CheckPerformanceData.UnitTests \
  --filter "FullyQualifiedName~Web.TagHelpers"
```

Covers:

- `GovukConfirmModalTagHelperTests` — 7 `[Fact]`s pinning the rendered DOM contract, including XSS encoding
- `ConfirmSweepTests` — 6 `[Fact]`s guarding against re-introduction of inline `confirm()` and confirming each swept site invokes the TagHelper

### E2E (open / close / focus-trap / submit)

```sh
make test-e2e-fast
```

Native E2E suite, no visual regression. The 8 `ConfirmModalTests` `[Fact]`s in `tests/.../E2ETests/Wiki/ConfirmModalTests.cs` cover:

- Trigger click opens the dialog and focuses Cancel
- Esc closes, focus returns to trigger
- Backdrop click closes
- Cancel link click closes (no navigation, no scroll)
- Confirm submits and the controller acts
- Tab cycle stays inside the dialog
- Warning icon has GDS styling
- Modal chrome (button colour, backdrop dim) matches GDS

### Visual regression (Linux baseline)

```sh
make test-e2e
```

Runs the full suite inside `mcr.microsoft.com/playwright/dotnet:v1.59.0-noble`. The single `ConfirmModalVisualTests.OpenDestructiveModal_MatchesSnapshot` `[SkippableFact]` compares the live render against `tests/.../E2ETests/Snapshots/linux-chromium/confirm-modal-open-destructive.png`. Skips on Windows/Mac.

### Manual

In a dev session:

```sh
docker compose --profile web --profile database --profile storage up -d
```

Then `http://localhost:8080/help/{any-page-slug}?edit` → click Delete on a node, exercise the modal. Tab cycling, Esc, backdrop, Cancel, and Confirm all work without reload.

## Files at a glance

| Path | Role |
|------|------|
| `src/.../Web/TagHelpers/GovukConfirmModalTagHelper.cs` | The TagHelper class — encoding, dialog markup, button-group composition |
| `src/.../Web/TagHelpers/README.md` | API reference (attributes, DOM order, ARIA contract, JS module dependency) |
| `src/.../Web/wwwroot/js/confirm-modal.js` | Trigger/cancel/backdrop/Tab-trap event wiring (auto-binds on DOMContentLoaded) |
| `src/.../Web/wwwroot/css/site.css` | Confirm-modal CSS block (dialog dimensions, `::backdrop` colour, button-group spacing, mobile clamp) |
| `src/.../Web/Views/_ViewImports.cshtml` | Registers the TagHelper assembly so `<govuk-confirm-modal>` resolves in any view |
| `src/.../Web/Views/Shared/_Layout.cshtml` | Loads `confirm-modal.js` site-wide via deferred script tag |
| `tests/.../UnitTests/Web/TagHelpers/GovukConfirmModalTagHelperTests.cs` | TagHelper rendering + XSS-encoding regression tests |
| `tests/.../UnitTests/Web/TagHelpers/ConfirmSweepTests.cs` | Sweep regression guard (no inline `confirm()`, every swept site uses the TagHelper) |
| `tests/.../E2ETests/Wiki/ConfirmModalTests.cs` | Playwright interaction matrix |
| `tests/.../E2ETests/Visual/ConfirmModalVisualTests.cs` | Linux-Chromium snapshot regression |
