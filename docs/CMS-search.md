# CMS search (`cms-search` branch)

Wiki search for the help CMS. Postgres full-text search over title + body, ranked, snippeted, paginated. Lives at `/help/search?q=...` and is wired into the help sidebar.

## What's on the branch

### Features

- **Full-text search over wiki pages.** New `SearchVector` column on `WikiPages` — Postgres `tsvector`, stored generated, indexed with GIN. Computed as `setweight(to_tsvector('english', Title), 'A') || setweight(to_tsvector('english', BodyPlainText), 'B')` so title hits outrank body hits at equal density.
- **Plain-text body column.** `BodyPlainText` is the tag-stripped, whitespace-collapsed version of `Content`. Written by the service layer on every create/update so the `tsvector` stays in sync without a trigger. The migration backfills existing rows once via a Postgres regex.
- **Search query parsing.** Uses `websearch_to_tsquery` (`EF.Functions.WebSearchToTsQuery`). Accepts user-typed input verbatim — quoted phrases, `OR`, `-exclude`, gibberish, unbalanced quotes — and never throws. Input is trimmed and length-capped at the service layer; no escaping needed.
- **Wildcard / prefix search.** Any `*` in the query switches the parser from `websearch_to_tsquery` to `to_tsquery`'s prefix-match mode (`pub*` → `pub:*`). Implemented as a separate repository method (`SearchPrefixAsync`) because `to_tsquery` is much stricter than `websearch_to_tsquery` — it raises syntax errors on punctuation, unbalanced operators, and empty input. The service layer (`BuildPrefixTsQuery`) sanitises every token to alphanumerics, attaches `:*` to any token containing `*`, and ANDs the tokens with `&` before they reach Postgres. Phrase quotes (`"..."`) and `-negation` are not honoured in wildcard mode — the parser is too strict for that to be safe. Asterisk-only input (`**`, `* * *`) sanitises to empty and returns the standard empty-query state.
- **Ranked results with snippet highlighting.** `ts_rank` orders by relevance, title is stable tie-break. `ts_headline` returns a snippet wrapped in `<mark>...</mark>` (15–25 words, one fragment). Rendered with `@Html.Raw` — safe because the source is `BodyPlainText`, not raw HTML.
- **Pagination.** `skip`/`take` on the controller; total count returned alongside the page.
- **Help sidebar + back link.** Search box on every `/help` page. Search results page has a back link to `/help`.
- **Postgres integration tests.** New `DfE.CheckPerformanceData.IntegrationTests` project running against real Postgres via Testcontainers. Covers the search path end-to-end (no mocks for the FTS bits) — including the `SearchPrefixAsync` wildcard path.

### Stemming and tokenisation

The `'english'` argument to `to_tsvector` and `websearch_to_tsquery` is a Postgres FTS configuration — Snowball English stemmer + the default English stop-word list + a basic punctuation-stripping tokeniser. Three things to know:

