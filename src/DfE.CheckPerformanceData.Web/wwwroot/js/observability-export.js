(function () {
    'use strict';

    // Client-side export for the dashboard. Two paths, no server round-trip and no dependency:
    //   - PNG: serialise the first chart/board SVG, draw it onto a canvas, and download the
    //     canvas as image/png via toDataURL;
    //   - PDF: window.print(), with an @media print stylesheet (in observability.css) hiding the
    //     admin chrome so only the dashboard content prints.
    // The CTA offers both; export captures only what the authenticated admin already sees.

    function downloadSvgAsPng(svg, fileName) {
        if (!svg) { return; }

        var serialized = new XMLSerializer().serializeToString(svg);
        var svgBlob = new Blob([serialized], { type: 'image/svg+xml;charset=utf-8' });
        var url = URL.createObjectURL(svgBlob);

        var rect = svg.getBoundingClientRect();
        var width = Math.max(1, Math.round(rect.width) || svg.clientWidth || 600);
        var height = Math.max(1, Math.round(rect.height) || svg.clientHeight || 200);

        var img = new Image();
        img.onload = function () {
            var canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;
            var ctx = canvas.getContext('2d');
            ctx.fillStyle = '#ffffff';
            ctx.fillRect(0, 0, width, height);
            ctx.drawImage(img, 0, 0, width, height);
            URL.revokeObjectURL(url);

            var pngUrl = canvas.toDataURL('image/png');
            var link = document.createElement('a');
            link.href = pngUrl;
            link.download = fileName || 'pipeline-dashboard.png';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        };
        img.onerror = function () { URL.revokeObjectURL(url); };
        img.src = url;
    }

    function exportPng() {
        // Prefer the board SVG if present, otherwise the first chart SVG.
        var svg = document.querySelector('[data-obs-board] svg') ||
                  document.querySelector('.obs-chart');
        downloadSvgAsPng(svg, 'pipeline-dashboard.png');
    }

    function exportPdf() {
        window.print();
    }

    function init() {
        var pngBtn = document.querySelector('[data-export-png]');
        if (pngBtn) { pngBtn.addEventListener('click', exportPng); }

        var pdfBtn = document.querySelector('[data-export-pdf]');
        if (pdfBtn) { pdfBtn.addEventListener('click', exportPdf); }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.ObservabilityExport = { png: exportPng, pdf: exportPdf };
})();
