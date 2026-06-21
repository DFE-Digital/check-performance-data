(function () {
    'use strict';

    // The self load-test (R9-3). Opening the modal pauses the board animation (it competes for the
    // same browser horsepower), then driving escalating message rates and measuring how fast each
    // batch drains finds the rough throughput knee for this environment. Live grid + throughput chart;
    // results export to Excel. Dev-gated server-side, and the modal only renders when demo tools are on.

    // The rate ladder, climbing geometrically so we cross the knee quickly.
    var LADDER = [1, 2, 4, 10, 20, 50, 100];
    var ENDPOINT = '/dev/uat/load-test';
    var EXPORT_ENDPOINT = '/dev/uat/load-test/export.xlsx';

    function token(modal) {
        var el = modal.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function fmtMs(ms) {
        if (ms == null || isNaN(ms)) { return '—'; }
        return ms >= 1000 ? (ms / 1000).toFixed(1) + 's' : Math.round(ms) + 'ms';
    }

    function init() {
        var trigger = document.querySelector('[data-obs-loadtest]');
        var modal = document.querySelector('[data-obs-loadtest-modal]');
        if (!trigger || !modal || typeof modal.showModal !== 'function') { return; }

        var startBtn = modal.querySelector('[data-obs-loadtest-start]');
        var stopBtn = modal.querySelector('[data-obs-loadtest-stop]');
        var exportBtn = modal.querySelector('[data-obs-loadtest-export]');
        var status = modal.querySelector('[data-obs-loadtest-status]');
        var optimum = modal.querySelector('[data-obs-loadtest-optimum]');
        var rows = modal.querySelector('[data-obs-loadtest-rows]');
        var chartHost = modal.querySelector('[data-obs-loadtest-chart-svg]');

        var running = false;
        var cancelled = false;
        var results = [];
        var pausedByMe = false;

        // Pause the board by toggling its pause control (if present and not already paused), so the
        // animation stops while the load test runs; resume it on close if we were the one to pause.
        function pauseBoard() {
            var pause = document.querySelector('[data-obs-pause]');
            if (pause && pause.getAttribute('aria-pressed') !== 'true') {
                pause.click();
                pausedByMe = true;
            }
        }
        function resumeBoard() {
            if (!pausedByMe) { return; }
            var pause = document.querySelector('[data-obs-pause]');
            if (pause && pause.getAttribute('aria-pressed') === 'true') { pause.click(); }
            pausedByMe = false;
        }

        trigger.addEventListener('click', function () {
            modal.showModal();
            pauseBoard();
        });

        modal.addEventListener('close', function () {
            cancelled = true;
            resumeBoard();
        });

        function cell(text, numeric) {
            var td = document.createElement('td');
            td.className = 'govuk-table__cell' + (numeric ? ' govuk-table__cell--numeric' : '');
            td.textContent = text;
            return td;
        }

        // A row appears as soon as a level STARTS, showing how many messages are being driven and a
        // "processing…" state; fillRow updates it in place once the batch has drained and the per-step
        // averages are known. So the user sees the rate go out, then the numbers come back, per level.
        function placeholderRow(rate) {
            var tr = document.createElement('tr');
            tr.className = 'govuk-table__row';
            tr.appendChild(cell(String(rate), false));
            tr.appendChild(cell('processing…', false));
            for (var i = 0; i < 6; i++) { tr.appendChild(cell('…', true)); }
            rows.appendChild(tr);
            return tr;
        }

        function fillRow(tr, r, isBest) {
            tr.className = 'govuk-table__row' + (isBest ? ' obs-loadtest__row--best' : '');
            tr.innerHTML = '';
            tr.appendChild(cell(String(r.rate) + (r.timedOut ? ' (timed out)' : ''), false));
            tr.appendChild(cell(r.completed + '/' + r.driven, true));
            tr.appendChild(cell(r.throughputPerSec != null ? r.throughputPerSec.toFixed(1) : '—', true));
            tr.appendChild(cell(fmtMs(r.elapsedMs), true));
            tr.appendChild(cell(fmtMs(r.rulesQueueMs), true));
            tr.appendChild(cell(fmtMs(r.rulesEngineMs), true));
            tr.appendChild(cell(fmtMs(r.zendeskQueueMs), true));
            tr.appendChild(cell(fmtMs(r.ticketMs), true));
        }

        function bestIndex() {
            var bi = -1, best = -1;
            results.forEach(function (r, i) {
                if (r.throughputPerSec != null && r.throughputPerSec > best) { best = r.throughputPerSec; bi = i; }
            });
            return bi;
        }

        // A simple SVG line chart: throughput (y) over rate (x), with the peak point ringed.
        function drawChart() {
            if (!results.length) { chartHost.innerHTML = ''; return; }
            var W = 520, H = 220, padL = 48, padB = 34, padT = 12, padR = 12;
            var maxRate = Math.max.apply(null, results.map(function (r) { return r.rate; }));
            var maxTp = Math.max.apply(null, results.map(function (r) { return r.throughputPerSec || 0; })) || 1;
            function x(rate) { return padL + (rate / maxRate) * (W - padL - padR); }
            function y(tp) { return H - padB - (tp / maxTp) * (H - padT - padB); }
            var pts = results.map(function (r) { return x(r.rate) + ',' + y(r.throughputPerSec || 0); }).join(' ');
            var bi = bestIndex();
            var svg = '<svg viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="Throughput against rate" width="100%">';
            // axes
            svg += '<line x1="' + padL + '" y1="' + padT + '" x2="' + padL + '" y2="' + (H - padB) + '" stroke="#b1b4b6"/>';
            svg += '<line x1="' + padL + '" y1="' + (H - padB) + '" x2="' + (W - padR) + '" y2="' + (H - padB) + '" stroke="#b1b4b6"/>';
            // axis labels
            svg += '<text x="' + padL + '" y="' + (H - padB + 16) + '" font-size="10" fill="#505a5f">0</text>';
            svg += '<text x="' + (W - padR) + '" y="' + (H - padB + 16) + '" font-size="10" fill="#505a5f" text-anchor="end">' + maxRate + ' msgs</text>';
            svg += '<text x="' + (padL - 6) + '" y="' + (padT + 8) + '" font-size="10" fill="#505a5f" text-anchor="end">' + maxTp.toFixed(0) + '</text>';
            svg += '<text x="6" y="' + (H / 2) + '" font-size="10" fill="#505a5f" transform="rotate(-90 10,' + (H / 2) + ')">msg/s</text>';
            // line + points
            svg += '<polyline points="' + pts + '" fill="none" stroke="#1d70b8" stroke-width="2"/>';
            results.forEach(function (r, i) {
                var cx = x(r.rate), cy = y(r.throughputPerSec || 0);
                svg += '<circle cx="' + cx + '" cy="' + cy + '" r="' + (i === bi ? 6 : 3) + '" fill="' + (i === bi ? '#00703c' : '#1d70b8') + '"/>';
            });
            svg += '</svg>';
            chartHost.innerHTML = svg;
        }

        function setBestHighlight() {
            var bi = bestIndex();
            var trs = rows.querySelectorAll('tr');
            trs.forEach(function (tr, i) { tr.classList.toggle('obs-loadtest__row--best', i === bi); });
            if (bi >= 0) {
                optimum.hidden = false;
                optimum.textContent = 'Optimum ≈ ' + results[bi].rate + ' messages (' +
                    results[bi].throughputPerSec.toFixed(1) + ' msg/s) before throughput stops climbing.';
            }
        }

        function runLevel(rate) {
            var headers = {
                'X-Requested-With': 'XMLHttpRequest',
                'Accept': 'application/json',
                // Required so the server parses rate from the form body (a string body would
                // otherwise be sent as text/plain and the rate would not bind).
                'Content-Type': 'application/x-www-form-urlencoded'
            };
            var t = token(modal);
            if (t) { headers['RequestVerificationToken'] = t; }
            var body = new URLSearchParams();
            body.set('rate', String(rate));
            return fetch(ENDPOINT, {
                method: 'POST', headers: headers, body: body.toString(), credentials: 'same-origin'
            }).then(function (r) { return r.ok ? r.json() : null; });
        }

        function finish(msg) {
            running = false;
            startBtn.hidden = false;
            stopBtn.hidden = true;
            exportBtn.hidden = results.length === 0;
            status.textContent = msg;
            setBestHighlight();
            drawChart();
        }

        async function run() {
            running = true; cancelled = false; results = [];
            rows.innerHTML = '';
            optimum.hidden = true;
            startBtn.hidden = true;
            stopBtn.hidden = false;
            exportBtn.hidden = true;

            var best = 0;
            for (var i = 0; i < LADDER.length; i++) {
                if (cancelled) { finish('Stopped.'); return; }
                var rate = LADDER[i];
                status.textContent = 'Driving ' + rate + ' message' + (rate === 1 ? '' : 's') + '…';
                // Show the row immediately (rate + processing…), then fill it when the batch drains.
                var tr = placeholderRow(rate);
                var r;
                try { r = await runLevel(rate); } catch (e) { r = null; }
                if (!r || !r.ok) { tr.remove(); finish('Load test failed at rate ' + rate + '.'); return; }
                results.push(r);
                fillRow(tr, r, false);
                drawChart();

                // Knee detection: stop once a level cannot keep up (some messages did not finish in
                // time) or throughput has fallen back below 90% of the best seen — past the optimum.
                if (r.throughputPerSec > best) { best = r.throughputPerSec; }
                if (r.timedOut || (best > 0 && r.throughputPerSec < best * 0.9)) {
                    finish('Reached the throughput knee.');
                    return;
                }
            }
            finish('Completed the full rate ladder.');
        }

        startBtn.addEventListener('click', function () { if (!running) { run(); } });
        stopBtn.addEventListener('click', function () { cancelled = true; });

        exportBtn.addEventListener('click', function () {
            if (!results.length) { return; }
            var headers = { 'Content-Type': 'application/json' };
            var t = token(modal);
            if (t) { headers['RequestVerificationToken'] = t; }
            fetch(EXPORT_ENDPOINT, {
                method: 'POST', headers: headers, credentials: 'same-origin',
                body: JSON.stringify({ rows: results })
            })
                .then(function (r) { return r.ok ? r.blob() : null; })
                .then(function (blob) {
                    if (!blob) { return; }
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = 'load-test.xlsx';
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    URL.revokeObjectURL(url);
                });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
