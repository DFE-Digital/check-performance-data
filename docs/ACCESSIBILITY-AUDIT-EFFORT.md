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
| 380 | Dragon cannot reach the file upload | S | Fixed 16 Oct 2026 by opting into GOV.UK Frontend's JavaScript-enhanced File upload on every file input — see the Done table. The auditor's `role="link"` workaround was not used. |
| 381 | Confusing announcements *(umbrella)* | — | Now split into #385–#389. Close once the split is agreed. |
| 382 | Related links not grouped in a `<nav>` | S | The side nav already uses `<nav aria-label="Page contents">`; the offenders are the Guidance card/rich-text link groups. Either a small Card widget change or a content re-author. |
| 383 | Radios not announced by TalkBack | M | `_Radio.cshtml` has no `data-module="govuk-radios"` and no `aria-describedby` on the fieldset. Central fix across `_Radio`, `_Checkbox`, `_Date`. Most of the cost is confirming on a real Android device. |
| 385 | "View"/"Delete" links announced identically | S | Same links and same fix as #378, reported under 4.1.2 instead of 2.4.4. Fix both in one change. |
| 386 | Separators announced by VoiceOver | S | Likely the visible `govuk-section-break--visible` rules in `_FileUpload.cshtml` and the landing page. Confirm on device, then hide the decorative ones from assistive tech. |
| 387 | List items announced as "em dash" | M | Find which list items hold a literal em dash. Spans views **and** CMS rich-text content, so the search is wider than it looks. |
| 388 | "Disclosure triangle" on expandable sections | S | Assessed 14 Oct 2026: no code. TalkBack's own name for the native `<details>` element — see the Done table. |
| 389 | Redundant hidden instructions on Provide Evidence | S | The file input's `aria-describedby` names the visible hint, which is the standard GOV.UK pattern. Confirm which text is actually duplicated before removing anything; the id list and markup must change together. |

**Totals:** 9 S, 5 M

## Done

Fixed in code, or assessed and recorded as correct as it stands, with `AccessibilityAuditViewTests`
pinning each one and the invariants written up in [`accessibility.md`](accessibility.md):

| # | What changed |
|---|---|
| 374 | `<header role="banner">` around the masthead in `_Layout`, `_AdminLayout` and `_ShareLayout`. |
| 375 | Titles added to the eight untitled multi-question pages in `Remove_KS4June.json`. `QuestionFlowValidatorAlignmentTests` now pins both halves of the heading rule — the pre-existing test covered single-question pages only, which is why this half went unguarded. |
| 377 | The CMS breadcrumb moved into an `@section Breadcrumbs` that renders before `<main>`. Three CSS rules that keyed off its old position were rewritten; the guidance page and a template-H1 page were compared before and after, pixel for pixel. |
| 378 + 385 | Hidden ` request {reference} for {pupil}` on every Edit / View / Delete link. |
| 379 | Hidden ` to {window title}` on each landing-page Continue. |
| 383 | `aria-describedby` on the radios fieldset for its hint and error, per-option hints wired on both `_Radio` and `_Checkbox`, and `data-module="govuk-radios"`. **Still needs confirming on a real Android device** before the ticket closes. |
| 386 | `aria-hidden="true"` on the decorative visible `<hr>` rules. The footer's own section break is left alone. |
| 388 | **No markup change.** The expandable sections are the GOV.UK Details component — a native `<details>`/`<summary>` as GOV.UK Frontend 5 ships it. Chromium exposes a `<summary>` with the role DisclosureTriangle, and "disclosure triangle" is TalkBack's name for that role, announced with the expanded or collapsed state and the summary text; name, role and state are all exposed, which is what 4.1.2 asks for. Published tests record the same announcement at Chrome 64 (Android 8), Chrome 81 (Android 10) and Chrome 149 (Android 16), and with TalkBack 6.2 and 7.2. Putting `role="button"` on the summary, or replacing the element with a button-and-`aria-expanded` disclosure, changes how the element is exposed on every platform — O'Hara found macOS VoiceOver then drops the state and loses headings nested in the summary — and every screen reader and browser pairing would need re-testing, and it leaves the GOV.UK pattern. Where the sections are: the four "How can I find a DfE number?" help panels on the KS4 Remove flow's DfE-number pages (`questionHelpTitle` in `Remove_KS4June.json`, rendered by `Views/Journey/_Question.cshtml`) and the "Have you checked your second late results file?" panel on the 16-19 results-issue page (`Views/ResultIssue/Index.cshtml`); the evidence pages' "What evidence do I need?" details went with #523, and the CMS guidance pages rendered none on preproduction on 14 Oct 2026. Evidence (rendered markup, Chromium accessibility tree collapsed and expanded, screenshot, sources) is held by the team outside the repository and available on request. Pinned by the `HelpPanel_IsANativeDetailsElement_WithNoAriaRoleOverride` theory in `AccessibilityAuditViewTests`, which covers both views. **Confirm with Zoonou on retest**; the issue stays open until they do. |
| 389 | The file input's `aria-describedby` no longer names the visible hint; it names the "N of 6 files added." line and, when there is one, the error. Chrome exposes a native file input as a button and, in our reading of the audit (unconfirmed with Zoonou), JAWS directs the user to press a key to hear its description, which was the hint on screen; the count line is still named, so the prompt itself probably remains — see the question in the PR. Pinned by `QuestionPartialModelDescribedByTests` and a `#389` fact in `AccessibilityAuditViewTests`. **Confirm with Zoonou on retest** that this is the text they meant — the count line did not exist when they audited. |
| 380 | Every file input is wrapped in GOV.UK's `govuk-file-upload-wrapper` with `data-module="govuk-file-upload"` (the schema page through the tag helper's `javascript-enhancements`), so the control Dragon reaches is a real "Choose file" button that opens the picker; the two scripts that touch a file input reach it by name and place their errors above the wrapper. Nothing changes without JavaScript. **Zoonou's retest with Dragon is the proof** — nothing in the build drives Dragon; the pins assert the button, its name and the picker. |

## Still open

- **376, 382, 387** — need content decisions, not just code: Guidance headings and link groups to
  re-author, and a hunt for literal em dashes that spans the CMS as well as the views.
- **380** — fixed 16 Oct 2026 with the GOV.UK enhanced File upload; stays open for Zoonou's retest with Dragon. (388 was assessed on 14 Oct 2026 — see Done.)

## Sequencing the work

- **#374, #377 and #383 are single-point fixes in shared layouts or shared partials.** One PR fixes them
  across every page. 
- **#378 + #385 are the same fix**, and #379 is that fix applied elsewhere. One PR.
- **#376 and part of #382 are content work, not code.** 

**#380** (Dragon) was fixed on 16 Oct 2026 with the GOV.UK enhanced File upload and waits for Zoonou's retest. **#388** (disclosure triangle) was assessed on 14 Oct 2026 and needs no code — see Done.
