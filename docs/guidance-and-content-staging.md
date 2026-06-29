# Guidance pages and content staging (`288673_guidance-landing-page` branch)

Two features ship together on this branch:

- **Guidance pages** (288673) — a `/guidance` landing page and the long `/guidance/2026-ks4-june-checking-exercise` page, both built from CMS content blocks against a section manifest in code.
- **Content staging** (289182) — export/import of wiki pages and content blocks between environments as a schema-versioned JSON bundle, with GUID identity and an import preview.

Plus supporting work: content-block search, custom SEO slugs, and two additive migrations.

---

## What's on the branch

### Guidance pages

- **Two MVC pages composed from CMS content blocks, not wiki pages.** `GuidanceController` exposes `GET /guidance` (`Index()`, no view model) and `GET /guidance/2026-ks4-june-checking-exercise` (`Ks4June2026()`, passes the static `GuidancePage.Ks4June2026`). Chrome/header/footer are the shared `_Layout` — untouched.
- **A section manifest in code is the single source of truth.** `GuidancePage.Ks4June2026` is a `GuidancePage` record holding the page's block keys plus an ordered `IReadOnlyList<GuidanceSection>`. The manifest — not the CMS — owns the page's structure: section order, anchors, heading levels, and which content-block key fills each slot. Editors change the *content* of each block; they cannot change the page's skeleton.
- **Headings and body are separate editable blocks.** Each section is rendered as a `<section id="{Anchor}">` wrapper containing an `EditableTitle` (the heading block) and an `EditableContent` (the body block). The template owns the `id` attribute so the HTML sanitiser never sees it — anchors can never be stripped or rewritten by an editor.
- **The contents nav is one editable block, not auto-generated at runtime.** `GuidancePage.NavBlockKey` points at a single block holding the side-nav markup (canonical **MoJ side-navigation**, with H3 sub-sections nested under their H2). It is rendered raw. The nav block's HTML is generated offline from the same section list (bootstrap tooling, outside the repo) and seeded like any other block, so the nav and the manifest stay in lockstep without a per-request build.
- **Landing page** (`Index.cshtml`) is similarly composed from `guidance-landing-*` blocks: search box, sign-in, email-alerts, and card grids whose cards deep-link into KS4-page sections.

### Content staging (CMS export/import)

- **Schema-versioned JSON bundle.** `ContentBundle` carries `$schema = "cpd-content-v1"` (`ContentBundle.CurrentSchema`) and `SchemaVersion = 1` (`CurrentSchemaVersion`), plus optional `ExportedAtUtc` / `ExportedBy` metadata and the `WikiPages` / `ContentBlocks` collections. Serialised camelCase, indented (diff-friendly), enums as strings, nulls omitted (`ContentStagingJson.Options`).
- **GUID identity, decoupled from slug/key.** Every wiki page and content block carries a stable `ContentId` GUID (new columns — see migrations). Import matches **by GUID, never by slug or key**, so a page renamed or re-slugged in one environment still updates the right row in another. An unknown id (or `Guid.Empty`) means "create new".
- **Selective or whole-environment export.** `GET /admin/content-staging/export` downloads everything; `GET …/select` lists the `ContentCatalog`, and `POST …/export` exports the ticked pages/blocks **plus all ancestor pages** (so a child never exports without its parent chain).
- **Import preview with per-collision decisions.** `POST …/preview` parses an uploaded bundle and returns a dry-run `ContentImportPreview` (per-item `Exists` / `ExistingDescription` / `ParentMissing`, plus `NewCount` / `CollisionCount` / `BlockedCount`). The reviewer picks an import mode and can override it per item, then `POST …/import` applies it.
- **Three import modes** (`ContentImportMode`): `Skip` (add missing only, leave existing untouched), `Replace` (overwrite existing — blocks record a new version), `Fail` (abort the whole import if any collision remains unresolved). The service guards up front and throws `ContentImportConflictException` if a `Fail`-mode collision is left without a per-item `Skip`/`Replace`.

### Content-block search

