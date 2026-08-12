# Accessibility

This is a public sector service, so **WCAG 2.2 AA** and the **Public Sector Bodies (Websites and Mobile Applications) (No. 2) Accessibility Regulations 2018** apply.

Everything below is a defect that was found and fixed on the `accessibility-pass` branch ahead of the Zoonou audit. Treat each as an invariant when adding or editing a view — they are cheap to keep and expensive to re-find.

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

## Testing

The reusable pieces are unit tested — `PageTitleTests`, `PaginationWindowTests`, `QuestionPartialModelDescribedByTests`. Anything new that encodes an accessibility rule in C# should get the same treatment rather than being verified by eye alone.

Dev seeding is sized to expose these issues locally: `SeedPupilData` writes 120 included + 120 non-included pupils per school per window (`PupilsPerGroup`), well past the pupil list's page size of 10, so the pagination window and its ellipses are exercised on every local run. Don't shrink that back to a page or two.
