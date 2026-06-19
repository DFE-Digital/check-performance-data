(function () {
    'use strict';

    // Dashboard export, two paths, no dependency:
    //   - CSV: a plain <a> download to the server endpoint (/admin/observability/export.csv) that
    //     streams the DATA behind the charts (throughput, decision-mix, dwell, the headline tiles)
    //     built from the same MetricsQueryService series — so no JS is needed for it at all.
    //   - PDF: window.print(), with an @media print stylesheet (in observability.css) hiding the
    //     admin chrome so only the dashboard content prints.
    // The old client-side chart-PNG path is gone: a single chart image was not useful — the numbers
    // are. Only the print button needs wiring here; the CSV link works without JavaScript.

    function exportPdf() {
        window.print();
    }

    // The chart "View … data table" disclosures flickered open/closed on a click: more than one
    // toggle path fired per activation (the native <details>/<summary> default plus a second
    // toggle from the surrounding init), so a single click flipped the state twice and only
    // settled after several clicks. We make this script the single source of truth: intercept the
    // summary click, suppress the native toggle (preventDefault), and flip details.open exactly
    // once behind a re-entrancy guard. Each summary is bound only once (a dataset stamp) so re-init
    // can never stack handlers. Keyboard activation still flows through the click event the browser
    // synthesises for Enter/Space on a <summary>, so accessibility is unchanged.
    function ownChartDisclosures() {
        var summaries = document.querySelectorAll('.obs-chart-panel details > summary');
        summaries.forEach(function (summary) {
            if (summary.dataset.obsDisclosureBound === 'true') { return; }
            summary.dataset.obsDisclosureBound = 'true';

            var toggling = false;
            summary.addEventListener('click', function (e) {
                // Suppress the native toggle so only our single flip runs.
                e.preventDefault();
                if (toggling) { return; }
                toggling = true;
                var details = summary.parentElement;
                if (details) { details.open = !details.open; }
                // Release on the next frame so a stray duplicate event in the same tick is dropped.
                window.requestAnimationFrame(function () { toggling = false; });
            });
        });
    }

    function init() {
        var pdfBtn = document.querySelector('[data-export-pdf]');
        if (pdfBtn) { pdfBtn.addEventListener('click', exportPdf); }
        ownChartDisclosures();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.ObservabilityExport = { pdf: exportPdf };
})();
