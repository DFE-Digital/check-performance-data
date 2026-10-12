// Progressive-enhancement filter for the CMS page scope picker (the checkbox list of pages in the
// Search and Search results widget editors). The full list renders server-side with the filter
// controls hidden; this script reveals them and hides list items that do not match. It only ever
// sets `hidden` on items: it never unticks, disables, removes or reorders a checkbox, so a ticked
// page that the filter hides is still posted with the widget form. Each picker on the page is
// filtered on its own. With JavaScript off the controls stay hidden and the list is unfiltered.
(function () {
    'use strict';

    function initPicker(picker) {
        var wrapper = picker.querySelector('[data-cpb-scope-filter]');
        var input = wrapper ? wrapper.querySelector('input[type="search"]') : null;
        var selectedOnly = picker.querySelector('[data-cpb-scope-selected-only]');
        var status = picker.querySelector('[data-cpb-scope-status]');
        var empty = picker.querySelector('[data-cpb-scope-empty]');
        var items = picker.querySelectorAll('.cpb-scope-picker__item');
        if (!wrapper || !input || !selectedOnly || !status || !empty || items.length === 0) {
            return;
        }

        function apply() {
            var query = (input.value || '').trim().toLowerCase();
            var onlySelected = selectedOnly.checked;
            var visible = 0;
            var selected = 0;
            for (var i = 0; i < items.length; i++) {
                var item = items[i];
                var box = item.querySelector('input[name="scopePages"]');
                var ticked = !!box && box.checked;
                if (ticked) {
                    selected++;
                }
                var text = item.getAttribute('data-filter') || '';
                var show = (query === '' || text.indexOf(query) !== -1) && (!onlySelected || ticked);
                item.hidden = !show;
                if (show) {
                    visible++;
                }
            }

            status.textContent = 'Showing ' + visible + ' of ' + items.length +
                (items.length === 1 ? ' page. ' : ' pages. ') + selected + ' selected.';
            empty.hidden = visible !== 0;
        }

        input.addEventListener('input', apply);
        input.addEventListener('keydown', function (event) {
            // Enter in the filter must not submit the widget form around it.
            if (event.key === 'Enter') {
                event.preventDefault();
            }
        });
        selectedOnly.addEventListener('change', apply);
        // Ticking a page changes the selected count (and, with "only selected" on, what is listed).
        picker.addEventListener('change', function (event) {
            var target = event.target;
            if (target && target.name === 'scopePages') {
                apply();
            }
        });

        wrapper.hidden = false;
        apply();
    }

    function init() {
        var pickers = document.querySelectorAll('[data-cpb-scope-picker]');
        for (var i = 0; i < pickers.length; i++) {
            initPicker(pickers[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
