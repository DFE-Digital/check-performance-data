// Client-side text filter for the search-analytics drill-in tables. Progressive-enhancement
// only — the server has already rendered every row, so users without JS see the full page and
// paginate through the server-side pagination widget instead. Filtering happens over rows
// that are already in the DOM; there is no fetch / no round-trip / no revealing of hidden rows.
(function () {
    'use strict';
    var inputs = document.querySelectorAll('input[data-filter-target]');
    for (var i = 0; i < inputs.length; i++) {
        (function (input) {
            var tableId = input.getAttribute('data-filter-target');
            var table = document.getElementById(tableId);
            if (!table) { return; }
            input.addEventListener('input', function () {
                var term = (input.value || '').toLowerCase();
                var rows = table.querySelectorAll('tr[data-filter-key]');
                for (var j = 0; j < rows.length; j++) {
                    var key = (rows[j].getAttribute('data-filter-key') || '').toLowerCase();
                    rows[j].hidden = term.length > 0 && key.indexOf(term) === -1;
                }
            });
        })(inputs[i]);
    }
})();
