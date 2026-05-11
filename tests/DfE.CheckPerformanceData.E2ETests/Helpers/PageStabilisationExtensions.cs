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

        // One animation frame after load lets the browser commit the post-load
        // layout pass (reflow from late-loading CSS, deferred font swaps) before
        // the screenshot. Deterministic; bounded; no polling.
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }
}
