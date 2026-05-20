# Web TagHelpers

Custom Razor TagHelpers used by the Check Performance Data Web project.

## govuk-confirm-modal

Renders a focus-trapped, GOV.UK Design System-styled confirmation dialog wrapping a real form.
Replaces inline `confirm()` calls for destructive actions (delete, revert).

The dialog body uses canonical GDS components throughout:

- Heading is phrased as a question ("Are you sure you want to delete '{name}'?")
- Warning is rendered through the canonical `govuk-warning-text` component
- Cancel control is `<a class="govuk-link" href="#">Cancel</a>` per GDS button-page guidance

### Usage

```razor
@{ var modalId = $"confirm-delete-{node.Id}"; }
<button type="button"
        class="tv-del"
        data-confirm-trigger="@modalId"
        aria-haspopup="dialog"
        aria-controls="@modalId"
        aria-label="Delete @node.Title">
    Delete
</button>

<govuk-confirm-modal id="@modalId"
                     title="@($"Are you sure you want to delete '{node.Title}' and all child pages?")"
                     warning-text="This action cannot be undone."
                     body=""
                     confirm-label="Yes, delete page"
                     destructive="true"
                     form-action="@($"/help/delete/{node.Id}")"
                     form-method="post">
    @Html.AntiForgeryToken()
    <input type="hidden" name="editMode" value="true" />
</govuk-confirm-modal>
```

### Attributes

| Attribute | Required | Type | Default | Notes |
|-----------|----------|------|---------|-------|
| `id` | yes | string | — | Unique per page. Used as the dialog id and as the target of the trigger's `data-confirm-trigger`/`aria-controls`. |
| `title` | yes | string | — | Modal heading. Phrase as a question ("Are you sure you want to ...?"). Use single quotes around quoted page names. Server-encoded. |
| `warning-text` | required when `destructive="true"` | string | `""` | Body of the canonical `govuk-warning-text` component rendered between the heading and the body paragraph. Use one short sentence describing the irreversible-or-replacing consequence. Server-encoded. |
| `body` | no | string | `""` | Optional supporting paragraph below the warning-text. Empty string renders an empty `<p>` element so `aria-describedby` remains valid. Server-encoded. |
| `confirm-label` | yes | string | — | "Yes, [verb] [noun]" pattern. Verb matches the title's action verb. |
| `destructive` | no | bool | `false` | When `true`, applies `govuk-button--warning` to the confirm button AND emits the `govuk-warning-text` component. |
| `form-action` | yes | string | — | Where the form posts on confirm. Server-side controlled. |
| `form-method` | no | string | `"post"` | HTTP method for the form. Use `"post"` for destructive actions. Do NOT use `"dialog"` — that closes the dialog without submitting. |

### Rendered DOM order (inside the form)

1. Razor child content (e.g. `@Html.AntiForgeryToken()`, hidden inputs)
2. `<h2 class="govuk-heading-m" id="{id}-title">` heading
3. `<div class="govuk-warning-text">` block (destructive variant only)
4. `<p class="govuk-body" id="{id}-body">` body paragraph (always emitted; may be empty)
5. `<div class="govuk-button-group">` containing:
   - `<button type="submit" class="govuk-button govuk-button--warning">` confirm button
   - `<a class="govuk-link" href="#" data-confirm-cancel autofocus>Cancel</a>` cancel link

### Trigger button contract

The trigger button lives in the parent view, **not** inside the TagHelper. It must:

| Attribute | Required | Value |
|-----------|----------|-------|
| `type` | yes | `"button"` (NOT `"submit"` — the trigger does not submit anything) |
| `data-confirm-trigger` | yes | the `id` attribute of the modal it opens |
| `aria-haspopup` | yes | `"dialog"` |
| `aria-controls` | yes | the modal `id` |
| `aria-label` (or visible text) | yes | accessible name for the button |

### Form children

Whatever you put inside `<govuk-confirm-modal>...</govuk-confirm-modal>` lands inside the
rendered `<form>`, before the heading. Always include `@Html.AntiForgeryToken()` so the POST
passes ASP.NET Core's antiforgery validation; add any hidden inputs the destination
controller action needs.

### JS module dependency

The trigger/cancel/backdrop wiring lives in `wwwroot/js/confirm-modal.js`, registered globally
in `Views/Shared/_Layout.cshtml`. You don't need to do anything per-modal — the JS auto-binds
every `[data-confirm-trigger]` button on the page.

The dialog itself uses the native HTML5 `<dialog>` element opened via `.showModal()`. This means
the browser provides Esc-to-close, backdrop dimming, and focus restoration for free. The JS
module additionally:

- Calls `event.preventDefault()` on cancel-link clicks so the `href="#"` does not navigate
- Re-focuses `[data-confirm-cancel]` after `.showModal()` to work around a Chromium autofocus
  edge case
- Wires an explicit Tab focus-trap because Chromium's native trap leaks to `<body>` when the
  autofocused element is the last focusable in DOM order (button-group convention is
  destructive-confirm-first / cancel-second)

### Accessibility notes

- The Cancel link receives initial focus on open (`autofocus`). This is deliberate — a reflex
  Enter press should NOT fire the destructive action. To confirm, the user must explicitly Tab
  to the destructive button and press Enter, or click it.
- For destructive variants, the canonical `govuk-warning-text` component wraps the warning body
  in `<strong class="govuk-warning-text__text">` with a `<span class="govuk-visually-hidden">Warning</span>`
  inside, so screen readers announce "Warning ..." even though the `!` icon is `aria-hidden="true"`.
- The modal uses `aria-labelledby` and `aria-describedby` to associate the heading and body
  paragraph with the dialog element. The body paragraph element is always emitted (even when
  empty) so the `aria-describedby` reference stays valid.

### Browser support

Native `<dialog>` + `.showModal()` + `::backdrop` is "Baseline Widely available" since March
2022 (Chrome 37+, Firefox 98+, Safari 15.4+). Internal admin tooling only — JS is a hard
requirement for this component.
