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

// Hover crosshair + tooltip for every chart on the search-analytics surface. Vanilla JS
// only — the SVG is server-rendered; this progressive-enhancement layer adds a full-height
// vertical + full-width horizontal dashed guide line plus a floating tooltip. Two snap
// modes, both driven off the same code path so the DOM/positioning logic is not duplicated
// per chart partial:
//
//   * mode="bucket" (default): the SVG carries data-sa-buckets (ISO timestamp array) +
//     data-sa-values (numeric array). Cursor X maps to the nearest bucket index. Tooltip
//     reads "<formatted time> — <value><suffix>" (e.g. "Wed 24 Jul, 14:00 — 32 searches").
//
//   * mode="scatter": the SVG carries data-sa-scatter-points, a JSON array of
//     { x: svgX, y: svgY, ts: iso, ms: number, q: string|null } — pre-computed by the
//     partial in SVG-viewBox pixel space. Cursor snaps to the nearest point by Euclidean
//     distance. Tooltip is three lines: formatted time / "<ms> ms" / query text (or
//     "(empty query)" fallback when q is null/empty).
//
// With JS off both flows degrade to a static SVG with no hover affordance.
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

    // Format a scatter point's timestamp — always includes seconds so a dot that plots
    // very close to another can be told apart. Whole-second precision matches the
    // 'u' format the drill-in table renders under the chart.
    function formatScatterTimestamp(iso) {
        var d = new Date(iso);
        if (isNaN(d.getTime())) { return iso; }
        var opts = {
            weekday: 'short', day: 'numeric', month: 'short',
            hour: '2-digit', minute: '2-digit', second: '2-digit',
            timeZone: 'UTC',
        };
        try { return d.toLocaleString('en-GB', opts) + ' UTC'; } catch (e) { return iso; }
    }

    // Draw + return one crosshair line element. Kept a helper so bucket + scatter modes
    // do not duplicate the SVG-namespace attribute plumbing.
    function makeCrosshairLine(svg) {
        var svgNs = 'http://www.w3.org/2000/svg';
        var line = document.createElementNS(svgNs, 'line');
        line.setAttribute('class', 'sa-chart__crosshair-line');
        line.setAttribute('stroke', '#505a5f');
        line.setAttribute('stroke-width', '1');
        line.setAttribute('stroke-dasharray', '3 3');
        line.setAttribute('pointer-events', 'none');
        line.setAttribute('visibility', 'hidden');
        svg.appendChild(line);
        return line;
    }

    // Place the tooltip in the upper-left quadrant of the cursor by default so the
    // cursor tip (upper-left of most pointer glyphs) never occludes the first line of
    // the readout. When the cursor is close to the viewport top / left edge the tooltip
    // would clip out; flip to another quadrant so the whole tooltip stays visible:
    //
    //     ┌──────────┐            ┌──────────┐
    //     │ tooltip  │            │ tooltip  │
    //     └────────┐/│            │\┌────────┘
    //              *                * ← cursor
    //     upper-left (default)     upper-right (LEFT-edge flip)
    //
    //     * cursor                * cursor
    //     │┌────────┐             ┌────────┐│
    //     ││ tooltip│             │ tooltip││
    //     │└────────┘             └────────┘│
    //     lower-left (TOP-edge flip)     lower-right (both flips)
    //
    // The 10 px offset keeps the tooltip's nearest corner from touching the cursor.
    function positionTooltip(evt, tt) {
        var offset = 10;
        // Read the rendered size after content is set so getBoundingClientRect gives us
        // the true measure of the box we're placing.
        var box = tt.getBoundingClientRect();
        var vw = window.innerWidth || document.documentElement.clientWidth;
        var vh = window.innerHeight || document.documentElement.clientHeight;

        // Default quadrant: upper-left of cursor.
        var left = evt.clientX - box.width - offset;
        var top = evt.clientY - box.height - offset;

        // If it would clip past the LEFT edge, flip horizontally.
        if (left < 0) { left = evt.clientX + offset; }
        // If it would clip past the TOP edge, flip vertically.
        if (top < 0) { top = evt.clientY + offset; }

        // Belt-and-braces: keep the tooltip inside the RIGHT / BOTTOM edges after either
        // flip in case an aggressive cursor position pushes it off. Clamp instead of flipping
        // again so we never toggle-loop on a small viewport.
        if (left + box.width > vw) { left = Math.max(0, vw - box.width - offset); }
        if (top + box.height > vh) { top = Math.max(0, vh - box.height - offset); }

        tt.style.left = left + 'px';
        tt.style.top = top + 'px';
    }

    // Cursor coord → SVG-viewBox coord. Prefer the native getScreenCTM path; fall back
    // to a proportional map when the browser hasn't computed a CTM yet.
    function toSvgCoords(svg, evt) {
        try {
            var pt = svg.createSVGPoint();
            pt.x = evt.clientX; pt.y = evt.clientY;
            var ctm = svg.getScreenCTM();
            if (ctm) return pt.matrixTransform(ctm.inverse());
        } catch (e) { /* fall through */ }
        var rect = svg.getBoundingClientRect();
        var vb = svg.viewBox && svg.viewBox.baseVal;
        if (!vb || rect.width === 0 || rect.height === 0) return null;
        return {
            x: vb.x + (evt.clientX - rect.left) * (vb.width / rect.width),
            y: vb.y + (evt.clientY - rect.top)  * (vb.height / rect.height)
        };
    }

    // Bucket-mode attachment — the historical behaviour. Snaps cursor X to the nearest
    // bucket index and reads the pre-computed values[] entry at that index. Kept
    // separate from the scatter branch so the two snap strategies don't inter-tangle.
    function attachBucket(svg) {
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

        var vLine = makeCrosshairLine(svg);
        var hLine = makeCrosshairLine(svg);

        svg.addEventListener('mousemove', function (evt) {
            var p = toSvgCoords(svg, evt);
            if (!p) return;
            if (p.x < padLeft || p.x > padLeft + plotW || p.y < padTop || p.y > padTop + plotH) {
                vLine.setAttribute('visibility', 'hidden');
                hLine.setAttribute('visibility', 'hidden');
                tooltip.style.display = 'none';
                return;
            }
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
            positionTooltip(evt, tooltip);
        });

        svg.addEventListener('mouseleave', function () {
            vLine.setAttribute('visibility', 'hidden');
            hLine.setAttribute('visibility', 'hidden');
            tooltip.style.display = 'none';
        });
    }

    // Scatter-mode attachment — every dot's SVG (x, y) is pre-baked into
    // data-sa-scatter-points along with the source-of-truth (ts, ms, q). Snapping is
    // nearest point by Euclidean distance, so a dense scatter still gives the user a
    // predictable target. Tooltip is three lines (time / latency / query) rendered
    // via innerHTML with escaped values so a query like <script> can't inject.
    function attachScatter(svg) {
        var padLeft = parseFloat(svg.getAttribute('data-sa-plot-left')) || 0;
        var padTop = parseFloat(svg.getAttribute('data-sa-plot-top')) || 0;
        var plotW = parseFloat(svg.getAttribute('data-sa-plot-width')) || 0;
        var plotH = parseFloat(svg.getAttribute('data-sa-plot-height')) || 0;

        var points;
        try {
            points = JSON.parse(svg.getAttribute('data-sa-scatter-points') || '[]');
        } catch (e) { return; }
        if (!points.length) { return; }

        var vLine = makeCrosshairLine(svg);
        var hLine = makeCrosshairLine(svg);

        function escapeHtml(s) {
            return String(s == null ? '' : s)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function nearest(cursorSvg) {
            // Squared-distance comparison — no sqrt, since we're only ranking.
            var best = 0;
            var bestD = Infinity;
            for (var i = 0; i < points.length; i++) {
                var dx = points[i].x - cursorSvg.x;
                var dy = points[i].y - cursorSvg.y;
                var d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        svg.addEventListener('mousemove', function (evt) {
            var p = toSvgCoords(svg, evt);
            if (!p) return;
            if (p.x < padLeft || p.x > padLeft + plotW || p.y < padTop || p.y > padTop + plotH) {
                vLine.setAttribute('visibility', 'hidden');
                hLine.setAttribute('visibility', 'hidden');
                tooltip.style.display = 'none';
                return;
            }
            var idx = nearest(p);
            var pt = points[idx];

            vLine.setAttribute('x1', pt.x);
            vLine.setAttribute('x2', pt.x);
            vLine.setAttribute('y1', padTop);
            vLine.setAttribute('y2', padTop + plotH);
            vLine.setAttribute('visibility', 'visible');

            hLine.setAttribute('x1', padLeft);
            hLine.setAttribute('x2', padLeft + plotW);
            hLine.setAttribute('y1', pt.y);
            hLine.setAttribute('y2', pt.y);
            hLine.setAttribute('visibility', 'visible');

            var whenLabel = formatScatterTimestamp(pt.ts);
            var msLabel = (pt.ms || 0).toLocaleString('en-GB') + ' ms';
            var qLabel = (pt.q == null || pt.q === '') ? '(empty query)' : '"' + pt.q + '"';
            tooltip.innerHTML =
                escapeHtml(whenLabel) + '<br>' +
                escapeHtml(msLabel) + '<br>' +
                'query: ' + escapeHtml(qLabel);
            tooltip.style.display = 'block';
            positionTooltip(evt, tooltip);
        });

        svg.addEventListener('mouseleave', function () {
            vLine.setAttribute('visibility', 'hidden');
            hLine.setAttribute('visibility', 'hidden');
            tooltip.style.display = 'none';
        });
    }

    for (var i = 0; i < svgs.length; i++) {
        var mode = (svgs[i].getAttribute('data-sa-crosshair-mode') || 'bucket').toLowerCase();
        if (mode === 'scatter') { attachScatter(svgs[i]); }
        else { attachBucket(svgs[i]); }
    }
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
