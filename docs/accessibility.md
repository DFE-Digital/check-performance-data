# Accessibility

This is a public sector service, so **WCAG 2.2 AA** and the **Public Sector Bodies (Websites and Mobile Applications) (No. 2) Accessibility Regulations 2018** apply.

Everything below is a defect that was found and fixed — first on the `accessibility-pass` branch ahead of the Zoonou audit, then from the audit's own findings (epic [#384](https://github.com/DFE-Digital/check-performance-data/issues/384), which names the ticket behind each rule). Treat each as an invariant when adding or editing a view — they are cheap to keep and expensive to re-find.

---

## Page titles

**Every page sets `ViewData["Title"]` / `ViewBag.Title`, and it matches the `<h1>`.**

The title is the first thing a screen reader announces after a navigation, and it is what appears in browser history and tab lists. GDS convention is that it says the same thing as the page heading, in sentence case.

- Where the `<h1>` is an `EditableTitle` content block, the title matches that block's **default** text, with a comment in the view saying so (the two can drift once an editor changes the block; the default is the best anchor available).
- Where the `<h1>` is dynamic — the school name on the landing page, a date-bearing sentence on Confirm — the title is built from the same value.

**Do not prefix a title with `"Error: "` in a view.** `_Layout.cshtml` and `_AdminLayout.cshtml` apply `PageTitle.WithErrorPrefix` (`Web/Common/PageTitle.cs`) centrally, so no page can forget it. Any new layout must do the same.

The layout triggers the prefix on `!ModelState.IsValid`. When a page's errors live **only** on its view model rather than in `ModelState` — the Summary page's duplicate-request conflict banner, for example — set `ViewData["HasError"] = true` in the view. `WithErrorPrefix` is idempotent, so a view that has genuinely already prefixed its own title won't double up.

**The GOV.UK Frontend library must not also apply the prefix.** `GovUk.Frontend.AspNetCore` ships a `TitleTagHelper` targeting `<title>` inside `<head>`; with `GovUkFrontendOptions.PrependErrorToTitle` (its default is `true`) it appends `"Error: "` whenever a `<govuk-error-summary>` has been rendered on the page. Combined with the layout, every error page that used the library's error summary component announced `"Error: Error: …"`. `AddCpdGovUkFrontend` (`Web/Startup/GovUkFrontendExtensions.cs`) turns the option off, so the layouts are the single owner — register GOV.UK Frontend through that extension, never `AddGovUkFrontend()` directly. The layouts keep ownership rather than the tag helper because the tag helper only fires for the library's own error summary, while `WithErrorPrefix` also covers `ViewData["HasError"]` pages. `GovUkFrontendExtensionsTests` guards the option.

## Headings

**Exactly one `<h1>` per page.**

In particular, do not set `is-page-heading="true"` on a `govuk-radios-fieldset-legend` when the page already has an `<h1>` (typically an `EditableTitle`). Use `class="govuk-fieldset__legend--l"` instead — same visual size, no second page heading. `Views/CheckYourPupilData/Index.cshtml` is the worked example.

## Labels

**Every form control has a real `<label for="…">`.** `aria-describedby` is not an accessible name, and neither is a nearby `<h1>`.

Two patterns, depending on the page:

- **The control is the page's single question** — wrap the label in the heading rather than adding a separate `<h1>`:

  ```html
  <h1 class="govuk-label-wrapper">
      <label class="govuk-label govuk-label--l" for="pupil-search">@Model.Title</label>
  </h1>
  ```

  See `Views/Journey/PupilSearch.cshtml`. This matters doubly for JS-built inputs: accessible-autocomplete creates the input at runtime, so nothing in the server markup carries an accessible name unless a label points at the id the script is told to use.

- **Other content sits between the heading and the control** — give the control its own plain label. On `_FileUpload.cshtml` the uploaded-files table sits between the question heading and the file input, so a `for` reaching back over the table would read out of order; the input gets `<label class="govuk-label" for="fileUpload">Upload a file</label>`.

## `aria-describedby`

**Only name ids that are actually rendered.** A dangling reference resolves to nothing, and browsers drop the *entire* description rather than the missing part — so one absent id silently costs you the hint as well.

Build the id list conditionally from what the view emits. `QuestionPartialModel.DescribedBy` includes `fileUpload-hint` only when the question has a hint and `fileUpload-error` only when there is an error; `_FileUpload.cshtml` renders exactly those ids. **The two must be changed together** — there is a unit test (`QuestionPartialModelDescribedByTests`) guarding the model side.

## accessible-autocomplete

**The component owns its input's `aria-describedby`.** It points that attribute at its own generated `#{id}__assistiveHint` element and rewrites it on every re-render, so anything you set — via `inputAttributes`, or by poking the DOM after init — is silently discarded.

Pass hint and error text through **`tAssistiveHint`** instead, which sets the text of the element `aria-describedby` already references:

