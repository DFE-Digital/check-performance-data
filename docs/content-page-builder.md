# Content page builder (`content-page-builder` branch)

A ground-up rewrite of the CMS around a single unified page tree. Replaces the old `ContentPage` / `GuidancePage`-in-code / wiki-only split with one entity — `PageNode` — that every page on the public site now resolves through. Content pages are authored as a **widget tree** (heading, rich-text, cards, page-nav, search, …) inside a **region-based layout**, published through **versions**, and edited from a **page-tree admin** with drag-and-drop reordering, right-click actions, and a live left-nav that mirrors the site.

This document is the dev-onboarding overview. Feature docs that predate this branch (`CMS-search.md`, `guidance-and-content-staging.md`) still describe the pieces that survived; anything they contradict, this branch wins.

---

## What ships on this branch

### The unified page tree

- **`PageNode` + `PageNodeVersion` tables.** Every page — folders, wiki pages, and the new widget-based content pages — is a row in `PageNodes` with a `PageType` (`folder` / `wiki` / `content`) and a `Path` (the URL, computed from `Segment` + ancestor path). Versioned content lives in `PageNodeVersions` with per-version publish windows (`StartDate` / `EndDate`), a `Content` text column (widget JSON for content pages, sanitised HTML for wiki pages), and decimal draft version numbers that promote to whole integers on publish.
- **Catch-all route resolver.** `PageController` binds `/{*path}` with `Order = int.MaxValue`, so any URL not claimed by a real controller route falls through to a PageNode lookup. Assertion test (`PageControllerRouteOrderTests`) guarantees the catch-all can never shadow app routes. Missing pages render a CMS-authored `/help/not-found` page with HTTP 404 (falls back to bare `NotFound()` if that page is deleted, so the resolver never returns nothing).
- **Retired the old ContentPage stack.** `ContentPages` and `ContentPageVersions` tables + the whole ContentPage web/service/persistence layer are gone (`Remove ContentPage …` commits). Old routes redirect via the page tree. `GuidanceController` is retired too — `/guidance` now resolves to a PageNode.
- **Soft delete + reorder.** Deletes mark `DeletedDate`; deleted pages surface under `/admin/pages/deleted` (moved off the wiki-flavored `/help/deleted` route). Up/down reorder is a `MoveTo` tree op with sibling-index maths that survives moves within the same parent.

### Content-page authoring model

- **Widget tree stored as JSON.** A content page's `Content` is a JSON array of nodes. Top-level is one or more `region` nodes; each region carries `columns`, each column an ordered list of `widget` nodes. Round-tripped through `ContentPageJson` (System.Text.Json), addressed by dotted paths for insert/remove/move (`ContentTreeEditor`).
- **Eight widgets ship.** `Heading` (h1–h6, auto-anchored so the auto side-nav renders), `RichText` (TinyMCE-authored, sanitised via `Ganss.Xss` with the GOV.UK-safe allow-list), `Divider`, `Card` (title, body, optional image, optional link — equal card heights across wrapped rows), `SummaryList`, `Published` (last-reviewed callout), `Search` (with an optional `scope` prop that constrains results to a subtree — e.g. every KS4 split page searches within `/guidance`), and `PageNav` (renders the direct-children nav from the tree). Widgets live under `Views/Shared/ContentPages/Widgets/` as Razor partials.
- **Region layouts map to the GDS grid.** Regions carry a `layout` name (`one-column`, `two-thirds-one-third`, `one-half-one-half`, etc). `RegionLayouts` maps each to the equivalent `govuk-grid-column-*` classes so authored content lines up with the design system without editors having to know CSS.
- **PageNode properties.** Beyond `Title`, pages carry an editable `Subtitle`, a `PageName` (short label for menu use), a `Segment` (URL slug), and a boolean `ShowInMenu` (default true) that hides the page from public side-nav + search widgets when off. Menu / search widgets skip `folder`-typed pages and pages with no live version.
- **Content-block auto-provisioning.** The `EditableContent` view component seeds a block on first render if none exists for the given `key`. Falling back to `defaultHtml` in code lets a template ship with useful default content that becomes editable in `/admin/content-blocks` the first time the page is viewed — no migration required.

