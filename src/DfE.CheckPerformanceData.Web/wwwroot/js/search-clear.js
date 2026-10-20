// The search widget's optional clear button. Without JavaScript it is a reset button, which empties
// the box on its own. With it, the button is hidden while there is nothing to clear, and clearing
// puts the visitor back in the box.
(function () {
    'use strict';

    function bind(form) {
        var button = form.querySelector('[data-cypmd-search-clear]');
        if (!button || button.getAttribute('data-cypmd-clear-ready')) return;
        button.setAttribute('data-cypmd-clear-ready', 'true');

        // Instant search swaps the box for its own, so the box is looked up each time.
        function box() { return form.querySelector('input[name="q"]'); }

        function update() {
            var field = box();
            button.hidden = !field || field.value === '';
        }

        form.addEventListener('input', update);

        button.addEventListener('click', function (event) {
            event.preventDefault();
            var field = box();
            if (!field) return;
            field.value = '';
            // Instant search keeps its own copy of the text and closes its suggestions when told
            // the box has changed.
            field.dispatchEvent(new Event('input', { bubbles: true }));
            field.focus();
            update();
        });

        update();
    }

    function init() {
        Array.prototype.forEach.call(document.querySelectorAll('form.cypmd-search'), bind);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