```js
var assistiveHint = (hasError ? 'Error: ' + errorText + '. ' : '')
    + hintText + '. '
    + 'When autocomplete results are available use up and down arrows to review and '
    + 'enter to select. Touch device users, explore by touch or with swipe gestures.';
```

Error first, then hint, then the component's own arrow-key/touch instructions — which must be kept, since replacing `tAssistiveHint` replaces them. Both `Views/Journey/PupilSearch.cshtml` and `Views/Journey/_Autocomplete.cshtml` do this; keep them in step.

**No select in the service is enhanced with `enhanceSelectElement` any more.** The revised-grade, syllabus-code and result pickers were all type-aheads at first; every list turned out short enough to read, so the enhancement was removed and the three are plain GOV.UK `<select>`s (see `docs/results-enquiry.md`). That retired a class of defect with it — the input opening with the placeholder row's text as its value (AB#301933), and an `onConfirm` override having to re-do the library's job of marking the posted `<option>` selected (AB#301934). If a picker ever needs a type-ahead again, read those tickets before reaching for `enhanceSelectElement`.

The remaining autocompletes — `PupilSearch.cshtml` and `_Autocomplete.cshtml` — are built with `accessibleAutocomplete({...})` over a suggestions endpoint, not by enhancing a select. `PupilSearch.cshtml` is therefore still JavaScript-dependent; that is a known gap, recorded in `docs/results-enquiry.md`.

**A chosen option's details are revealed by the `<select>`'s own `change` event** (`ResultSearch.cshtml`, AB#301934). The blocks are server-rendered, one per result, and the server decides which is hidden from the session — so the reveal works with JavaScript off too, and the script is an enhancement rather than the only path. They sit in a polite live region (`aria-live="polite"`) so the change is announced; keep it polite.

**A `Locator.PressAsync` call refocuses its element before dispatching the key.** ArrowDown moves DOM focus onto the highlighted `<li role="option">`, so refocusing the input between an ArrowDown and a following Enter fires the library's `handleInputFocus`, which resets `selected` to `-1` (the menu stays open; only the highlight is lost). `handleEnter` is a no-op unless `state.selected >= 0`, so that Enter silently does nothing. Drive the keyboard path of an autocomplete with `Page.Keyboard` instead, which sends keys to whatever already has focus.

## Links styled as buttons

**Every `<a class="govuk-button …">` needs `role="button" draggable="false" data-module="govuk-button"`.**

Without it, Space does not activate the control — a link responds to Enter only — so keyboard users cannot operate something that looks and reads like a button. The `data-module` attribute is what wires up GOV.UK Frontend's Space-key handling.

## Pagination

**Never render one link per page.** Use `PaginationWindow.Build` (`Web/Common/PaginationWindow.cs`): first page, last page, and the current page with one either side, with `<govuk-pagination-ellipsis />` marking each skipped run.

A school with a few hundred pupils otherwise produced one link per page — hundreds of tab stops for a keyboard user to get past the list, and every number read aloud by a screen reader. `Views/CheckYourPupilData/_Pagination.cshtml` is the reference implementation, and the dev seed deliberately generates enough pupils (see below) for this to be visible locally.

## Links must go somewhere

**No `href="#"` placeholders, ever** (WCAG 2.4.4). If a destination does not exist yet, either build a stub page or don't render the link.

## Statutory footer links are not content-managed

Privacy, Cookies, Accessibility statement and Guidance live as **static markup** in `_Layout.cshtml`, served by `PrivacyController` / `AccessibilityController` → `Views/Privacy/Index.cshtml` and `Views/Accessibility/Index.cshtml`.

Publishing an accessibility statement is a legal requirement under the 2018 regulations, so a CMS editor must not be able to retarget it, blank it, or leave it as a placeholder. Do not move these links back into an `EditableContent` block.

> **Content-block gotcha:** `IContentBlockService.EnsureAsync` seeds `defaultHtml` only when no block exists for the key. Editing `defaultHtml` in a view is therefore inert on every environment whose database already holds that block. That is why the footer block was re-keyed to `footer-support-and-guidance-v2` rather than edited in place; the old block is left orphaned (visible in `/admin/content-blocks`, no longer rendered) so hand-edited prose can still be recovered.

## Journey pages must render exactly one heading

**A question-flow page gets its `<h1>` from one of two places, and the config decides which.** `Page.cshtml` renders the page-level `<h1>` from the page's `title`; `JourneyViewModelBuilder` promotes the single question to the heading instead (`IsPageHeading`) when the page has exactly one question *and no title*. So:

- **single question** → leave `title` empty; the question is the heading.
- **anything else** — several questions, or a `Content`, `EvidenceUpload`, `ResultDetails` or `QualificationDetails` page — → set a `title`.

Setting a `title` on a single-question page switches *both* headings off, and leaving it off a multi-question page never turns one on. Eight pages in `Remove_KS4June.json` were in the second state when the audit ran (#375). Either way the page still renders, validates and submits, and the browser title is fine, so nothing but a screen reader notices. Use `pageTitle` when the browser title has to differ from the heading — for instance to keep a pupil name out of it.

`ResultDetails.cshtml` and `QualificationDetails.cshtml` render the `<h1>` unconditionally, so a missing title there gives an *empty* heading rather than none — worse, not better. `PupilSearch`, `ResultSearch` and `QualificationSearch` build their own heading (a `govuk-label-wrapper` `<h1>` around the search input, or a hard-coded fallback) and are exempt.

Both halves are pinned in `QuestionFlowValidatorAlignmentTests` — `SingleQuestionPages_LeaveThePageTitleEmpty_…` and `PagesWithoutASingleQuestionHeading_SetATitle_…`.

## Landmarks

**Every layout wraps its masthead in `<header role="banner">`.** GOV.UK Frontend's header component renders a plain `<div class="govuk-header">`, not a `<header>` element, so nothing in the masthead is a landmark unless the service supplies one. Without the wrapper, the GOV.UK home link and the phase banner's feedback link sit outside every landmark and are unreachable by landmark navigation (audit #374). `_Layout.cshtml`, `_AdminLayout.cshtml` and `_ShareLayout.cshtml` each open the wrapper before `<govuk-header>` and close it after the last masthead element; on `_Layout` that means the phase banner is inside it too.

## Skip link and breadcrumbs

**Breadcrumbs render before `<main>`, never inside it.** A breadcrumb is a repeated navigation block, so landing on it is exactly what "Skip to main content" exists to avoid (audit #377). `Views/Page/Content.cshtml` puts its breadcrumb in an `@section Breadcrumbs`, which `_Layout.cshtml` renders inside the `Header` section — after the banner landmark closes, before `@RenderBody()`. The section supplies its own `govuk-width-container`, because the layout renders it outside the template's.

Moving it out of `<main>` broke three CSS rules that keyed off its old position (`main:has(.cpb-breadcrumbs)`, and `.cpb-breadcrumbs + .govuk-grid-row` twice). Those now key off `body:has(.cpb-breadcrumbs)` and the `.cpb-content` wrapper that `Content.cshtml` puts around the authored tree. Spacing is unchanged; `AccessibilityAuditViewTests` pins that the old selectors do not come back.

## Repeated link and button text

**A link or button whose text repeats down a page carries a `govuk-visually-hidden` suffix naming its row.** Identical accessible names give a screen reader user no way to tell one row's control from the next, and no way to pick one out of a list of links (audit #378, #385, #379).

- `Views/AmendmentRequests/Index.cshtml` — Edit, View and Delete each carry ` request {reference} for {pupil name}`. The reference number is in there because it is the only value guaranteed unique when one pupil has two requests.
- `Views/LandingPage/Index.cshtml` — each window card's "Continue" carries ` to {window title}`.

## Decorative separators

**A visible `<hr class="govuk-section-break--visible">` used as decoration is `aria-hidden="true"`.** An `<hr>` maps to the separator role, and VoiceOver and TalkBack announce it — noise, when the heading either side already carries the structure (audit #386). This covers the landing page, the amendment requests page, `_FileUpload.cshtml` and the CMS Divider widget. The footer's `govuk-footer__section-break` is left alone: it is part of the GOV.UK Frontend footer markup.

## Option groups

**A radio or checkbox fieldset names its own hint and error through `aria-describedby`, and each option names its own hint on its own input.** `_Radio.cshtml` had neither, so the group's hint and its error message were never announced with the group, and every option's `SubLabel` was rendered with an id that nothing referenced (audit #383). `_Checkbox.cshtml` and `_Date.cshtml` already described the group; both option partials now describe the per-option hint too.

The id list is built conditionally on what the view actually emits, per the rule below — an option with no `SubLabel` gets no `aria-describedby` at all rather than a dangling one. `_Radio` also carries `data-module="govuk-radios"`, which is what GOV.UK Frontend's radios JS binds to.

## Testing

The reusable pieces are unit tested — `PageTitleTests`, `PaginationWindowTests`, `QuestionPartialModelDescribedByTests`. Anything new that encodes an accessibility rule in C# should get the same treatment rather than being verified by eye alone.

Rules that live in markup rather than in C# are pinned by `AccessibilityAuditViewTests` (`tests/.../Web/`), which reads the `.cshtml` as text and asserts on the source — the same hostless pattern as `LayoutRenderTests`. Each fact names the audit ticket it covers. Nothing else in the build notices a dropped attribute or a moved element, so add a fact there whenever you fix an accessibility defect in a view.

Dev seeding is sized to expose these issues locally: `SeedPupilData` writes 120 included + 120 non-included pupils per school per window (`PupilsPerGroup`), well past the pupil list's page size of 10, so the pagination window and its ellipses are exercised on every local run. Don't shrink that back to a page or two.
