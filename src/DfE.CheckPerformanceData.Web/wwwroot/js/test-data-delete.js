(function () {
    'use strict';

    // Danger-zone delete wiring for the Test data admin page.
    //
    // Two destructive actions, each behind a modal confirmation:
    //   * Delete seeded — plain confirm.
    //   * Delete all — typed-confirmation gate (input must equal 'DELETE' exactly).
    //
    // Progressive enhancement: with JS disabled, both forms submit synchronously — the
    // "delete-all" form ships a hidden `confirm=DELETE` input so the server-side
    // typed-confirmation guard still passes on the JS-less fallback. This trades some
    // safety for accessibility; the modal is the primary UX for JS-enabled users, and
    // the surface itself is dev-only. With JS enabled we intercept both submit-button
    // clicks, open the corresponding <dialog>, and only issue the POST after the user
    // confirms in the modal.

    var TYPED_LITERAL = 'DELETE';

    function el(selector, root) {
        return (root || document).querySelector(selector);
    }

    function tokenFromForm(form) {
        var input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    // Native <dialog>.showModal / close, mirrors the seed-progress-modal open path.
    // Modal close buttons carry data-modal-close so a single delegated handler drives
    // every close point without per-modal wiring.
    function openModal(dialog) {
        if (typeof dialog.showModal === 'function') {
            dialog.showModal();
        } else {
            dialog.setAttribute('open', '');
        }
    }

    function closeModal(dialog) {
        if (typeof dialog.close === 'function') {
            dialog.close();
        } else {
            dialog.removeAttribute('open');
        }
    }

    // Wires a per-modal close-button delegation: any element carrying data-modal-close
    // inside the dialog closes the dialog when clicked. Idempotent — safe to call
    // multiple times against the same dialog.
    function wireModalCloseButtons(dialog) {
        var closers = dialog.querySelectorAll('[data-modal-close]');
        for (var i = 0; i < closers.length; i++) {
            (function (btn) {
                if (btn.__wiredClose) return;
                btn.__wiredClose = true;
                btn.addEventListener('click', function () {
                    closeModal(dialog);
                });
            })(closers[i]);
        }
    }

    // Wires the Delete-seeded form: intercept submit, open the modal, POST from the
    // Confirm button. On success (302 -> 200 on the follow-up GET), reload the page so
    // the TempData banner renders.
    function wireDeleteSeeded() {
        var form = document.getElementById('delete-seeded-form');
        var modal = document.getElementById('delete-seeded-modal');
        if (!form || !modal) return;

        wireModalCloseButtons(modal);

        form.addEventListener('submit', function (evt) {
            evt.preventDefault();
            openModal(modal);
        });

        var confirmBtn = modal.querySelector('[data-role="delete-seeded-confirm"]');
        if (!confirmBtn) return;

        confirmBtn.addEventListener('click', function () {
            var token = tokenFromForm(form);
            confirmBtn.setAttribute('disabled', 'disabled');
            var body = new FormData();
            if (token) body.append('__RequestVerificationToken', token);
            fetch(form.action, {
                method: 'POST',
                body: body,
                credentials: 'same-origin',
                redirect: 'follow'
            }).then(function () {
                closeModal(modal);
                window.location.reload();
            }).catch(function () {
                confirmBtn.removeAttribute('disabled');
            });
        });
    }

    // Wires the Delete-all form: intercept submit, open the typed-confirmation modal,
    // enable the Confirm button only when the input value equals 'DELETE' (case-
    // sensitive). Fetch POST with the typed literal in the `confirm` field so the
    // server-side guard has the same value the user typed.
    function wireDeleteAll() {
        var form = document.getElementById('delete-all-form');
        var modal = document.getElementById('delete-all-modal');
        if (!form || !modal) return;

        wireModalCloseButtons(modal);

        var input = modal.querySelector('[data-role="delete-all-confirm-input"]');
        var confirmBtn = modal.querySelector('[data-role="delete-all-confirm"]');
        if (!input || !confirmBtn) return;

        // Reset the input each time the modal opens so an aborted attempt does not
        // leave the button enabled from a prior valid typing.
        form.addEventListener('submit', function (evt) {
            evt.preventDefault();
            input.value = '';
            confirmBtn.setAttribute('disabled', 'disabled');
            openModal(modal);
            input.focus();
        });

        input.addEventListener('input', function () {
            if (input.value === TYPED_LITERAL) {
                confirmBtn.removeAttribute('disabled');
            } else {
                confirmBtn.setAttribute('disabled', 'disabled');
            }
        });

        confirmBtn.addEventListener('click', function () {
            if (input.value !== TYPED_LITERAL) return;
            var token = tokenFromForm(form);
            confirmBtn.setAttribute('disabled', 'disabled');
            var body = new FormData();
            if (token) body.append('__RequestVerificationToken', token);
            body.append('confirm', TYPED_LITERAL);
            fetch(form.action, {
                method: 'POST',
                body: body,
                credentials: 'same-origin',
                redirect: 'follow'
            }).then(function () {
                closeModal(modal);
                window.location.reload();
            }).catch(function () {
                confirmBtn.removeAttribute('disabled');
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        wireDeleteSeeded();
        wireDeleteAll();
    });
})();
