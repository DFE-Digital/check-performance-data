# Accessibility audit — effort estimate

Sub-tickets of epic [#384 Accessibility Issues from Audit](https://github.com/DFE-Digital/check-performance-data/issues/384),
raised from the Zoonou audit recorded on [#373](https://github.com/DFE-Digital/check-performance-data/issues/373).

| # | Title | Size | What it needs |
|---|---|---|---|
| 374 | Page regions not identified with ARIA landmarks | S | The home link and feedback link sit outside any landmark. Wrap the header/phase-banner block in `_Layout.cshtml`; repeat in the three admin/share layouts. Fixes every page at once. |
| 375 | Missing Heading 1 | M | 8 multi-question pages in `Remove_KS4June.json` have no `title`, so `Page.cshtml` renders no `<h1>`. Code fix is one line; the work is writing 8 titles that content sign off. Add a test asserting every flow page yields an H1. |
| 376 | Semantic markup not used for headings | M | Guidance page text looks like headings but is not marked up as such. CMS content work, not code — a `Heading` widget already exists. Can run in parallel with the dev tickets. |
| 377 | "Skip to main content" lands on the breadcrumb | M | The breadcrumb is inside the main wrapper (`Views/Page/Content.cshtml:38`). Needs a new layout section so it renders before `<main>`. Small change, but retest every CMS page. |
| 378 | Multiple instances of identical link text | S | Add a `govuk-visually-hidden` pupil name to the View/Delete links in `Views/AmendmentRequests/Index.cshtml`. Same pattern the row checkbox already uses. Do with #385. |
| 379 | Buttons contain the same accessible name | S | Several "Continue" buttons on the landing page. Add the window title as hidden text inside each link (`Views/LandingPage/Index.cshtml:66`). |
| 380 | Dragon cannot reach the file upload | S | Raised for reference. A known Dragon bug against GOV.UK Frontend, not our code. The suggested `role="link"` workaround departs from the GDS pattern. **Recommend won't-fix**, or half a day for a spike. |
| 381 | Confusing announcements *(umbrella)* | — | Now split into #385–#389. Close once the split is agreed. |
| 382 | Related links not grouped in a `<nav>` | S | The side nav already uses `<nav aria-label="Page contents">`; the offenders are the Guidance card/rich-text link groups. Either a small Card widget change or a content re-author. |
| 383 | Radios not announced by TalkBack | M | `_Radio.cshtml` has no `data-module="govuk-radios"` and no `aria-describedby` on the fieldset. Central fix across `_Radio`, `_Checkbox`, `_Date`. Most of the cost is confirming on a real Android device. |
| 385 | "View"/"Delete" links announced identically | S | Same links and same fix as #378, reported under 4.1.2 instead of 2.4.4. Fix both in one change. |
| 386 | Separators announced by VoiceOver | S | Likely the visible `govuk-section-break--visible` rules in `_FileUpload.cshtml` and the landing page. Confirm on device, then hide the decorative ones from assistive tech. |
| 387 | List items announced as "em dash" | M | Find which list items hold a literal em dash. Spans views **and** CMS rich-text content, so the search is wider than it looks. |
| 388 | "Disclosure triangle" on expandable sections | S | TalkBack's own name for the native `<details>` element, not text we supply. Likely **no action** — confirm with the auditor before touching the markup, since ARIA would be worse here. |
| 389 | Redundant hidden instructions on Provide Evidence | S | The file input's `aria-describedby` names the visible hint, which is the standard GOV.UK pattern. Confirm which text is actually duplicated before removing anything; the id list and markup must change together. |

**Totals:** 10 S, 4 M

## Sequencing the work

- **#374, #377 and #383 are single-point fixes in shared layouts or shared partials.** One PR fixes them
  across every page. 
- **#378 + #385 are the same fix**, and #379 is that fix applied elsewhere. One PR.
- **#376 and part of #382 are content work, not code.** 

Two tickets may close with no code: **#380** (Dragon) and **#388** (disclosure triangle). Confirm with Zoonou.
