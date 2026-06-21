(function () {
    'use strict';

    // The real-time per-step averages strip. Refreshes the four figures from the stage-averages JSON
    // endpoint on a short interval and whenever the window dropdown changes, so the numbers track the
    // live pipeline without a page reload. Pure progressive enhancement: the server renders an initial
    // set, and this only updates them.

    var REFRESH_MS = 10000;

    function fmt(ms) {
        if (ms == null || isNaN(ms)) { return '—'; } // em dash
        return ms >= 1000 ? (ms / 1000).toFixed(1) + 's' : Math.round(ms) + 'ms';
    }

    function init() {
        var root = document.querySelector('[data-obs-stats]');
        if (!root) { return; }

        var url = root.getAttribute('data-obs-stats-url');
        var windowSelect = root.querySelector('[data-obs-stats-window]');

        function apply(data) {
            if (!data) { return; }
            ['rulesQueueMs', 'rulesEngineMs', 'zendeskQueueMs', 'ticketMs'].forEach(function (key) {
                var el = root.querySelector('[data-obs-stat="' + key + '"]');
                if (el) { el.textContent = fmt(data[key]); }
            });
        }

        function refresh() {
            var w = windowSelect ? windowSelect.value : '';
            fetch(url + '?window=' + encodeURIComponent(w), {
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(apply)
                .catch(function () { /* leave the last figures in place on a transient failure */ });
        }

        if (windowSelect) {
            windowSelect.addEventListener('change', refresh);
        }
        window.setInterval(refresh, REFRESH_MS);
        // A first refresh shortly after load so a window change before the first tick still reflects.
        window.setTimeout(refresh, 1000);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