- **Stemming is two-way and automatic.** Both the stored `SearchVector` and the user's query get reduced to the same root form before they're compared. `school`, `schools`, `schooled`, `schooling` all index and search as `school`. `manage`, `manages`, `managed`, `managing`, `manager`, `management` all collapse to `manag`. So `?q=managing` will hit a page that only ever wrote "management", and vice-versa. This is desired behaviour, not a bug.
- **Stop words are dropped.** Words like `the`, `a`, `is`, `of`, `and` are stripped from both the index and the query. `?q=the schools` searches for `school`. `?q=the` alone returns nothing (after stemming + stop-word stripping the query is empty — `websearch_to_tsquery` returns an empty `tsquery`, which matches no rows). The empty/min-length error in `WikiService.SearchAsync` is a *pre-Postgres* check on raw input length; it doesn't catch this case, so a stop-word-only search produces a clean "no results" page rather than an error. That's intentional.
- **Stemming applies to titles AND body the same way.** Title weight (`A`) only changes the *rank*, not whether something matches. A page titled "Schools" matches `?q=school`, just with higher rank than a page that mentions schools only in its body.
- **What it does NOT do.** No synonyms (`pupil` ≠ `student`), no fuzzy/edit-distance matching (`scool` won't hit `school`), no language detection (everything is parsed as English even if the page is in another language). The `*` wildcard *does* give you prefix matching on whole tokens (`schoo*` → `school`, `schools`, `schooling`), but stemming already collapses most of that to the same root, so wildcards are mainly useful for partial-word lookups where the stem isn't enough (e.g. `pub*` → `published`, `publication`, `publish` — though the stemmer collapses the latter two anyway). If we ever need synonyms or fuzzy match, the `'english'` arg is the seam — swap in a custom configuration registered via `CREATE TEXT SEARCH CONFIGURATION` and the rest of the pipeline keeps working.

To see what stemming actually produces for a phrase, query Postgres directly:

```sh
docker compose exec postgres psql -U postgres -d cpd -c \
  "SELECT to_tsvector('english', 'The schools were managed effectively');"
-- ⇒ 'effect':5 'manag':4 'school':2
```

The leading `the`/`were` are gone (stop words), `effectively` → `effect`, `managed` → `manag`, `schools` → `school`. Numbers in the output are word positions, used by `ts_headline` to pick a snippet window.

### Bugs / gotchas fixed along the way

- **GOV.UK warning component rendered unstyled on seeded wiki content.** `Ganss.Xss` `HtmlSanitizer` with the default constructor strips `class`, `aria-*`, `role`, and `data-module` — even though `class` is documented as a default. Fix: explicit allow-list in `HtmlRenderingService.CreateSanitizer()`. XSS protection unchanged (`<script>`, `on*`, `javascript:` still blocked). Two regression tests in `HtmlRenderingServiceTests`.
- **EF translation broke when the FTS query was hoisted.** `EF.Functions.WebSearchToTsQuery("english", q)` *must* be written inline at every call site. Its `config` arg is `[NotParameterized]`, so storing it in a local variable forces client-evaluation and throws. Repeated three times in `WikiRepository.SearchAsync` for that reason — not a refactor opportunity.
- **Soft-delete leak risk.** `WikiPageConfiguration` has `HasQueryFilter(w => !w.IsDeleted)`. The search path deliberately doesn't call `IgnoreQueryFilters()` so deleted pages stay out of results. If anyone adds it later, deleted pages will be searchable.
- **Duplicate title/path silently appended a tick suffix.** Adding a page whose title produced an existing slug under the same parent used to succeed with a slug like `about-us-638492837412345678` — invisible to the user, ugly URL, and easy to create accidental near-duplicates. Now `WikiService.CreatePageAsync` throws `DuplicateWikiPageException` when `SlugExistsAsync(slug, parentId)` is true. `HelpController.Create` catches it, stashes the message + the typed title + the chosen parent in `TempData`, and redirects back to `/help` (preserving `?edit`). The Index view re-opens the "Add new page" disclosure, renders a GDS error summary, marks the title input with `govuk-input--error`, and pre-fills both inputs so the user just retypes the title. Soft-deleted pages don't block — the unique index and `SlugExistsAsync` both filter on `IsDeleted = false`.
- **Slug-path enrichment used to load every column of every page.** Earlier the search post-processing called `GetAllOrderedAsync()` to build the parent-walk lookup, which materialised full `WikiPageDto`s including `Content`. Now `IWikiRepository.GetSlugLookupAsync()` returns `WikiSlugLookupEntry` records (`Id`, `Slug`, `ParentId` only). Same correctness, three columns instead of seven plus a string blob. The remaining work — collapsing the count + select round-trips into a single `COUNT(*) OVER ()` window query — is a follow-up.
- **Static assets 302'd to OIDC for anonymous visitors.** The site-wide `AddAuthorization` `FallbackPolicy = RequireAuthenticatedUser` was applied to `MapStaticAssets` endpoints, so `GET /css/site.css` returned a 302 to DfE Sign-In for unauthenticated browsers. The `<link>` tag still attached but the response was a cross-origin opaque redirect — no styles ever applied. Most visibly, the wiki sidebar 280px lock had no effect on the rendered page. Fix: `app.MapStaticAssets().AllowAnonymous()`. Static CSS/JS/fonts contain no secrets and need to be reachable for the sign-in page itself.
- **CSS cache busting.** `_Layout.cshtml` uses `asp-append-version="true"` on `site.css` so browsers pick up stylesheet changes without manual cache clears. Added after the static-asset fix turned up: with caching now actually working, stylesheet edits were sticky in the browser.
- **Wiki sidebar expanded to full page width when empty.** With no seeded pages, the wiki navigation tree rendered empty and the sidebar's search input was the only child of the flex container — with no width constraint, it grew to fill the available row, dragging the sidebar to ~100% page width. Fix in `wwwroot/css/site.css`: explicit `min-width: 280px; max-width: 280px; flex-grow: 0` on `.wiki-sidebar`, plus `box-sizing: border-box` throughout, plus constraining `.wiki-search-form` and `.govuk-input` to `width: 100%` of their container. Layout now holds regardless of whether the tree has 0, 1, or 21 pages.
- **TinyMCE paste preserved formatting users didn't expect.** Editor had `paste_as_text: false` — pasting from Word or a styled web page brought spans, fonts, and inline colours into the wiki source. Flipped to `paste_as_text: true` in `_WikiEdit.cshtml`. Plain text on paste, styling comes from the GDS classes the editor actually offers.

## Code review fixes (CR / WR / IN)

A code review of the cms-search branch surfaced 12 issues across security and quality. All were resolved on the branch — summarised here so the doc captures *why* certain code looks the way it does:

- **CR-01, CR-02 (critical) — auth wiring.** `[Authorize]` and role-based `[Authorize(Roles = "cypmd_content_access_user")]` added to `HelpController`. See the Authorization section.
- **WR-01 — page-size validation.** `WikiService.SearchAsync` early-return paths (empty query, etc.) now construct `WikiSearchResult` with `PageSize = DefaultPageSize` rather than the unchecked `pageSize` parameter, so a caller passing `pageSize = 1_000_000` doesn't get that echoed back in the result envelope.
- **WR-02 — pagination ellipsis.** `Search.cshtml` pagination only renders the `…` ellipsis when there are actually hidden pages between the current window and the last page. Was rendering unconditionally.
- **WR-03 — ARIA on slug breadcrumbs.** Search result slug-path breadcrumbs now carry `aria-label` so screen readers announce the hierarchy properly.
- **WR-04 — TinyMCE paste-as-text.** Covered in the gotchas list above.
- **IN-01, IN-02, IN-03 — informational.** Inline comments documenting URL-encoding safety of the search query, silent truncation of oversized queries, and `page` parameter validation pushed up to the controller layer.

## Wiki seeder

Same branch ships `WikiSeeder` and a `POST /help/seed` button in the edit-mode toolbar (rendered by `Help/Index.cshtml`). One click inserts a 21-page tableschecking-shaped tree (Getting started / Checking exercises / Submitting amendments / Help and support, with sub-pages and KS2/KS4 sub-trees). Useful for filling an empty environment with realistic content for search and breadcrumb testing.

Mechanics:

- **Goes through `WikiService.CreatePageAsync`.** Same path as the UI — same validation, same `BodyPlainText` extraction, same `SearchVector` regen. Seeded pages are immediately searchable.
- **Additive, not idempotent.** If a page with the same title already exists at that level, `CreatePageAsync` throws `DuplicateWikiPageException`; the seeder catches it and retries with `Title (2)`, `Title (3)`, ... So running the seed twice gives you a "Getting started" tree *and* a "Getting started (2)" tree side-by-side. To re-seed cleanly, soft-delete the old tree first.
- **Per-page transactions, not whole-tree.** Each `CreatePageAsync` is its own transaction (via `WikiRepository.ExecuteInTransactionAsync`). A mid-seed crash leaves a partial tree behind. Live with it for now or wrap the seeder loop in a single transaction if it bites.
- **Edit-mode only on the UI.** The seed button is rendered inside the edit-mode disclosure, behind a `@if (ViewBag.IsEditMode)` guard. The endpoint has `[ValidateAntiForgeryToken]` and `[Authorize(Roles = "cypmd_content_access_user")]` — see the Authorization section below.

## Authorization

The whole `HelpController` is split into anonymous read paths and role-gated write paths. Editorial mutations require the DfE Sign-In role `cypmd_content_access_user` — populated into the user's claims by `ClaimsEnrichmentService` from the DfE Sign-In API.

- **Anonymous (read-only):** `GET /help/{slugPath}` (Index), `GET /help/search`, `GET /help/deleted`, `GET /help/versions/{id}`. Marked `[AllowAnonymous]` because the site-wide `RequireAuthenticatedUser` fallback policy would otherwise force a sign-in for plain wiki reading.
- **Role-gated (`cypmd_content_access_user`):** `POST /help/create`, `POST /help/edit/{id}`, `POST /help/delete/{id}`, `POST /help/move`, `POST /help/restore/{id}`, `POST /help/revert/{pageId}/{versionId}`, `POST /help/seed`. Each carries `[Authorize(Roles = "cypmd_content_access_user")]` *and* `[ValidateAntiForgeryToken]`.
- **UI matches endpoint policy.** `Help/Index.cshtml` only renders the Edit-mode toggle, the "Deleted pages" link, and the "Seed sample pages" button when `User.IsInRole("cypmd_content_access_user")`. So a signed-in user without the role sees the same read-only wiki an anonymous user does — no broken-looking buttons that 403 on click.
- **Role code gotcha.** The role code is `cypmd_content_access_user` (with the `_user` suffix), not `cypmd_content_access`. The shorter form was the working assumption from the review fixes; the actual code populated by DfE Sign-In has the suffix. If `[Authorize(Roles = "...")]` ever silently 403s a known-good account, that's the first thing to check.

## How to test it

### Build + run

```sh
cd check-performance-data
docker compose up --build -d --profile all
dotnet build
dotnet run --project src/DfE.CheckPerformanceData.Web
```

### Tests

```sh
# Unit tests (fast)
dotnet test tests/DfE.CheckPerformanceData.UnitTests

# Integration tests (spins up Postgres via Testcontainers — needs Docker running)
dotnet test tests/DfE.CheckPerformanceData.IntegrationTests
```

Whole solution should be green: 100/100, 0 skipped.

### Manual checks

Seed has at least one page tagged with the `govuk-warning-text` component — useful for the sanitizer regression check.

| What to try | URL / action | Expected |
|---|---|---|
| Basic match | `/help/search?q=schools` | Pages mentioning "schools" listed, most relevant first, snippet has `<mark>schools</mark>` highlighted. |
| Stemming works | `/help/search?q=managing` and `/help/search?q=management` and `/help/search?q=manage` | Same set of pages in each — anything mentioning any inflection of "manage" hits. Snippet highlights the form that's actually present in the body. |
| Stop-word-only query | `/help/search?q=the` | "No results" page (clean empty state, not a 500 — the stemmed query is empty). |
| Wildcard / prefix match | `/help/search?q=schoo*` | Pages mentioning "school", "schools", "schooling" all hit. Equivalent to `school*` because the stemmer was already collapsing them. |
| Wildcard with multiple tokens | `/help/search?q=key stage*` | AND-match on `key` and `stage` with prefix on `stage` — pages titled "Key Stage 2", "Key Stage 4" both match. |
| Wildcard ignores phrase quotes | `/help/search?q="performance data*"` | Quotes are stripped (sanitiser drops non-alphanumerics), prefix match runs on `performance` AND `data`. Phrase semantics do *not* apply in wildcard mode. |
| Asterisk-only query | `/help/search?q=**` or `/help/search?q=* * *` | "No results" empty state. Sanitiser yields an empty `tsquery`, service returns `EmptyQuery` invalid reason. |
| Title beats body | Pick a word that appears in one page's title and another's body. Search for it. | Title-match page is above the body-match page. |
| Phrase | `/help/search?q="performance data"` | Only pages containing that exact phrase. |
| Exclusion | `/help/search?q=schools -academy` | Pages with "schools" but not "academy". |
| Garbage input doesn't crash | `/help/search?q="`<br>`/help/search?q=)))` <br>`/help/search?q=` (empty) | 200 OK, empty or sensible result set, no 500. |
| Pagination | `/help/search?q=<common term>&skip=10&take=10` | Page two of results, total count unchanged. |
| Soft-delete hidden | Soft-delete a page that previously matched, search again. | Deleted page is no longer in results. |
| Sidebar present | Visit any `/help/*` page. | Search box shows in the sidebar; submitting routes to `/help/search`. |
| Back link | From `/help/search?q=anything`, click "Back". | Returns to `/help`. |
| GDS warning renders | Visit a wiki page that uses `govuk-warning-text`. | Renders with the black-circle `!` icon, not as plain bold text. (Sanitizer regression check.) |
| Duplicate title is blocked | In edit mode, open "Add new page", create a page called `About`. Open the disclosure again and try to add another page called `About` (or `about`, or `About!`) under the same parent. | Page is **not** created. The disclosure stays open showing a GDS error summary ("A page with the title 'About' already exists at this location. Choose a different title."), the Title input is highlighted as `--error` and pre-filled with what was typed, and the Parent select is pre-selected to the chosen parent. Save unblocks once the title is changed to something whose slug doesn't collide. |
| Duplicate allowed under different parent | Create `About` at root, then create `About` as a child of some page. | Both succeed (slug uniqueness is scoped per parent). |
| Duplicate allowed if existing page is soft-deleted | Create `About`, soft-delete it, create another `About` at the same level. | Second create succeeds — the unique index and `SlugExistsAsync` both filter on `IsDeleted = false`. |
| Seeder fills an empty wiki | On a wiki with no pages, enter edit mode and click "Seed sample pages". | 21 pages appear in a four-root tree (Getting started / Checking exercises / Submitting amendments / Help and support) with the KS2/KS4 sub-tree under Checking exercises. All immediately searchable. |
| Seeder is additive, not idempotent | Click "Seed sample pages" a second time. | Another full tree appears alongside the first, with each root suffixed `(2)` (e.g. "Getting started (2)"). Search returns hits from both. Soft-delete the prior trees and re-seed for a clean baseline. |
| Anonymous user can read the wiki | Sign out, then visit `/help` and `/help/search?q=anything`. | Pages render normally. No sign-in redirect for read-only routes. No Edit-mode toggle, no "Deleted pages" link, no Seed button visible in the UI. |
| Anonymous user is blocked from edits | Sign out, then `curl -X POST` to `/help/create` (or any `POST /help/*`). | 302 redirect to the DfE Sign-In OIDC endpoint. Mutation does not run. |
| Signed-in user without role sees read-only UI | Sign in as an account that does NOT have `cypmd_content_access_user`. Visit `/help`. | Same chrome as an anonymous visitor — no Edit-mode toggle, no Deleted pages link, no Seed button. Direct POSTs to mutation endpoints return 403. |
| Editorial user sees full UI | Sign in as an account with `cypmd_content_access_user`. Visit `/help`. | Edit-mode toggle, Deleted pages link, and Seed button all visible. All mutation endpoints accept the request. |
| Static assets are reachable when signed out | Sign out, then `GET /css/site.css` directly. | 200 with the stylesheet body. NOT a 302 to OIDC. (Regression check for the static-asset `AllowAnonymous` fix — without it, anonymous styling silently breaks.) |

### Quick sanity from the DB

```sh
docker compose exec postgres psql -U postgres -d cpd -c \
  "SELECT \"Title\", ts_rank(\"SearchVector\", websearch_to_tsquery('english','schools')) AS rank
   FROM \"WikiPages\"
   WHERE \"SearchVector\" @@ websearch_to_tsquery('english','schools')
   ORDER BY rank DESC LIMIT 10;"
```

Confirms the GIN index is doing the work and ranking lines up with what the UI shows.
