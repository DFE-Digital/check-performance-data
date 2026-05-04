# CMS search (`cms-search` branch)

Wiki search for the help CMS. Postgres full-text search over title + body, ranked, snippeted, paginated. Lives at `/help/search?q=...` and is wired into the help sidebar.

## What's on the branch

### Features

- **Full-text search over wiki pages.** New `SearchVector` column on `WikiPages` — Postgres `tsvector`, stored generated, indexed with GIN. Computed as `setweight(to_tsvector('english', Title), 'A') || setweight(to_tsvector('english', BodyPlainText), 'B')` so title hits outrank body hits at equal density.
- **Plain-text body column.** `BodyPlainText` is the tag-stripped, whitespace-collapsed version of `Content`. Written by the service layer on every create/update so the `tsvector` stays in sync without a trigger. The migration backfills existing rows once via a Postgres regex.
- **Search query parsing.** Uses `websearch_to_tsquery` (`EF.Functions.WebSearchToTsQuery`). Accepts user-typed input verbatim — quoted phrases, `OR`, `-exclude`, gibberish, unbalanced quotes — and never throws. Input is trimmed and length-capped at the service layer; no escaping needed.
- **Ranked results with snippet highlighting.** `ts_rank` orders by relevance, title is stable tie-break. `ts_headline` returns a snippet wrapped in `<mark>...</mark>` (15–25 words, one fragment). Rendered with `@Html.Raw` — safe because the source is `BodyPlainText`, not raw HTML.
- **Pagination.** `skip`/`take` on the controller; total count returned alongside the page.
- **Help sidebar + back link.** Search box on every `/help` page. Search results page has a back link to `/help`.
- **Postgres integration tests.** New `DfE.CheckPerformanceData.IntegrationTests` project running against real Postgres via Testcontainers. Covers the search path end-to-end (no mocks for the FTS bits).

### Stemming and tokenisation

The `'english'` argument to `to_tsvector` and `websearch_to_tsquery` is a Postgres FTS configuration — Snowball English stemmer + the default English stop-word list + a basic punctuation-stripping tokeniser. Three things to know:

- **Stemming is two-way and automatic.** Both the stored `SearchVector` and the user's query get reduced to the same root form before they're compared. `school`, `schools`, `schooled`, `schooling` all index and search as `school`. `manage`, `manages`, `managed`, `managing`, `manager`, `management` all collapse to `manag`. So `?q=managing` will hit a page that only ever wrote "management", and vice-versa. This is desired behaviour, not a bug.
- **Stop words are dropped.** Words like `the`, `a`, `is`, `of`, `and` are stripped from both the index and the query. `?q=the schools` searches for `school`. `?q=the` alone returns nothing (after stemming + stop-word stripping the query is empty — `websearch_to_tsquery` returns an empty `tsquery`, which matches no rows). The empty/min-length error in `WikiService.SearchAsync` is a *pre-Postgres* check on raw input length; it doesn't catch this case, so a stop-word-only search produces a clean "no results" page rather than an error. That's intentional.
- **Stemming applies to titles AND body the same way.** Title weight (`A`) only changes the *rank*, not whether something matches. A page titled "Schools" matches `?q=school`, just with higher rank than a page that mentions schools only in its body.
- **What it does NOT do.** No synonyms (`pupil` ≠ `student`), no fuzzy/edit-distance matching (`scool` won't hit `school`), no language detection (everything is parsed as English even if the page is in another language). If we ever need any of those, the `'english'` arg is the seam — swap in a custom configuration registered via `CREATE TEXT SEARCH CONFIGURATION` and the rest of the pipeline keeps working.

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

### Quick sanity from the DB

```sh
docker compose exec postgres psql -U postgres -d cpd -c \
  "SELECT \"Title\", ts_rank(\"SearchVector\", websearch_to_tsquery('english','schools')) AS rank
   FROM \"WikiPages\"
   WHERE \"SearchVector\" @@ websearch_to_tsquery('english','schools')
   ORDER BY rank DESC LIMIT 10;"
```

Confirms the GIN index is doing the work and ranking lines up with what the UI shows.
