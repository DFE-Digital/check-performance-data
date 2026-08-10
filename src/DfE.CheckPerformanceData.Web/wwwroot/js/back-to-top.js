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

    // The reader must have put nearly a whole screenful behind them before the link
    // appears — until then the top of the document is one flick of the wheel away, so a
    // shortcut to it is noise. Measured in screenfuls rather than pixels because "how far
    // has the reader travelled" is a question about screens: a pixel threshold tuned on a
    // laptop means something quite different on a phone or a tall external monitor.
    var REVEAL_AFTER_VIEWPORTS = 0.9;

    // ...but a screenful alone is not enough, because a tall window is penalised twice: the
    // threshold goes up while the same document's scrollable distance goes down. A page of
    // ~2000px is 1.3 screens of scrolling on a 900px window and only 0.6 of one at 1300px,
    // so a fixed fraction of the viewport silently stops being reachable on big monitors —
    // the component disappears from most of the site for anyone with a large screen.
    //
    // So cap the threshold at a fraction of what the page actually offers. Long articles
    // still reveal after a screenful (the cap is far away); pages that only just outrun the
    // window reveal halfway down instead of never.
    var REVEAL_AFTER_PAGE_FRACTION = 0.5;

    // Below this there is not enough page to be worth a shortcut — the GDS guidance to
    // "avoid using this component on short pages or on pages designed to fit the entire
    // viewport". Two floors, and the page has to clear both:
    //
    //   * half a screen, so the judgement scales with the reader's window;
    //   * an absolute distance, because "is scrolling back to the top a chore?" is a
    //     question about travel, not about screens. Without it, a page that fits a desktop
    //     window comfortably becomes 1.8 screens on a small laptop and starts showing a
    //     link it has no business showing — the very complaint this component was reported
    //     for. 600px is about two-thirds of a laptop screen.
    //
    // These are the only pixel-denominated numbers here, and deliberately so: the reveal
    // threshold answers "how far has the reader come" (screens), this answers "is there
    // enough page to bother" (distance).
    var MIN_SCROLLABLE_VIEWPORTS = 0.5;
    var MIN_SCROLLABLE_PIXELS = 600;

    // Flip the initialisation marker on the document root so the corresponding CSS
    // block (which hides the link) takes effect. Non-JS environments never see this
    // marker, so the link keeps its default in-flow visible state.
    document.documentElement.setAttribute('data-back-to-top-init', 'true');

    var ticking = false;
    var wasVisible = null;
    function apply() {
        ticking = false;

        // Layout reads first, DOM writes after, so the frame costs one style recalc rather
        // than one per container. scrollHeight is re-read every time rather than cached at
        // start-up: content that grows or shrinks after load (lazy images settling, a
        // <details> being opened, a filter collapsing a list) moves the threshold, and
        // neither of those fires a resize event.
        var viewport = window.innerHeight;
        var scrollable = document.documentElement.scrollHeight - viewport;

        var worthShowing = scrollable > Math.max(viewport * MIN_SCROLLABLE_VIEWPORTS,
                                                MIN_SCROLLABLE_PIXELS);
        var threshold = Math.min(viewport * REVEAL_AFTER_VIEWPORTS,
                                 scrollable * REVEAL_AFTER_PAGE_FRACTION);
        var visible = worthShowing && window.scrollY > threshold;

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
