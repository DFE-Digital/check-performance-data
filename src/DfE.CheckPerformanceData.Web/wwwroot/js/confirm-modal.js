(function () {
    'use strict';

    // ====================================================================
    //  Confirm modal — trigger / cancel / backdrop / focus-trap wiring
    //
    //  Structure (rendered by GovukConfirmModalTagHelper):
    //
    //      dialog.govuk-modal-dialogue__box  (opened via .showModal())
    //        .govuk-modal-dialogue__header   (black band + X close)
    //        .govuk-modal-dialogue__content  (heading, form, body, buttons)
    //
    //  Backdrop is the native ::backdrop pseudo. We can't use a sibling
    //  element because .showModal() makes everything outside the dialog
    //  inert and unclickable. Backdrop clicks bubble to the dialog with
    //  e.target === dialog (children intercept their own clicks).
    //
    //  The Tab focus-trap is explicit because Chromium's native trap leaks
    //  to <body> when autofocus targets the last focusable in DOM order.
    // ====================================================================

    function focusable(dialog) {
        var sel = 'button:not([disabled]), [href], input:not([type="hidden"]):not([disabled]),' +
                  ' select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
        var nodes = dialog.querySelectorAll(sel);
        var out = [];
        for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            if (el.offsetParent !== null || el === document.activeElement) {
                out.push(el);
            }
        }
        return out;
    }

    function wireFocusTrap(dialog) {
        dialog.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;

            var els = focusable(dialog);
            if (els.length === 0) return;

            var first = els[0];
            var last = els[els.length - 1];
            var active = document.activeElement;

            if (e.shiftKey) {
                if (active === first || !dialog.contains(active)) {
                    e.preventDefault();
                    last.focus();
                }
            } else {
                if (active === last || !dialog.contains(active)) {
                    e.preventDefault();
                    first.focus();
                }
            }
        });
    }

    // Trigger click → open the target dialog.
    document.addEventListener('click', function (e) {
        var trigger = e.target.closest('[data-confirm-trigger]');
        if (!trigger) return;

        var dialogId = trigger.getAttribute('data-confirm-trigger');
        var dialog = document.getElementById(dialogId);
        if (dialog && typeof dialog.showModal === 'function') {
            dialog.showModal();
            var cancelBtn = dialog.querySelector('[data-confirm-cancel]');
            if (cancelBtn) cancelBtn.focus();
        }
    });

    // Cancel link click → close the dialog. The href="#" must be intercepted.
    document.addEventListener('click', function (e) {
        var cancel = e.target.closest('[data-confirm-cancel]');
        if (!cancel) return;

        e.preventDefault();

        var dialog = cancel.closest('dialog');
        if (dialog) dialog.close();
    });

    // X header close button.
    document.addEventListener('click', function (e) {
        var closer = e.target.closest('[data-modal-close]');
        if (!closer) return;

        var dialog = closer.closest('dialog');
        if (dialog) dialog.close();
    });

    // Backdrop click + focus-trap wiring per dialog.
    // The backdrop is the dialog's ::backdrop pseudo — clicks on it bubble to
    // the dialog element with e.target === dialog (the form/heading/buttons
    // sit inside the dialog and intercept their own clicks).
    var dialogs = document.querySelectorAll('dialog.govuk-modal-dialogue__box');
    for (var i = 0; i < dialogs.length; i++) {
        (function (dialog) {
            dialog.addEventListener('click', function (e) {
                if (e.target === dialog) dialog.close();
            });
            wireFocusTrap(dialog);
        })(dialogs[i]);
    }
})();
