// Progressive enhancement for the rules branch editor.
//
// Without JS every interaction is a full-page postback: structural edits POST to
// /admin/rules/branch/transform and re-render the page; "Save branch" POSTs to
// /admin/rules/branch/save and redirects. With JS we intercept the form submit and
// issue async fetch() calls instead, swapping just the condition tree in place and,
// on a successful save, showing a transient "Saved" toast — no page reload.
//
// The server actions are unchanged in their model binding: we post the SAME form
// fields (url-encoded), only adding the X-Requested-With header so they return a
// fragment (transform) or JSON (save) rather than the full view. Everything is gated
// on body.js-enabled, so the no-JS fallback keeps working.
(function () {
  'use strict';

  var TRANSFORM_URL = '/admin/rules/branch/transform';

  function getToken() {
    var el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
  }

  function editorForm() {
    return document.querySelector('form[data-rules-editor-form]');
  }

  // --- Field-change enhancement -------------------------------------------------
  // Changing the Field <select> clicks the hidden "Update field" fallback button,
  // which submits a setField transform. That submit is caught by the interceptor
  // below, so it flows through the same async path as every other edit.
  function applyFieldChange(select) {
    var group = select.closest('.rules-condition__field');
    if (!group) return;
    var button = group.querySelector('.rules-condition__apply');
    if (button) button.click();
  }

  document.addEventListener('change', function (event) {
    var target = event.target;
    if (target && target.matches && target.matches('select[data-rules-field]')) {
      applyFieldChange(target);
    }
  });

  // --- Async submit interception ------------------------------------------------
  document.addEventListener('submit', function (event) {
    var form = event.target;
    if (!form || !form.matches || !form.matches('form[data-rules-editor-form]')) return;
    if (!document.body.classList.contains('js-enabled')) return; // no-JS fallback owns this

    var submitter = event.submitter;
    // A transform button carries its own formaction attribute; the Save button has none and uses
    // the form's action. Read the ATTRIBUTE, not the .formAction property — the property returns
    // the document URL (not the form action) when the attribute is absent, which would 405.
    var formAction = submitter ? submitter.getAttribute('formaction') : null;
    var endpoint = formAction || form.action;
    var action = submitter ? submitter.value : 'save';

    event.preventDefault();

    var body = new URLSearchParams(new FormData(form));
    body.set('Action', action); // the clicked button's name/value isn't auto-included

    fetch(endpoint, {
      method: 'POST',
      headers: {
        'X-XSRF-TOKEN': getToken(),
        'X-Requested-With': 'XMLHttpRequest'
      },
      body: body
    }).then(function (response) {
      var isSave = endpoint.indexOf('/branch/save') >= 0;
      return isSave ? handleSave(response) : handleTransform(response);
    }).catch(function (err) {
      console.error('Rules editor request failed:', err);
      showMessage('<div class="govuk-error-summary" data-module="govuk-error-summary"><div role="alert">' +
        '<h2 class="govuk-error-summary__title">There is a problem</h2>' +
        '<div class="govuk-error-summary__body"><p class="govuk-body">Something went wrong. ' +
        'Reload the page and try again.</p></div></div></div>');
    });
  });

  // --- Transform: swap the condition tree --------------------------------------
  function handleTransform(response) {
    return response.text().then(function (html) {
      if (!response.ok) { console.error('Transform failed:', response.status); return; }
      var container = document.getElementById('rules-condition-editor');
      if (container) {
        container.innerHTML = html;
        reinit(container);
      }
      clearMessages();
    });
  }

  // --- Save: JSON on success, HTML messages fragment on failure -----------------
  function handleSave(response) {
    var contentType = response.headers.get('content-type') || '';
    if (contentType.indexOf('application/json') >= 0) {
      return response.json().then(function (data) {
        if (data && data.ok) {
          var etag = document.getElementById('LoadETag');
          if (etag && typeof data.newETag !== 'undefined' && data.newETag !== null) {
            etag.value = data.newETag;
          }
          clearMessages();
          showToast(data.message || 'Saved');
        }
      });
    }
    // Validation error or concurrency conflict — server returned the banners fragment.
    return response.text().then(function (html) { showMessage(html); });
  }

  // --- Messages region ----------------------------------------------------------
  function messagesRegion() { return document.getElementById('rules-edit-messages'); }

  function showMessage(html) {
    var region = messagesRegion();
    if (!region) return;
    region.innerHTML = html;
    reinit(region);
    var alert = region.querySelector('[role="alert"]');
    if (alert) {
      // Make the banner focusable so keyboard/AT users land on the problem.
      alert.setAttribute('tabindex', '-1');
      alert.focus();
    }
  }

  function clearMessages() {
    var region = messagesRegion();
    if (region) region.innerHTML = '';
  }

  // --- "Saved" toast ------------------------------------------------------------
  // A non-modal, non-focus-trapping notification: role="status" announces "Saved"
  // politely, and the CSS fades it out over ~3s. We remove the node on animationend
  // (or a timeout fallback) so repeated saves don't stack stale toasts.
  function showToast(text) {
    var region = document.getElementById('rules-toast-region');
    if (!region) return;
    region.innerHTML = '';

    var toast = document.createElement('div');
    toast.className = 'rules-toast';
    toast.setAttribute('role', 'status');
    toast.textContent = text;
    region.appendChild(toast);

    var removed = false;
    function remove() {
      if (removed) return;
      removed = true;
      if (toast.parentNode) toast.parentNode.removeChild(toast);
    }
    toast.addEventListener('animationend', remove);
    window.setTimeout(remove, 3500); // fallback if animation doesn't fire (e.g. reduced motion)
  }

  // Re-run GOV.UK Frontend component init over freshly injected markup.
  function reinit(scope) {
    if (window.GOVUKFrontend && typeof window.GOVUKFrontend.initAll === 'function') {
      try { window.GOVUKFrontend.initAll(scope ? { scope: scope } : undefined); }
      catch (e) { /* initAll signature varies by version; ignore */ }
    }
  }
})();
