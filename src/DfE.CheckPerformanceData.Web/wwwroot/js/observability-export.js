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

    function init() {
        var pdfBtn = document.querySelector('[data-export-pdf]');
        if (pdfBtn) { pdfBtn.addEventListener('click', exportPdf); }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.ObservabilityExport = { pdf: exportPdf };
})();
