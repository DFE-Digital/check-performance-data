using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Visual;

[Collection("E2E")]
[Trait("Category", "VisualRegression")]
public sealed class ConfirmModalVisualTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private string _slug = "";
    private int _pageId;

    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } };

    protected override async Task SeedAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Snapshots are Linux-only; skip the seed work on Windows/macOS so dev runs are fast.
            return;
        }

        var (id, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            Fixture.SeedClient,
            title: "modal-snapshot",
            body: "Page body for modal snapshot.",
            parentId: null,
            TrackedIds);

        _pageId = id;
        _slug = slug;
    }

    [SkippableFact]
    public async Task OpenDestructiveModal_MatchesSnapshot()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/{_slug}?edit");
        await Page.Locator($"[data-confirm-trigger='confirm-delete-{_pageId}']").ClickAsync();
        await Page.WaitForFunctionAsync(
            $"() => document.getElementById('confirm-delete-{_pageId}').matches(':modal')");

        await Page.StabiliseAsync();

        // SeedHelpers prefixes every test page title with e2e-{Guid:N}- to prevent
        // seed-collision; replace the GUID-bearing strings with stable placeholders so
        // the snapshot captures layout/colours/chrome rather than per-run text.
        await Page.EvaluateAsync(@"
            () => {
                document.querySelectorAll('h1, h2, h3, p, .govuk-heading-l, .govuk-heading-m, .govuk-heading-s, .govuk-body').forEach(el => {
                    if (el.textContent && /e2e-[a-f0-9]{32}/i.test(el.textContent)) {
                        el.textContent = el.textContent.replace(/e2e-[a-f0-9]{32}\s*/gi, 'STABLE-FIXTURE ');
                    }
                });
                document.querySelectorAll('a').forEach(el => {
                    if (el.textContent && /e2e-[a-f0-9]{32}/i.test(el.textContent)) {
                        el.textContent = el.textContent.replace(/e2e-[a-f0-9]{32}\s*/gi, 'STABLE-FIXTURE ');
                    }
                });
            }");

        await Page.MatchSnapshotAsync("confirm-modal-open-destructive.png");
    }
}
