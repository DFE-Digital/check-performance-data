using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class PageStabilisationExtensions
{
    // NetworkIdle was previously used as the synchronisation primitive here but
    // Playwright explicitly discourages it: it never fires on pages that emit
    // long-polling, telemetry beacons, or background SignalR/WebSocket traffic,
    // and produces sporadic 500ms-plus timing variance even when the page is
    // visually stable. The combination below — load event + fonts.ready + a
    // micro-tick for the post-load layout pass — is what the Playwright team
    // recommends for visual-regression-style stability without paying for
    // application-specific waits.
    public static async Task StabiliseAsync(this IPage page)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "* { animation: none !important; transition: none !important; }"
        });

        await page.WaitForLoadStateAsync(LoadState.Load);

        await page.WaitForFunctionAsync("() => document.fonts && document.fonts.ready");

        // SeedHelpers prefixes every test wiki title with e2e-{Guid:N}-..., so any
        // visible h1 or sidebar anchor referencing a seeded page contains 32 random
        // hex characters that wrap differently between runs in a proportional font
        // — shifting every pixel below them and pushing visual diffs above threshold.
        // Replace the variable text with a fixed string so bootstrap and validation
        // see identical layout. Only affects elements whose text *contains* 'e2e-',
        // so static page chrome is untouched.
        await page.EvaluateAsync(@"() => {
            const stable = '[stable-title]';
            document.querySelectorAll('h1').forEach(h => {
                if (h.textContent && h.textContent.includes('e2e-')) {
                    h.textContent = stable;
                }
            });
            document.querySelectorAll('aside.wiki-sidebar a').forEach(a => {
                if (a.textContent && a.textContent.includes('e2e-')) {
                    a.textContent = stable;
                }
            });
        }");

        // One animation frame after the DOM mutation lets the browser commit the
        // post-load layout pass (reflow from late-loading CSS, deferred font swaps,
        // and the text replacement above) before the screenshot. Deterministic;
        // bounded; no polling.
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }
}
