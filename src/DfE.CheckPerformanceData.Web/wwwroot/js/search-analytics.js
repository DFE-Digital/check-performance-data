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

// Hover crosshair + tooltip for the primary dashboard charts. Vanilla JS only — the SVG
// is server-rendered; this progressive-enhancement layer adds a full-height vertical +
// full-width horizontal dashed guide line plus a floating tooltip. When the cursor is
// within snap range of a data bucket the tooltip reads the mapped X-value (formatted
// time) and Y-value ("32 searches", "150 ms", etc). With JS off the chart still renders
// fine, minus the hover affordance.
(function () {
    'use strict';
    var svgs = document.querySelectorAll('svg.sa-chart[data-sa-crosshair="true"]');
    if (svgs.length === 0) { return; }

    // One shared tooltip element positioned near the cursor. Appended to body so it can
    // escape any scrolling / overflow container. pointer-events:none so it never grabs
    // the mouse from the SVG.
    var tooltip = document.createElement('div');
    tooltip.className = 'sa-chart__crosshair-tooltip';
    tooltip.style.display = 'none';
    tooltip.setAttribute('role', 'tooltip');
    document.body.appendChild(tooltip);

    // Aggregate-to-typical-week mode is a page-scope property (a full reload flips it),
    // so read it once at module init from the URL. In aggregate mode the bucket
    // timestamps are anchored to a synthetic Monday (2001-01-01 UTC) — the anchor date
    // is a rendering artefact, not real user data, so tooltips must NOT show it.
    var aggregateMode = false;
    try {
        var params = new URLSearchParams(window.location.search);
        aggregateMode = (params.get('aggregate') || '').toLowerCase() === 'week';
    } catch (e) { /* older browsers — fall through, non-aggregate default is safe */ }

    function formatTimestamp(iso, windowSpanMs) {
        var d = new Date(iso);
        if (isNaN(d.getTime())) { return iso; }
        var opts;
        if (aggregateMode) {
            // Aggregate mode — weekday + HH:mm only. The date part of the ISO string is a
            // synthetic anchor (a Monday in 2001), so rendering it would confuse the reader
            // ("why is my search from January 2001?"). UTC forced so the WSL2 / UK Docker
            // host still surfaces "Monday" and not the previous day off a negative offset.
            opts = { weekday: 'long', hour: '2-digit', minute: '2-digit', timeZone: 'UTC' };
        } else if (windowSpanMs <= 24 * 3600 * 1000) {
            opts = { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' };
        } else if (windowSpanMs <= 90 * 24 * 3600 * 1000) {
            opts = { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' };
        } else {
            opts = { day: 'numeric', month: 'short', year: 'numeric' };
        }
        try { return d.toLocaleString('en-GB', opts); } catch (e) { return iso; }
    }

    function attachHover(svg) {
        var padLeft = parseFloat(svg.getAttribute('data-sa-plot-left')) || 0;
        var padTop = parseFloat(svg.getAttribute('data-sa-plot-top')) || 0;
        var plotW = parseFloat(svg.getAttribute('data-sa-plot-width')) || 0;
        var plotH = parseFloat(svg.getAttribute('data-sa-plot-height')) || 0;
        var suffix = svg.getAttribute('data-sa-value-suffix') || '';

        var buckets, values;
        try {
            buckets = JSON.parse(svg.getAttribute('data-sa-buckets') || '[]');
            values = JSON.parse(svg.getAttribute('data-sa-values') || '[]');
        } catch (e) { return; }
        if (!buckets.length) { return; }

        var windowSpanMs = 0;
        if (buckets.length >= 2) {
            windowSpanMs = new Date(buckets[buckets.length - 1]).getTime() - new Date(buckets[0]).getTime();
        }

        // Build the crosshair line elements once and hide them; toggle on mouse events.
        var svgNs = 'http://www.w3.org/2000/svg';
        var vLine = document.createElementNS(svgNs, 'line');
        vLine.setAttribute('class', 'sa-chart__crosshair-line');
        vLine.setAttribute('stroke', '#505a5f');
        vLine.setAttribute('stroke-width', '1');
        vLine.setAttribute('stroke-dasharray', '3 3');
        vLine.setAttribute('pointer-events', 'none');
        vLine.setAttribute('visibility', 'hidden');
        svg.appendChild(vLine);

        var hLine = document.createElementNS(svgNs, 'line');
        hLine.setAttribute('class', 'sa-chart__crosshair-line');
        hLine.setAttribute('stroke', '#505a5f');
        hLine.setAttribute('stroke-width', '1');
        hLine.setAttribute('stroke-dasharray', '3 3');
        hLine.setAttribute('pointer-events', 'none');
        hLine.setAttribute('visibility', 'hidden');
        svg.appendChild(hLine);

        function toSvgCoords(evt) {
            // Prefer SVG native coord conversion; fall back to a viewBox proportional
            // map when getScreenCTM is null (some browsers report null before layout).
            try {
                var pt = svg.createSVGPoint();
                pt.x = evt.clientX; pt.y = evt.clientY;
                var ctm = svg.getScreenCTM();
                if (ctm) return pt.matrixTransform(ctm.inverse());
            } catch (e) { /* fall through */ }
            // Fallback: linear interpolate over the SVG's bounding rect into viewBox coords.
            var rect = svg.getBoundingClientRect();
            var vb = svg.viewBox && svg.viewBox.baseVal;
            if (!vb || rect.width === 0 || rect.height === 0) return null;
            return {
                x: vb.x + (evt.clientX - rect.left) * (vb.width / rect.width),
                y: vb.y + (evt.clientY - rect.top)  * (vb.height / rect.height)
            };
        }

        svg.addEventListener('mousemove', function (evt) {
            var p = toSvgCoords(evt);
            if (!p) return;
            // Reject when outside the plot area.
            if (p.x < padLeft || p.x > padLeft + plotW || p.y < padTop || p.y > padTop + plotH) {
                vLine.setAttribute('visibility', 'hidden');
                hLine.setAttribute('visibility', 'hidden');
                tooltip.style.display = 'none';
                return;
            }
            // Nearest bucket to cursor X.
            var relX = (p.x - padLeft) / plotW;
            var idxRaw = Math.round(relX * (buckets.length - 1));
            if (idxRaw < 0) idxRaw = 0;
            if (idxRaw > buckets.length - 1) idxRaw = buckets.length - 1;

            var snappedX = padLeft + (plotW * idxRaw / Math.max(1, buckets.length - 1));

            vLine.setAttribute('x1', snappedX);
            vLine.setAttribute('x2', snappedX);
            vLine.setAttribute('y1', padTop);
            vLine.setAttribute('y2', padTop + plotH);
            vLine.setAttribute('visibility', 'visible');

            hLine.setAttribute('x1', padLeft);
            hLine.setAttribute('x2', padLeft + plotW);
            hLine.setAttribute('y1', p.y);
            hLine.setAttribute('y2', p.y);
            hLine.setAttribute('visibility', 'visible');

            var whenLabel = formatTimestamp(buckets[idxRaw], windowSpanMs);
            var valueLabel = (values[idxRaw] || 0).toLocaleString('en-GB') + suffix;
            tooltip.textContent = whenLabel + ' — ' + valueLabel;
            tooltip.style.display = 'block';
            tooltip.style.left = (evt.clientX + 12) + 'px';
            tooltip.style.top = (evt.clientY + 12) + 'px';
        });

        svg.addEventListener('mouseleave', function () {
            vLine.setAttribute('visibility', 'hidden');
            hLine.setAttribute('visibility', 'hidden');
            tooltip.style.display = 'none';
        });
    }

    for (var i = 0; i < svgs.length; i++) attachHover(svgs[i]);
})();

// Aggregate-to-typical-week toggle: submit the enclosing GET form the moment the
// checkbox flips so the admin does not need to click a separate Apply button. With JS
// off the <noscript> Apply button is the fallback path.
(function () {
    'use strict';
    var toggle = document.querySelector('input[data-sa-aggregate-toggle="true"]');
    if (!toggle) return;
    toggle.addEventListener('change', function () {
        var form = toggle.closest('form');
        if (form) form.submit();
    });
})();
