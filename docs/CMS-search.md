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

### Bugs / gotchas fixed along the way

- **GOV.UK warning component rendered unstyled on seeded wiki content.** `Ganss.Xss` `HtmlSanitizer` with the default constructor strips `class`, `aria-*`, `role`, and `data-module` — even though `class` is documented as a default. Fix: explicit allow-list in `HtmlRenderingService.CreateSanitizer()`. XSS protection unchanged (`<script>`, `on*`, `javascript:` still blocked). Two regression tests in `HtmlRenderingServiceTests`.
- **EF translation broke when the FTS query was hoisted.** `EF.Functions.WebSearchToTsQuery("english", q)` *must* be written inline at every call site. Its `config` arg is `[NotParameterized]`, so storing it in a local variable forces client-evaluation and throws. Repeated three times in `WikiRepository.SearchAsync` for that reason — not a refactor opportunity.
- **Soft-delete leak risk.** `WikiPageConfiguration` has `HasQueryFilter(w => !w.IsDeleted)`. The search path deliberately doesn't call `IgnoreQueryFilters()` so deleted pages stay out of results. If anyone adds it later, deleted pages will be searchable.

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

Whole solution should be green: 92/92, 0 skipped.

### Manual checks

Seed has at least one page tagged with the `govuk-warning-text` component — useful for the sanitizer regression check.

| What to try | URL / action | Expected |
|---|---|---|
| Basic match | `/help/search?q=schools` | Pages mentioning "schools" listed, most relevant first, snippet has `<mark>schools</mark>` highlighted. |
| Title beats body | Pick a word that appears in one page's title and another's body. Search for it. | Title-match page is above the body-match page. |
| Phrase | `/help/search?q="performance data"` | Only pages containing that exact phrase. |
| Exclusion | `/help/search?q=schools -academy` | Pages with "schools" but not "academy". |
| Garbage input doesn't crash | `/help/search?q="`<br>`/help/search?q=)))` <br>`/help/search?q=` (empty) | 200 OK, empty or sensible result set, no 500. |
| Pagination | `/help/search?q=<common term>&skip=10&take=10` | Page two of results, total count unchanged. |
| Soft-delete hidden | Soft-delete a page that previously matched, search again. | Deleted page is no longer in results. |
| Sidebar present | Visit any `/help/*` page. | Search box shows in the sidebar; submitting routes to `/help/search`. |
| Back link | From `/help/search?q=anything`, click "Back". | Returns to `/help`. |
| GDS warning renders | Visit a wiki page that uses `govuk-warning-text`. | Renders with the black-circle `!` icon, not as plain bold text. (Sanitizer regression check.) |

### Quick sanity from the DB

```sh
docker compose exec postgres psql -U postgres -d cpd -c \
  "SELECT \"Title\", ts_rank(\"SearchVector\", websearch_to_tsquery('english','schools')) AS rank
   FROM \"WikiPages\"
   WHERE \"SearchVector\" @@ websearch_to_tsquery('english','schools')
   ORDER BY rank DESC LIMIT 10;"
```

Confirms the GIN index is doing the work and ranking lines up with what the UI shows.