### Admin CMS (`/admin/…`)

- **Live page tree in the admin left-nav.** The full page tree renders in the sidebar on every admin screen, with a localStorage-backed collapse state, an "open by default" reset for stale states, and a "highlight + expand branch" pass for the currently-loaded node.
- **Content blocks in the same tree.** A parallel tree under `/admin/content-blocks` mirrors the pages tree and lists the blocks used on each page. `LastSeenPath` / `LastSeenAt` on `ContentBlocks` (from the guidance branch) drives the location column.
- **Right-click context menu on every tree node.** Edit, Versions, New child page, Delete — each with an icon, each honouring modifier keys (right-click on Edit / Versions / New child opens in a new tab).
- **Drag-and-drop reorder + reparent.** HTML5 DnD with a **placeholder slot** that inflates between siblings so the drop target is unambiguous (see `admin-page-tree-dnd.js`). Client-side sort-index maths accounts for the source being filtered out of the sibling list when it's in the same parent as the target. Anti-forgery header wired as `X-XSRF-TOKEN` for the JSON move POST. After a move, the browser lands on the moved page so its branch stays open.
- **Edit page with tabs.** Content editor, Properties, Versions. Content editor: widget palette sorted alphabetically, drag-and-drop widget reordering (including into regions), inline "Add content here" slots between widgets, per-widget forms (rich-text uses TinyMCE), light-orange region backgrounds so the layout is visible. Properties: editable Title, URL segment (via rename modal), Subtitle, PageName, ShowInMenu, PageType. Versions: full history with Save / Publish draft / Unpublish / schedule window / restore.
- **Version numbers.** Whole integers on released versions, decimal fractions on drafts, `Past` status tag for expired-window versions, and the edit page's status badge is reconciled with the actual publish state (drafts show as drafts even when a live version exists). Publish window can be pre-filled but the Publish button ignores it and goes live now.
- **Pages admin header.** H1 shows the current page's Title (not "Pages"). Action toolbar (New child page, Delete, View, Search) is a compact icon row stacked immediately under the title. Empty-state green "New child page" button removed in favour of the icon-row entry. Search widget hidden when the section has no children and no active query.
- **In-page Edit shortcut on published pages.** Logged-in editors/admins see a floating pencil chip in the top-right of any published content page. Clicking opens `/admin/pages/{id}/edit` in a **new tab** (so the public view stays in place). Rendered only for `Editor` / `Admin` roles; invisible to end users.
- **Post-delete navigation.** Deleting a page from the admin now redirects to the deleted page's parent (falls back to `/admin/pages` at the root) instead of hanging on the just-deleted URL.
- **Deleted pages view.** Lives under `/admin/pages/deleted` — the old `/help/deleted` wiki-flavoured route is gone.

### Unified search

- **`/search?q=…`** returns pages and content blocks in one ranked list. Postgres `ILIKE` for both (title + body for pages, key + rendered HTML for blocks). Results de-duplicate by URL, snippet-highlight matches with `<mark>`, and every card is one clickable link — no type-filter checkboxes, no bottom URL row.
- **Search widget scope.** The `Search` widget accepts an optional `scope` prop that pins the search to a subtree — the KS4 split pages set `scope="guidance"` so their bottom-of-page search only surfaces guidance results.
- **Search results exclude folder-typed pages** and pages with no live version, matching the menu widget so the two never diverge.

### KS4 split (public `/guidance`)

- The 22222222-… KS4 monster page has been broken into **13 sibling pages under `/guidance`**, one per top-level section of the source docx spec. Titles moved into the right-hand column above content (matching the split-page comp), left column is the MoJ side-navigation, breadcrumbs anchored to the top of both columns for a clean lock-up.
- Each split page ends with a **`Divider` immediately above a scoped `Search` widget** so it's visually clear where the page body ends and the search hand-off begins.
- Anchors on copied headings are regenerated on the way in so the auto side-nav renders (anchorize pass in the copy tool).
- Links on the `/guidance` landing page have been updated to point at the corresponding split page rather than the monster page.