- `ContentBlockSearchService.SearchAsync` (wired into `/help/search`) searches content blocks alongside wiki pages. It rejects terms under two characters, over-fetches to de-duplicate by destination URL, builds a `<mark>`-highlighted snippet (everything HTML-encoded except the mark tag), and resolves each hit to a clickable, anchored URL.
- `ContentBlockLocations.Resolve(key)` is the static block-key → page-URL + anchor map that makes deep-linking work: `home-*` → `/`, `guidance-landing-*` → `/guidance`, `guidance-ks4-2026-<section>` → `/guidance/2026-ks4-june-checking-exercise#<section>` (a `-heading` suffix resolves to the same anchor). Unmapped keys return null and never surface in search.

---

## The KS4 page manifest

`GuidancePage` (record):

| Field | Purpose |
|-------|---------|
| `Title` | Page heading text |
| `TitleBlockKey` | Editable `Title` block for the main heading |
| `IntroBlockKey` | Editable lede/intro block |
| `PublishedBlockKey` | Editable "published / last reviewed" blue callout |
| `NavBlockKey` | The single editable block holding the side-nav HTML |
| `Sections` | Ordered `GuidanceSection` list — the page skeleton |

`GuidanceSection` (record):

| Field | Purpose |
|-------|---------|
| `Anchor` | Stable, slug-safe id; rendered as the `<section id>` (template-owned, never sanitised) |
| `NavTitle` | Heading text, also the nav link label |
| `Level` | `2` top-level, `3` nested (default `2`) |
| `HeadingBlockKey` | Editable `Title` block for the section heading |
| `BlockKey` | Editable content block for the section body |
| `HeadingCssClass` *(computed)* | `govuk-heading-l` (L2) / `govuk-heading-m` (L3) |
| `HeadingElement` *(computed)* | `h2` (L2) / `h3` (L3) |

The KS4 manifest is **31 sections** in the `guidance-ks4-2026-` namespace: 17 level-2 sections and 14 level-3 sections nested under "Pupil removal reason". Block keys are derived from the anchor: `{namespace}{anchor}` for the body, `{namespace}{anchor}-heading` for the heading — so adding a section is a one-line manifest edit plus seeding the two new blocks.

---

## Database

Two additive migrations (apply clean on a fresh DB and over current main):

- **`20260626070144_ContentId_PagesAndBlocks`** — adds a `ContentId` `uuid` column to `WikiPages` and `ContentBlocks`, each defaulting to `gen_random_uuid()` with a unique index (`IX_WikiPages_ContentId`, `IX_ContentBlocks_ContentId`). This is the cross-environment identity content staging matches on.
- **`20260626084300_ContentBlock_LastSeenPath`** — adds nullable `LastSeenPath` (text) and `LastSeenAt` (timestamptz) to `ContentBlocks`. The editable view components record the request path each time a block is rendered, so the content-blocks admin page can show *which page uses this block* — essential for dynamically-keyed blocks that a code scan can't find.

---

## Content is CMS data, not migrations

The guidance pages render their **structure** from the manifest, but their **content** lives in CMS content blocks, which are not part of any migration or auto-seeder. A fresh environment (including a PR review app) renders the page skeleton with "Content to be added" placeholders until the blocks are seeded — this is expected, not a bug. Seeding is done out-of-band via the controller surface (`POST /content-block/save`), the same path content staging import uses.

(Separately, the admin **rules editor** gets its data from the `rules-config` blobs, which the web app self-seeds on startup — see [E2E-Playwright.md](E2E-Playwright.md) under "Test data isolation".)

### Check the data directory 
For an example file to import to populate the CMS

---

## Testing

- Unit — guidance section/manifest mapping, content-block search (HTML-encoding + `<mark>` safety), slug generation, content-block service/controller.
- Integration (Testcontainers Postgres) — content-staging export/import round-trip and both migrations on a real database.
- E2E — guidance landing + KS4 structure and the content-staging admin pages (export / select / content-blocks), including anonymous-redirect guards.
