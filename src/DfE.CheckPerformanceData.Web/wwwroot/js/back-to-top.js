// Progressive-enhancement hook for the .app-back-to-top link on CMS content pages.
// Mirrors the GDS design-guidance behaviour, minus the DOM rewrites:
//   https://design-system.service.gov.uk/community/resources-and-tools/
//
//   1. Marks <html> with data-back-to-top-init="true" so the CSS default hides the
//      link. Without JS this marker is never set and the link stays visible at the
//      end of the content flow (accessibility fallback: users can still reach it by
//      scrolling normally).
//   2. Adds .is-visible when scrollY passes the threshold; removes it when the user
//      scrolls back to the top.
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

    // Any scroll at all reveals the link. GDS uses a slightly larger threshold to
    // avoid revealing on incidental micro-scrolls, so match that here.
    var revealThreshold = 100;

    // Flip the initialisation marker on the document root so the corresponding CSS
    // block (which hides the link) takes effect. Non-JS environments never see this
    // marker, so the link keeps its default in-flow visible state.
    document.documentElement.setAttribute('data-back-to-top-init', 'true');

    var ticking = false;
    var wasVisible = null;
    function apply() {
        ticking = false;
        var visible = window.scrollY > revealThreshold;
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
