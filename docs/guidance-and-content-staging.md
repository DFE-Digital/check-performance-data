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
- **Zipped on the wire.** The download is a single-entry `.zip` (`ContentBundleArchive`) containing `bundle.json`. Bundles are repetitive JSON, so this is roughly an order of magnitude off both the download and the subsequent upload into the target environment. Import sniffs the leading bytes rather than the file extension, so a plain `.json` bundle from an earlier release still imports, and the decompressed size is bounded as it is read so a small hostile archive cannot expand to fill memory.
- **Bounded version history.** By default each page exports its most recent `ContentExportSelection.DefaultMaxVersionsPerNode` versions plus whichever version is currently live — the live one is resolved from publish windows, so it is not necessarily the newest and is retained explicitly. Both export forms carry an **Include full version history** checkbox (`MaxVersionsPerNode = null`) for a wholesale environment migration.
- **Preview state lives server-side.** `POST …/preview` stores the parsed bundle in `content_staging_sessions` and returns only the session id to the browser; `POST …/import` quotes it back, and the row is deleted once the import succeeds (kept on failure, so a retry needs no re-upload). Sessions expire after `ContentStagingSessionDefaults.Lifetime` and expired rows are swept whenever a new preview is stored.
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

## Content shipped with the service

Some content has to be in every environment: the service's own help pages, for example. It
is kept in the repository as content-staging bundles and imported by the application
itself at start-up, so a deployment puts it in place and nobody has to import it by hand.

The files are in `src/DfE.CheckPerformanceData.Web/Data/Import`:

| File | What it is |
|---|---|
| `manifest.json` | The list of bundles to import, in order |
| `cms-guide.json` | The in-app guide *How to use the CMS*. Generated: see `docs/user-guides/README.md` |
| `development-testing.json` | The pages the automated browser tests navigate to, and the *Development testing* folder they sit in. It includes a *Widgets* folder with a test page for each widget (*Heading test page*, *Search test page* and so on) that shows the widget set up in several ways |

### The manifest

```json
{
  "imports": [
    {
      "file": "cms-guide.json",
      "environments": [ "Development", "Review", "QA", "Preproduction", "Production" ],
      "existing": "replaceOlder"
    },
    {
      "file": "development-testing.json",
      "environments": [ "Development", "Review" ],
      "existing": "replace"
    }
  ]
}
```

Each entry names one bundle in the same folder. They are imported from top to bottom, so
a bundle whose pages hang off pages in another bundle goes after it.

| Property | Values | What it does |
|---|---|---|
| `file` | A file name in the folder | The bundle to import. A `.json` bundle, as exported from *Content staging import/export* and unzipped. |
| `environments` | A list of `Development`, `Review`, `QA`, `Preproduction`, `Production` | The environments to import the file into, matched against `ASPNETCORE_ENVIRONMENT`. A file is imported nowhere it is not named. An entry with no list is reported as an error and skipped. |
| `existing` | `keep` (the default) | Adds what is missing and leaves everything already there alone. |
| | `replace` | Overwrites what is there on every start-up. For content nobody edits, which must always be exactly as shipped. |
| | `replaceOlder` | Overwrites a page only if it was last changed before the bundle's `exportedAtUtc`. A newer bundle replaces older pages. A page edited since is kept until a newer bundle is released. Content blocks are added if missing and otherwise kept. |

### Adding a file

1. Build the content in a local environment and export it from *Content staging
   import/export*. Unzip the download to get the `.json` bundle.
2. Put the bundle in `Data/Import`.
3. Add an entry for it to `manifest.json`, naming the environments it is for. Content
   that exists for testing, for example, would name `Development` and `Review` only.

A bundle's top-level pages need a parent that exists in every environment. The four
sections (Support, Wiki, Help and Guidance) are created before the import runs and have the
same id everywhere, so pages beneath them import cleanly. A bundle can also bring a
top-level page of its own, as `development-testing.json` does: put it first in the file and
leave out its `parentId`.

A bundle records whether each page is shown in the site menus (`showInMenu`), so a page
hidden from the menus where it was exported arrives hidden. A bundle written before this
was recorded says nothing, and importing it leaves menu visibility as it is.

### Importing a file on request

*Seed sample CMS pages*, in the Test data menu, also imports `development-testing.json`
in any environment except Production, whichever environments its manifest entry names.
That is for environments such as QA, where the test pages are useful now and then but
should not arrive by themselves.

### What the import will and will not do

- It runs each time the application starts, and imports each file into the environments
  its entry names.
- Each bundle goes through the same importer as an upload, so the same validation and
  sanitisation apply.
- A page that someone has deleted stays deleted, along with any pages the bundle would put
  beneath it. Restore it from *Deleted pages* to get it back.
- With `replaceOlder`, replacing a page marks it as changed at the time of the import. The
  next start-up therefore finds nothing to do, and nothing is rewritten on every restart.
- The page editors show a warning at the top of any page that a `replace` or
  `replaceOlder` file will overwrite in that environment, so nobody loses work by
  editing one.
- A file that is missing, cannot be read or fails to import is logged and skipped. The
  files after it are still imported, and the application still starts. Look for
  `Content import:` in the application log.

`ShippedContentImportTests` fails the build if the manifest lists a file that is not there,
if a file in the folder is not listed, if an entry names an environment that does not
exist, or if two files contain the same page.
