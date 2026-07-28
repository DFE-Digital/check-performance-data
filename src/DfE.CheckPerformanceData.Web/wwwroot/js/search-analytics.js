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

// Interactive tile switcher for the /admin/Search/ dashboard. Clicking a tile swaps the
// chart panel(s) below to that tile's own series. Progressive-enhancement only: with JS
// disabled every chart panel remains visible stacked (the server never hides them).
//
// The map is many-to-many: multiple panels can share a data-sa-panel key so a single
// tile click reveals more than one panel at once. The "latency" key drives BOTH the
// latency-percentiles chart AND the request-timings scatter — plotted in that order on
// the server so a keyboard user tabbing through sees percentiles first, then the raw
// timings. This is the simplest path to the two-panel-per-tile shape asked for by the
// acceptance: keep the tile → key mapping unchanged, let two panels share the "latency"
// key, and the same activate(key) loop toggles both.
(function () {
    'use strict';
    var tiles = document.querySelectorAll('button.sa-tile[data-sa-tile]');
    var panels = document.querySelectorAll('[data-sa-panel]');
    if (tiles.length === 0 || panels.length === 0) { return; }

    function activate(key) {
        for (var i = 0; i < tiles.length; i++) {
            var isActive = tiles[i].getAttribute('data-sa-tile') === key;
            tiles[i].setAttribute('aria-pressed', isActive ? 'true' : 'false');
            if (isActive) {
                tiles[i].classList.add('sa-tile--active');
            } else {
                tiles[i].classList.remove('sa-tile--active');
            }
        }
        for (var j = 0; j < panels.length; j++) {
            var isVisible = panels[j].getAttribute('data-sa-panel') === key;
            panels[j].hidden = !isVisible;
            if (isVisible) {
                panels[j].classList.remove('sa-chart-panel--hidden');
            } else {
                panels[j].classList.add('sa-chart-panel--hidden');
            }
        }
    }

    // Hide the non-default panels only after JS wires the switcher — with JS off, every
    // chart stays visible so the page still tells the truth.
    var defaultKey = null;
    for (var t = 0; t < tiles.length; t++) {
        if (tiles[t].getAttribute('aria-pressed') === 'true') {
            defaultKey = tiles[t].getAttribute('data-sa-tile');
            break;
        }
    }
    if (defaultKey === null && tiles[0]) {
        defaultKey = tiles[0].getAttribute('data-sa-tile');
    }
    if (defaultKey !== null) { activate(defaultKey); }

    for (var k = 0; k < tiles.length; k++) {
        (function (tile) {
            tile.addEventListener('click', function () {
                activate(tile.getAttribute('data-sa-tile'));
            });
        })(tiles[k]);
    }
})();
