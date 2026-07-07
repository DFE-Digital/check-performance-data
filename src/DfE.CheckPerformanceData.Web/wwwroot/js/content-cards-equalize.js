(function () {
    'use strict';

    // For every govuk-grid-row that contains dfe-card elements as direct grandchildren
    // (`.row > .col > .dfe-card`), sets every card's height to the tallest card in that row.
    // Runs after render and on resize so the tallest card in the whole row wins even when
    // cards wrap to multiple visual rows. Flex-wrap alone only equalises within one wrapped
    // row; this script pushes the equalisation across wraps too, matching the Figma design.

    function equalize() {
        document.querySelectorAll('.govuk-grid-row').forEach(function (row) {
            var cards = row.querySelectorAll(':scope > [class*="govuk-grid-column"] > .dfe-card');
            if (cards.length < 2) return;

            // Clear first so a resize (narrower viewport → more wraps → potentially taller
            // cards) can shrink AND grow the equalised height.
            cards.forEach(function (c) { c.style.height = ''; });

            var max = 0;
            cards.forEach(function (c) {
                var h = c.getBoundingClientRect().height;
                if (h > max) max = h;
            });
            cards.forEach(function (c) { c.style.height = max + 'px'; });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', equalize);
    } else {
        equalize();
    }
    window.addEventListener('resize', equalize);
})();