### Roles + system administration

- **Role-based section access grid** at `/admin/system-administration/roles`. One column per role, one row per admin section. `Admin` is locked to every section (checkboxes rendered `disabled` alongside a hidden `true` input so the server still receives the value on save). New roles can be registered with an empty grant and persist (a `__registered__` sentinel row keeps the role in the schema until real grants are added).
- **Native `<input type="checkbox">`, not `govuk-checkboxes__input`.** The GDS class visually-hides the input and relies on a label, which doesn't fit a compact grid — the grid uses plain natives with a light custom style.

### Analytics (`280389-analytics`, merged into this branch)

- DfE Analytics `web_request` middleware and organisation enricher wired into `Program.cs`.
- Custom events: `journey_funnel`, `evidence`, `validation_error`, `pupil-search`, and a `rules-engine` decision-mix event that fires from the rules engine worker.

### Footer content block

- The `Support and guidance` section in `_Layout.cshtml` — heading, intro paragraph, and the Privacy / Cookies / Accessibility / Guidance link row — is now a single `EditableContent` block keyed `footer-support-and-guidance`. Editors update all four labels + the intro text from `/admin/content-blocks` without a redeploy.

---

## Database

Migrations applied on this branch (apply clean on a fresh DB and over current `main`):

- **`PageNode` + `PageNodeVersion` tables** — the unified page tree and per-page versioned content. Filtered unique index on `Path` where `DeletedDate IS NULL` so a soft-deleted path can be reused by a new page (positive test in `PageNodeRepositoryTests`).
- **`Drop ContentPages / ContentPageVersions`** — the old tables are removed once the code that reads them is gone.
- **`ShowInMenu` column** on `PageNodes` (default `true`) driving the public menu / search filter.
- **`PageName` column** on `PageNodes` for the short label used in menus.
- **`Subtitle` column** on `PageNodes` for content-page subheadings.
- **`MinorVersion` column** on `PageNodeVersions` for decimal draft version numbers.
- The guidance branch's `ContentId` and `LastSeenPath` / `LastSeenAt` columns on `ContentBlocks` are still in play.

---

## Route resolution order

1. Real controller routes (attribute + conventional) win.
2. The catch-all `PageController.Show([HttpGet("/{*path}", Order = int.MaxValue)])` picks up anything unclaimed.
3. It resolves the path to a `PageNode`. If the node is a `folder`, it renders a child index; otherwise it looks up the live version.
4. If there's a live version, `content` renders through the widget template, `wiki` through the sanitised-HTML template.
5. If there's no live version, `Editor`/`Admin` roles see a **draft preview** of the working/latest version; everyone else gets the CMS-authored 404 page.

---

## Testing

- **Unit** — page-tree services, widget-tree edit operations, region-layout mapping, version-window logic, path validator (route-collision guard), HTML sanitiser allow-list, block auto-provisioning.
- **Integration** — Postgres via Testcontainers. Page-tree CRUD, publish-window resolution over time, soft-delete + path re-use, cross-version scheduling, `MoveTo` sibling-index maths, and content-staging bundle round-trip against the new tree.
- **E2E (Playwright)** — page-tree admin drag-and-drop, right-click context menu, widget-editor drag-into-region, publish → view flow, unified search page, KS4 split navigation, in-page Edit shortcut.

---

## Local dev

Unchanged from before: `docker compose --project-name check-performance-data up -d` from `check-performance-data/`. Web at [http://localhost:8080/](http://localhost:8080/); admin at [http://localhost:8080/admin](http://localhost:8080/admin). `DOTNET_NUGET_SIGNATURE_VERIFICATION=false` is still required for local `dotnet build` because of the upstream Refit signing-cert issue.

Anything that touched view code needs a `docker compose --project-name check-performance-data build web --no-cache` if you see stale HTML after a rebuild — the Razor compile step can hold onto a stale layer under BuildKit.
