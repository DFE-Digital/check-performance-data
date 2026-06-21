(function () {
    'use strict';

    // The custom from/to date inputs only make sense when a range <select> is set to "Custom range".
    // Each such select is marked data-obs-range-select and toggles the [data-obs-custom-range] groups
    // within its nearest form (the chart controls form and the seed form each scope their own). The
    // groups carry the hidden attribute server-side; this keeps them in sync as the selection changes.
    // Progressive enhancement: with JS off the server renders the custom inputs visible when the
    // selected range already is custom, so the form still works.

    function scopeFor(select) {
        return select.closest('form') || select.parentElement;
    }

    function sync(select) {
        var scope = scopeFor(select);
        if (!scope) { return; }
        var isCustom = (select.value || '').toLowerCase() === 'custom';
        var groups = scope.querySelectorAll('[data-obs-custom-range]');
        for (var i = 0; i < groups.length; i++) {
            if (isCustom) { groups[i].removeAttribute('hidden'); }
            else { groups[i].setAttribute('hidden', ''); }
        }
    }

    function init() {
        var selects = document.querySelectorAll('[data-obs-range-select]');
        for (var i = 0; i < selects.length; i++) {
            (function (select) {
                sync(select);
                select.addEventListener('change', function () { sync(select); });
            })(selects[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
