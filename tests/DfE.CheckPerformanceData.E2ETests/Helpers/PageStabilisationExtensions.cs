using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class PageStabilisationExtensions
{
    public static async Task StabiliseAsync(this IPage page)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "* { animation: none !important; transition: none !important; }"
        });

        await page.WaitForFunctionAsync("() => document.fonts && document.fonts.ready");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
