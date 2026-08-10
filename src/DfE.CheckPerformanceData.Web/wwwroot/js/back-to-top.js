// Progressive-enhancement hook for the .app-back-to-top link on CMS content pages.
// Mirrors the GDS design-guidance behaviour, minus the DOM rewrites:
//   https://design-system.service.gov.uk/community/resources-and-tools/
//
//   1. Marks <html> with data-back-to-top-init="true" so the CSS default hides the
//      link. Without JS this marker is never set and the link stays visible at the
//      end of the content flow (accessibility fallback: users can still reach it by
//      scrolling normally).
//   2. Adds .is-visible once the reader has scrolled nearly a whole viewport; removes it
//      when the user scrolls back up. Pages too short to reach that never show the link,
//      which is the GDS "not on short pages" guidance falling out of the same rule.
//   3. Uses requestAnimationFrame to coalesce scroll events so we don't do the class
//      flip on every single scroll notification (~60 Hz cap).
//   4. Idempotent: if the script is loaded more than once (bfcache, hot-reload, a
//      partial re-render that re-injects the tag), the second run finds the init
//      marker and bails without re-binding listeners.

(function () {
    'use strict';

    // Idempotence guard — re-execution finds the marker and bails.
    if (document.documentElement.getAttribute('data-back-to-top-init') === 'true') {
        return;
    }

    var containers = document.querySelectorAll('.app-back-to-top');
    if (containers.length === 0) return;

    // One rule, measured in viewports rather than pixels: the reader must have put very
    // nearly a whole screenful behind them before the link appears. Until then the top of
    // the document is still one flick of the wheel away, so offering a shortcut is noise.
    //
    // Screenfuls rather than pixels because "how far has the reader travelled" is a
    // question about screens — a pixel threshold tuned on a laptop means something quite
    // different on a phone or a tall external monitor.
    //
    // This also delivers the GDS guidance to "avoid using this component on short pages or
    // on pages designed to fit the entire viewport" without a second rule: a page that fits
    // the viewport has nothing to scroll, and a page with only a third of a screen of
    // scroll can never reach the threshold, so neither ever reveals the link.
    var REVEAL_AFTER_VIEWPORTS = 0.9;

    // Flip the initialisation marker on the document root so the corresponding CSS
    // block (which hides the link) takes effect. Non-JS environments never see this
    // marker, so the link keeps its default in-flow visible state.
    document.documentElement.setAttribute('data-back-to-top-init', 'true');

    var ticking = false;
    var wasVisible = null;
    function apply() {
        ticking = false;

        var visible = window.scrollY > window.innerHeight * REVEAL_AFTER_VIEWPORTS;

        // Skip the DOM write when the visibility state didn't change since the last frame.
        // classList.toggle('is-visible', visible) is not free on low-power devices — every
        // call triggers a style invalidation even when the class is already at the desired
        // state. During a sustained scroll rAF ticks ~60 times/second; wasVisible bookkeeping
        // reduces that to one write per state change (typically two writes per scroll gesture).
        if (visible === wasVisible) return;
        wasVisible = visible;
        for (var i = 0; i < containers.length; i++) {
            containers[i].classList.toggle('is-visible', visible);
        }
    }

    // rAF-throttle: multiple scroll events between paints collapse into one class-toggle,
    // so a fast trackpad flick doesn't fire the handler hundreds of times per second.
    function schedule() {
        if (!ticking) {
            ticking = true;
            window.requestAnimationFrame(apply);
        }
    }

    apply();
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule, { passive: true });
})();
