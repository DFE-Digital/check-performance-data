// Progressive-enhancement filter for the admin settings table.
// The full table renders server-side; this script hides rows whose combined key + value
// text does not contain the query. When JS is disabled the input stays inert and the
// table renders unfiltered — nothing else on the page depends on this script running.
(function () {
    'use strict';

    function init() {
        var input = document.getElementById('settings-filter');
        var table = document.getElementById('settings-table');
        var empty = document.getElementById('settings-filter-empty');
        var emptyTerm = document.getElementById('settings-filter-empty-term');
        if (!input || !table || !empty || !emptyTerm) {
            return;
        }

        var rows = table.querySelectorAll('tbody tr');

        function apply() {
            var query = (input.value || '').trim().toLowerCase();
            var visible = 0;
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var text = (row.textContent || '').toLowerCase();
                var match = query === '' || text.indexOf(query) !== -1;
                row.hidden = !match;
                if (match) {
                    visible++;
                }
            }

            if (query !== '' && visible === 0) {
                emptyTerm.textContent = '"' + input.value.trim() + '"';
                empty.hidden = false;
                table.hidden = true;
            } else {
                empty.hidden = true;
                table.hidden = false;
            }
        }

        input.addEventListener('input', apply);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
